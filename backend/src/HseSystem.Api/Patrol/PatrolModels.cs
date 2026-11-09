using HseSystem.Api.Auth;

namespace HseSystem.Api.Patrol;

public static class FindingStatus
{
    /// <summary>Baru dicatat; tim HSE belum menentukan departemen.</summary>
    public const string Open = "Open";
    public const string Ditugaskan = "Ditugaskan";
    public const string OnProgress = "OnProgress";
    public const string Verifikasi = "Verifikasi";
    public const string NotClose = "NotClose";
    public const string Close = "Close";

    /// <summary>Status yang masih boleh diisi perbaikannya oleh departemen.</summary>
    public static bool CanRepair(string status) => status is Ditugaskan or OnProgress or NotClose;

    /// <summary>Departemen masih boleh ditentukan atau diganti (belum mulai dikerjakan).</summary>
    public static bool CanAssign(string status) => status is Open or Ditugaskan;
}

public static class FindingType
{
    public const string Safety = "Safety";
    public const string FiveR = "5R";

    public static bool IsValid(string? type) => type is Safety or FiveR;
}

public static class PhotoKind
{
    public const string Temuan = "Temuan";
    public const string Perbaikan = "Perbaikan";
}

public sealed record PhotoRef(string? Path, string? FileName);

public sealed record CreateFindingRequest(int PlantId, DateOnly? PatrolDate, string? Detail, int AreaId, string? Type, PhotoRef[]? Photos);

public sealed record AssignRequest(int DeptId, int? PicUserId, DateOnly? DueDate);

/// <param name="Selesai"><c>false</c> = simpan progres saja; <c>true</c> = ajukan untuk diperiksa.</param>
public sealed record RepairRequest(string? Countermeasure, PhotoRef[]? Photos, bool Selesai);

public sealed record VerifyRequest(bool Setuju, string? Catatan);

public sealed class FindingQuery
{
    public int? PlantId { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }

    // Semua nullable: properti non-nullable pada [AsParameters] dianggap wajib dan dijawab 400 kalau tidak dikirim.
    public bool? Overdue { get; set; }
    public bool? Mine { get; set; }
    public bool? Pic { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record FindingListItem(
    int FindingId,
    string Number,
    string PlantCode,
    string PlantName,
    DateOnly PatrolDate,
    string Type,
    string Detail,
    string AreaName,
    string? DeptCode,
    string? PicName,
    DateOnly? DueDate,
    string Status,
    bool IsOverdue,
    string? ThumbnailUrl);

public sealed record FindingPhotoResponse(string Kind, string Path, string Url, string FileName);

public sealed record FindingEventResponse(string Action, string? Note, string UserName, DateTimeOffset CreatedAt);

public sealed record FindingDetail(
    int FindingId,
    string Number,
    int PlantId,
    string PlantCode,
    string PlantName,
    DateOnly PatrolDate,
    string Type,
    string Detail,
    string AreaName,
    int? DeptId,
    string? DeptCode,
    string? DeptName,
    int? PicUserId,
    string? PicName,
    DateOnly? DueDate,
    string Status,
    bool IsOverdue,
    string? Countermeasure,
    string? RepairedByName,
    DateTimeOffset? RepairedAt,
    string? VerifiedByName,
    DateTimeOffset? VerifiedAt,
    string? VerifyNote,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    bool CanAssign,
    bool CanRepair,
    bool CanVerify,
    IReadOnlyList<FindingPhotoResponse> Photos,
    IReadOnlyList<FindingEventResponse> Events);

public sealed record AreaOption(int AreaId, string Name);

public sealed record DepartmentOption(int DeptId, string Code, string Name);

public sealed record PicOption(int UserId, string DisplayName, int DeptId);

/// <param name="Pics">Calon PIC di plant yang diminta; hanya diisi untuk pengguna dengan hak menentukan departemen.</param>
public sealed record FindingOptions(
    IReadOnlyList<PlantInfo> Plants,
    IReadOnlyList<AreaOption> Areas,
    IReadOnlyList<DepartmentOption> Departments,
    IReadOnlyList<PicOption> Pics);

/// <summary>Satu baris rekap: per plant, per bulan, per tipe. Frontend menjumlahkan sesuai tampilan.</summary>
public sealed record DashboardRow(
    int PlantId, string PlantCode, string PlantName, int Month, string Type, int Total, int Closed, int Overdue, int ClosedOnTime);

/// <summary>Rekap per departemen. Departemen kosong = temuan yang belum ditugaskan.</summary>
public sealed record DashboardDeptRow(int PlantId, string Type, string? DeptCode, string? DeptName, int Total, int Closed, int Overdue);

public sealed record DashboardStatusRow(int PlantId, string Type, string Status, int Total);

public sealed record DashboardOverdueItem(
    int FindingId, string Number, int PlantId, string Type, string Detail, string? DeptCode, string? PicName, DateOnly DueDate, int DaysLate);

public sealed record DashboardResponse(
    int Year,
    IReadOnlyList<DashboardRow> Rows,
    IReadOnlyList<DashboardDeptRow> Departments,
    IReadOnlyList<DashboardStatusRow> Statuses,
    IReadOnlyList<DashboardOverdueItem> Overdue);

public sealed record GenbaScheduleItem(int PlantId, int Month, DateOnly PlannedDate);

public sealed record GenbaScheduleRequest(int PlantId, int Year, int Month, DateOnly? PlannedDate);

/// <summary>
/// Temuan mana yang boleh dilihat pengguna: plant sendiri, semua plant (hak <c>temuan.semua-plant</c>),
/// atau hanya temuan departemennya (user departemen yang punya <c>perbaikan.input</c> saja).
/// </summary>
public sealed record PatrolScope(
    int UserId, int? DeptId, IReadOnlyList<int> PlantIds, bool AllPlants, bool OnlyDept, IReadOnlySet<string> Permissions)
{
    public static PatrolScope From(UserSession session)
    {
        var permissions = session.EffectivePermissions.ToHashSet(StringComparer.Ordinal);
        return new PatrolScope(
            session.UserId,
            session.DeptId,
            [.. session.Plants.Select(p => p.PlantId)],
            permissions.Contains(Auth.Permissions.TemuanSemuaPlant),
            !permissions.Contains(Auth.Permissions.TemuanLihat)
                && !permissions.Contains(Auth.Permissions.TemuanVerifikasi)
                && !permissions.Contains(Auth.Permissions.TemuanTugaskan),
            permissions);
    }

    public bool Can(string permission) => Permissions.Contains(permission);

    public bool CoversPlant(int plantId) => AllPlants || PlantIds.Contains(plantId);

    public bool IsOwnDept(int? findingDeptId) => DeptId is not null && findingDeptId == DeptId;

    public bool CanSee(int plantId, int? findingDeptId) => CoversPlant(plantId) && (!OnlyDept || IsOwnDept(findingDeptId));
}
