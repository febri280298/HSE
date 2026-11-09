namespace HseSystem.Api.Auth;

public sealed record PermissionInfo(string Code, string Group, string Name, string Description);

/// <summary>
/// Daftar hak akses. Peran (role) dibuat bebas oleh admin dengan mencentang hak dari daftar ini.
/// Fitur baru = tambah konstanta + entri di <see cref="All"/>, lalu pasang di endpoint:
/// <c>.RequireAuthorization(Permissions.Xxx)</c>. Peran Administrator otomatis mendapat hak baru.
/// </summary>
public static class Permissions
{
    /// <summary>Jenis claim untuk hak akses pada pengguna yang login.</summary>
    public const string ClaimType = "hse:perm";

    public const string TemuanLihat = "temuan.lihat";
    public const string TemuanInput = "temuan.input";
    public const string PerbaikanInput = "perbaikan.input";
    public const string TemuanVerifikasi = "temuan.verifikasi";
    public const string TemuanSemuaPlant = "temuan.semua-plant";
    public const string TemuanTugaskan = "temuan.tugaskan";
    public const string JadwalKelola = "jadwal.kelola";
    public const string KpiLihat = "kpi.lihat";
    public const string KpiInput = "kpi.input";
    public const string KpiKelola = "kpi.kelola";
    public const string PenggunaKelola = "pengguna.kelola";
    public const string PeranKelola = "peran.kelola";
    public const string MasterKelola = "master.kelola";

    /// <summary>Kebijakan gabungan: boleh membuka daftar temuan (cakupannya dibatasi di <c>PatrolScope</c>).</summary>
    public const string TemuanAkses = "kebijakan.temuan-akses";

    /// <summary>Kebijakan gabungan: boleh mengupload foto (mencatat temuan atau mengisi perbaikan).</summary>
    public const string FotoUpload = "kebijakan.foto-upload";

    /// <summary>Kebijakan gabungan: boleh membuka menu KPI K3 (cakupannya dibatasi di <c>KpiScope</c>).</summary>
    public const string KpiAkses = "kebijakan.kpi-akses";

    private const string Patrol = "Safety & 5R Patrol";
    private const string Kpi = "KPI K3";
    private const string Admin = "Administrasi";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(TemuanLihat, Patrol, "Lihat temuan", "Melihat daftar dan detail temuan di plant sendiri."),
        new(TemuanInput, Patrol, "Input temuan", "Mencatat temuan baru beserta foto."),
        new(TemuanTugaskan, Patrol, "Tentukan departemen", "Tim HSE menentukan departemen dan due date temuan baru."),
        new(PerbaikanInput, Patrol, "Input perbaikan", "User departemen mengisi countermeasure dan foto sesudah perbaikan, untuk temuan departemennya."),
        new(JadwalKelola, Patrol, "Kelola jadwal genba", "Mengatur tanggal genba tiap plant, sebulan sekali."),
        new(TemuanVerifikasi, Patrol, "Verifikasi temuan", "Memeriksa perbaikan lalu menutup (close) temuan."),
        new(TemuanSemuaPlant, Patrol, "Semua plant", "Melihat temuan semua plant, tidak hanya plant sendiri."),
        new(KpiLihat, Kpi, "Lihat KPI", "Melihat Summary KPI, Kalender Safety, PPT, dan PICA di plant sendiri."),
        new(KpiInput, Kpi, "Input KPI departemen", "Mengisi KPI bulanan, mengunggah Kalender Safety dan PPT, serta PICA untuk departemen sendiri."),
        new(KpiKelola, Kpi, "Kelola semua KPI", "Tim HSE: melihat semua plant dan mengisi KPI untuk semua departemen."),
        new(PenggunaKelola, Admin, "Kelola pengguna", "Menambah dan mengubah pengguna, reset password, mengatur peran dan plant."),
        new(PeranKelola, Admin, "Kelola peran", "Membuat peran dan mengatur hak aksesnya."),
        new(MasterKelola, Admin, "Kelola master data", "Mengatur daftar area dan departemen."),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(p => p.Code)];

    public static bool Exists(string code) => Codes.Contains(code);
}
