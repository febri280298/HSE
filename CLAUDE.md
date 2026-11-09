# HSE System — panduan kerja

Sistem HSE & TPS untuk **PT. Bonecom Tricom (BTI)**, dipakai internal. Bahasa komunikasi dengan user: **Indonesia**.
Menggantikan alur Excel bulanan "5R & Safety Patrol" (spreadsheet dashboard + folder per plant di Google Drive).

## Tujuan (dari user)

1. **Sistem temuan** Safety & 5R Patrol per plant (KIMU, TEGAL, BTI Plant 5, BTP Plant 1), dilakukan tiap bulan.
2. **Foto langsung dari web**, tanpa tersimpan di HP atasan → kamera di dalam halaman (`getUserMedia`).
3. **Penilaian improvement otomatis** dari sistem. **Ditunda user (8 Nov)**; kriteria belum ditetapkan, tanyakan dulu.
4. **Database SQL Server + login dengan peran yang bisa diatur sendiri** (8 Nov, selesai).

Kolom laporan Excel lama: No, Foto temuan, Detail, Area, Type (Safety/5R), Dept, PIC, Due date,
Foto sesudah perbaikan, Countermeasure, Evaluation (C = close, NC = not close, P = pending), tanda tangan
Prepared/Checked/Approved (sheet "Laporan PD").

## Keputusan teknis

- Backend .NET 10, pola sama dengan `D:\.NET\NewBps`: minimal API per fitur, Dapper + Microsoft.Data.SqlClient,
  `Directory.Packages.props`, `.slnx`, xUnit v3.
- **Database `web_hse`**, login aplikasi `hse_app` (hanya db_datareader + db_datawriter). Perubahan struktur lewat
  skrip `db/` yang **dijalankan user** (DBeaver, satu blok Ctrl+A Ctrl+Enter, aman diulang). Skema `auth` dan `mst`.
- **Peran custom**: hak akses = daftar tetap di kode (`Auth/Permissions.cs`, sama di frontend `auth.model.ts`);
  admin membuat peran bebas dan mencentang hak. Peran `IsSystem` (Administrator) = semua hak, tidak bisa
  diubah/dihapus; minimal satu Administrator aktif harus tersisa.
- Sesi: cookie `hse.auth` (HttpOnly, SameSite=Strict) hanya berisi id + SecurityStamp. Setiap request
  `OnValidatePrincipal` memuat keadaan pengguna (cache 30 detik, di-invalidate saat admin mengubah data) → peran,
  nonaktif, dan reset password berlaku seketika. Selama `MustChangePassword`, hak akses kosong.
- Administrator pertama dibuat dari `Bootstrap:AdminUsername/AdminPassword` hanya kalau tabel pengguna kosong.
- Frontend Angular 21 + **PrimeNG 21 (tema Aura) dengan template Sakai** (user pilih 8 Nov, menggantikan ngx-admin/Nebular),
  Tailwind 4, font Inter. Kerangka di `src/app/layout`, gaya Sakai yang sudah diubah di `src/assets/layout` (lisensi MIT:
  `LICENSE-sakai.md`), gaya aplikasi di `src/assets/app.scss`. Sidebar sengaja gelap dan bisa diringkas jadi ikon, permintaan user.
  Dialog lewat `ModalService` (data dibaca dari `DynamicDialogConfig`), notifikasi lewat `NotifyService`, ikon PrimeIcons.
- Struktur frontend dari user: `core/{services,models}`, `shared/components/...`, `modules/<modul>/...`.
  Nama file gaya 2016 (`*.component.ts`).
- Foto: browser memperkecil (maks. 1600 px, JPEG, EXIF/GPS terbuang, cap waktu) → `POST /api/images`
  (hak `temuan.input`) → `{kategori}/{yyyy}/{MM}/{guid v7}.jpg` → `/uploads/...` (wajib login, cache private).

- Temuan (`Patrol/`, skema `patrol`), alur dari user 8 Nov: patroller mencatat (Open) → **tim HSE menentukan departemen
  + PIC + due date** (Ditugaskan) → **user departemen itu** mengerjakan (OnProgress → Verifikasi) → Close, atau NotClose
  kembali ke departemen. **PIC wajib ada** (user minta tetap ada, 8 Nov): user departemen itu, di plant temuan, dengan
  hak Input perbaikan. PIC = penanggung jawab; perbaikan boleh diisi rekan sedepartemen, dikonfirmasi user 8 Nov (tercatat di RepairedBy).
  Overdue dihitung, tidak disimpan (Open tanpa due date tidak overdue). Yang memperbaiki tidak boleh memeriksa sendiri.
  Cakupan di `PatrolScope`. Path foto divalidasi (`ImageStorage.IsStored`) dan satu file hanya untuk satu temuan.
- Pengguna punya satu departemen (`auth.Users.DeptId`). Genba sebulan sekali: `patrol.GenbaSchedules` unik per
  plant-bulan; jadwal hanya informasi, tidak membatasi input temuan.
- Dashboard: API memberi baris per plant/bulan/tipe, frontend menjumlahkan (`dashboard-data.ts`). Warna grafik
  `--viz-series-*` di `assets/app.scss`, tervalidasi skill dataviz untuk mode terang dan gelap.
- **Semua tabel memakai AG Grid Community 36.2.0** (permintaan user 8 Nov) lewat `shared/components/data-grid`, jangan pakai
  `<table>` baru. Setiap tabel punya tombol mata (`detailAction`) yang membuka modal: `openDetail` untuk data biasa,
  `openFindingDetail` untuk temuan. Grid memuat semua baris di browser, jadi daftar temuan mengambil sampai 5000 baris
  (`PageSize` dibatasi 5000 di `PatrolStore`). Isi sel dibuat lewat DOM (`grid-cells.ts`), bukan string HTML dari data;
  gayanya global di `assets/app.scss`.

## Perintah

- Backend: `cd backend; dotnet build HseSystem.slnx` / `dotnet test --solution HseSystem.slnx`
  (matikan API dulu, DLL terkunci saat API jalan).
- Frontend: `cd frontend; pnpm build` (output ke `backend/src/HseSystem.Api/wwwroot`), `pnpm exec ng test --watch=false`.
- Jalankan: `dotnet run --project src/HseSystem.Api --launch-profile http` → http://localhost:5190;
  dev Angular `pnpm start` → http://localhost:4200.
- Dev laptop: LocalDB `(localdb)\MSSQLLocalDB`, database `web_hse`; connection + bootstrap admin di user-secrets
  (`dotnet user-secrets list --project src/HseSystem.Api`). Jangan tampilkan password di chat.
- Tes API memakai LocalDB (atau env `HSE_TEST_SQL`); database `hse_test_*` dibuat lalu dihapus otomatis.

## Jebakan yang sudah ketemu

- Jangan pakai alamat foto `/media`: Angular menaruh font (`@fontsource`) di `wwwroot/media`.
- Jangan tambahkan `MapFallback("/uploads/{**rest}")`: endpoint dicocokkan sebelum `UseStaticFiles`, foto jadi 404.
- `provideHttpClient()` **tanpa** `withFetch()`: fetch tidak punya progres upload.
- Handler minimal API yang hanya menerima `HttpContext` dibaca sebagai `RequestDelegate` dan hasilnya dibuang
  (ASP0016) → cast ke `Delegate` (lihat logout di `AuthEndpoints`).
- `permissionGuard` mengarahkan ke `/dashboard?akses=ditolak`; pesannya ditampilkan di dashboard, bukan toast dari guard.
- Tombol yang membuka dialog berisi data (Tambah Peran/Pengguna) dinonaktifkan sampai data selesai dimuat.
- Aturan password menolak password yang memuat username (password uji "AdminUji..." untuk user `admin` ditolak).
- Menghentikan `dotnet run` lewat TaskStop **tidak** mematikan proses `HseSystem.Api`; matikan proses itu juga.
- Properti non-nullable pada `[AsParameters]` dianggap wajib (400 kalau tidak dikirim); pakai nullable (`FindingQuery`).
- Dapper belum mendukung `DateOnly` sebagai parameter: konversi ke `DateTime` di `PatrolStore`.
- Uji kamera otomatis: Chrome `--use-fake-ui-for-media-stream --use-fake-device-for-media-stream`.
- pnpm di Windows memakai junction berpath absolut: setelah memindah folder, hapus `node_modules` dulu lalu `pnpm install`.
- Tailwind membuat class `.grid`, `.hidden`, dll. secara global; class komponen dengan nama sama harus punya aturan `display` sendiri.
- Input di dalam flex butuh `min-width: 0` supaya bisa menyusut di bawah lebar bawaannya.
- Kamera di dalam web hanya jalan di HTTPS/localhost; di `http://IP` jatuh ke kamera bawaan HP (bisa masuk galeri).
- AG Grid belum punya locale Indonesia: label ada di `grid-locale.ts`. Tinggi baris custom lewat parameter tema
  (input `[rowHeight]` membuat teks tidak sejajar tengah).
- DOM AG Grid 36 berbeda dari versi lama: baris ada di `.ag-grid-scrolling-container`, sel pinned satu baris dengan sel
  biasa (tidak ada `.ag-center-cols-container`). Scrollbar overlay di HP menutupi baris terakhir; sudah diatasi di `assets/app.scss`.

## Status dan langkah berikutnya

Status 8 Nov 2026: fondasi, foto, database, login + peran, master area/departemen, penugasan departemen, jadwal genba,
alur temuan sampai Close, overdue, dan dashboard selesai. Dashboard setara sheet PROGRESS & STATISTIC (per plant Safety/5R, status, departemen, overdue terlama, genba bulan ini);
Modul KPI K3 selesai 9 Nov (`db/005`, `backend/.../Kpi`, `frontend/.../modules/kpi`): rumus mengikuti file MASTER KPI SUMMARY di `KpiCalculator`, KPI 3 otomatis dari temuan, Kalender dan PPT berupa unggahan file. Sengaja beda dari Excel: ACT ACCUMULATION penyakit memakai kumulatif sebenarnya (rumus Excel tidak konsisten; persen dan evaluasi tidak terpengaruh). Semua tabel sudah AG Grid + modal detail. Layar bidik webcam/kamera depan sengaja seperti cermin (permintaan user 8 Nov), hanya tampilan: foto tersimpan tetap asli supaya tulisan terbaca.
Tes: backend 104 (termasuk integrasi database), frontend 33.
Alur diuji di Chrome headless dengan kamera palsu; kamera HP sungguhan belum dicoba. Di server perusahaan skrip `db/`
belum dijalankan (baru di LocalDB laptop).

Semua halaman sudah di template Sakai (8 Nov), `frontend` adalah versi ini. Versi lama disimpan di `frontend-lama-nebular` (tanpa `node_modules`) sebagai
cadangan karena belum ada commit; boleh dihapus kalau user sudah yakin. Logo perisai di `layout/brand-mark.component.ts` masih buatan sendiri, belum logo resmi perusahaan.

Data contoh hanya di LocalDB `web_hse`: 267 temuan Jan-Nov 2026 + jadwal genba Jan-Des, dibuat oleh `patroller.contoh`, akun `patroller.contoh`, `tim.hse`, dan `dept.<kode>` tiap departemen,
password di user-secrets `Contoh:Password`. Nama dan fotonya fiktif; dibuat lewat skrip sementara, bukan bagian `db/`.

Berikutnya: form Kalender Safety digital, impor data KPI dari file master, notifikasi ke departemen, laporan PDF (Prepared/Checked/Approved), HTTPS di server, impor data Jan–Okt 2026,
pembersihan foto yatim. Penilaian improvement menunggu kriteria dari user.
