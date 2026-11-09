# HSE System

Sistem HSE & TPS **PT. Bonecom Tricom**: temuan Safety & 5R Patrol (foto langsung dari web) dan penilaian
improvement otomatis untuk plant KIMU, TEGAL, BTI (Plant 5), dan BTP (Plant 1).

- **Backend**: ASP.NET Core (.NET 10), minimal API, SQL Server (Dapper).
- **Frontend**: Angular 21 + PrimeNG 21 dengan template [Sakai](https://github.com/primefaces/sakai-ng); semua tabel memakai AG Grid Community.
- **Login**: cookie HttpOnly; **peran dibuat bebas oleh admin** dengan mencentang hak akses.

## Struktur

```
backend/HseSystem.slnx
  src/HseSystem.Api/
    Auth/                       login, sesi, hak akses (Permissions.cs), Administrator pertama
    Admin/                      API peran & pengguna
    Master/                     plant, area, departemen
    Patrol/                     temuan, perbaikan, verifikasi, rekap dashboard
    Images/                     upload foto: POST /api/images, foto di /uploads/... (wajib login)
  tests/HseSystem.Tests/        xUnit v3: unit + integrasi API dengan database uji sementara
frontend/src/app/
  core/
    models/                     image-response, auth, admin, temuan
    services/                   image-upload, auth, admin, temuan
    guards/ interceptors/ utils/
  shared/components/            image-uploader, camera-capture, image-preview, confirm-dialog, data-grid, detail-modal
  modules/
    auth/                       login, ganti password
    admin/                      pengguna, peran & hak akses, master data
    dashboard/                  angka ringkas + grafik per bulan
    temuan/                     form, daftar, detail (perbaikan & verifikasi)
  layout/                       sidebar, topbar, mode gelap (dari template Sakai)
db/                             skrip SQL berurutan (dijalankan manual lewat DBeaver)
```

## Database (SQL Server)

### Server perusahaan (sekali saja)

1. `db/001_buat_database_dan_login.sql`: jalankan sebagai **sysadmin**. Isi dulu password login `hse_app`
   (lihat petunjuk di kepala skrip), lalu tutup file **tanpa menyimpan**. Hasilnya database `web_hse` dan login
   `hse_app` yang hanya bisa baca/tulis data.
2. `db/002_auth_dan_master.sql`: jalankan di database **web_hse**. Membuat tabel pengguna, peran, hak akses,
   dan master 4 plant. Aman dijalankan ulang.
3. `db/003_temuan_patrol.sql`: jalankan di database **web_hse**. Membuat tabel temuan, foto, riwayat, master area,
   dan departemen. Aman dijalankan ulang.
4. `db/004_penugasan_departemen.sql`: jalankan di database **web_hse**. Menambah departemen pengguna, status
   Ditugaskan, dan jadwal genba. Aman dijalankan ulang.
5. `db/005_kpi_k3.sql`: jalankan di database **web_hse**. Membuat tabel KPI K3 dan daftar departemen KPI tiap plant.
   Aman dijalankan ulang. Setelah itu centang hak *Lihat KPI*, *Input KPI departemen*, atau *Kelola semua KPI*
   pada peran yang memerlukan di **Peran & Hak Akses**.

### Pengaturan aplikasi (tidak disimpan di repo)

| Kunci | Contoh | Keterangan |
|---|---|---|
| `ConnectionStrings:Hse` | `Server=192.168.3.250;Database=web_hse;User ID=hse_app;Password=...;TrustServerCertificate=true` | koneksi aplikasi |
| `Bootstrap:AdminUsername` | `admin` | Administrator pertama, dibuat **hanya kalau belum ada pengguna** |
| `Bootstrap:AdminPassword` | (password kuat) | wajib diganti saat login pertama; **hapus** setelah itu |

- Development: `dotnet user-secrets set "ConnectionStrings:Hse" "..." --project src/HseSystem.Api`
- Server/IIS: environment variable `ConnectionStrings__Hse`, `Bootstrap__AdminUsername`, `Bootstrap__AdminPassword`.

**Laptop development ini sudah disiapkan**: database `web_hse` di SQL Server LocalDB, dan ketiga pengaturan di atas
sudah ada di user-secrets. Untuk melihat password admin awal:
`cd backend; dotnet user-secrets list --project src/HseSystem.Api`.

## Login dan peran

- Login pertama Administrator dan setiap pengguna baru **wajib ganti password**. Sebelum diganti, hak aksesnya kosong.
- Salah password 5 kali berturut-turut → akun terkunci 5 menit. Admin bisa membukanya lewat **Reset Password**.
- Menu **Administrasi → Peran & Hak Akses**: buat peran (mis. Patroller, PIC Perbaikan, Admin HSE) lalu centang
  hak aksesnya. Peran **Administrator** otomatis punya semua hak dan tidak bisa diubah atau dihapus.
- Menu **Administrasi → Pengguna**: tambah pengguna, atur peran (boleh lebih dari satu), plant, aktif/nonaktif,
  dan reset password.
- Perubahan peran, nonaktif, dan reset password **berlaku seketika**, tanpa pengguna perlu login ulang.
- Hak akses baru untuk fitur baru: tambahkan di `backend/.../Auth/Permissions.cs` dan `frontend/.../core/models/auth.model.ts`.

## Alur temuan

1. **Persiapan admin**: isi area per plant di **Master Data**, dan pasang **departemen** serta plant pada tiap
   pengguna. Tanggal genba diatur di **Jadwal Genba**: satu tanggal per plant per bulan.
2. **Patroller** (hak *Input temuan*) mencatat foto, detail, area, dan tipe. Status **Open**, nomor otomatis
   (mis. `BTI-202611-001`).
3. **Tim HSE** (hak *Tentukan departemen*) membuka temuan Open lalu menentukan **departemen, PIC, dan due date**.
   Pilihan PIC hanya berisi user departemen itu di plant temuan. Status **Ditugaskan**. Selama belum dikerjakan,
   penugasan masih bisa diubah.
4. **User departemen itu** (hak *Input perbaikan*) mengisi countermeasure dan foto sesudah, lalu **Simpan Progres**
   atau **Ajukan Selesai**. PIC adalah penanggung jawabnya; rekan satu departemen juga bisa mengisi, dan namanya
   tercatat sebagai "Dikerjakan oleh".
5. **Pemeriksa** (hak *Verifikasi temuan*) menekan **Setujui & Close**, atau **Tolak** dengan alasan (Not Close,
   kembali ke departemen). Orang yang memperbaiki tidak bisa memeriksa perbaikannya sendiri.
6. Lewat due date dan belum Close = **Overdue** otomatis. Dashboard merekap semuanya tanpa input manual.

Cakupan data: pengguna melihat temuan di plant-nya; hak *Semua plant* melihat semuanya; user departemen yang hanya
punya *Input perbaikan* melihat temuan departemennya di plant-nya saja, dan temuan Open belum muncul untuknya.

## KPI K3

Setiap departemen di tiap plant wajib melapor tiga hal setiap bulan:

1. **Summary KPI**: isi kolom kuning (jumlah accident, kasus penyakit, keterangan). Persen dan evaluasi terhitung
   otomatis dengan rumus file master. Tab **ALL** menjumlahkan semua departemen. KPI 3 (temuan) diambil dari Daftar Temuan.
2. **Kalender Safety**: unggah PDF yang sudah disetujui.
3. **PPT P2K3L**: unggah file PPT. Kalau ada kasus penyakit atau kecelakaan, isi juga **PICA**.

**Monitoring KPI** merekap angka tiap plant dan departemen yang belum melapor. Departemen yang wajib melapor diatur di
**Master Data → Departemen KPI per Plant**.

## Menjalankan saat development

```powershell
# 1. API → http://localhost:5190
cd backend
dotnet run --project src/HseSystem.Api --launch-profile http

# 2. Angular → http://localhost:4200 (proxy /api dan /uploads ke 5190)
cd frontend
pnpm install
pnpm start
```

## Build untuk server

```powershell
cd frontend; pnpm build                      # hasil masuk ke backend/src/HseSystem.Api/wwwroot
cd ../backend; dotnet publish src/HseSystem.Api -c Release
```

## Tes

```powershell
cd backend;  dotnet test --solution HseSystem.slnx     # butuh SQL Server LocalDB (atau env HSE_TEST_SQL)
cd frontend; pnpm exec ng test --watch=false
```

Tes API membuat database uji `hse_test_xxx` sementara, menjalankan `db/002...`, lalu menghapusnya. Kalau SQL Server
tidak tersedia, tes API dilewati (skip), bukan gagal.

## Pengaturan foto (`appsettings.json` → `ImageStorage`)

| Kunci | Default | Keterangan |
|---|---|---|
| `RootPath` | `App_Data/images` | Folder foto. Di server arahkan ke disk/NAS khusus, mis. `D:\HSE-Storage\images`. |
| `RequestPath` | `/uploads` | Awalan URL foto. Jangan `/media` (dipakai Angular untuk font). |
| `MaxBytes` | `10485760` | Batas satu foto (10 MB). Foto sudah diperkecil browser jadi ±200–400 KB. |

Foto disimpan sebagai `{kategori}/{tahun}/{bulan}/{guid}.jpg` dan hanya bisa dibuka pengguna yang sudah login.
Yang disimpan ke database adalah `path`-nya.

## Pengaturan dokumen KPI (`appsettings.json` → `DocumentStorage`)

| Kunci | Default | Keterangan |
|---|---|---|
| `RootPath` | `App_Data/documents` | Folder Kalender Safety dan PPT. Di server arahkan ke disk/NAS khusus. |
| `MaxBytes` | `52428800` | Batas satu file (50 MB). |

## Kamera di HP: wajib HTTPS

Kamera di dalam web (`getUserMedia`) hanya diizinkan browser lewat **HTTPS** atau `localhost`. Kalau dibuka lewat
`http://IP-server`, komponen otomatis memakai kamera bawaan HP. Cara itu tetap jalan, tetapi sebagian merek HP
menyimpan salinan foto ke galeri. Jadi server produksi harus memakai HTTPS (sertifikat internal atau domain).
