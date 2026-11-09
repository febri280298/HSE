using HseSystem.Api.Auth;

namespace HseSystem.Api.Kpi;

public sealed record KpiDepartment(int DeptId, string Code, string Name);

public sealed record KpiPlantOption(int PlantId, string Code, string Name, IReadOnlyList<KpiDepartment> Departments);

/// <param name="CanManage">Boleh mengisi untuk semua departemen (tim HSE).</param>
/// <param name="DeptId">Departemen pengguna; tab awal Summary KPI.</param>
public sealed record KpiOptions(IReadOnlyList<KpiPlantOption> Plants, bool CanManage, int? DeptId);

/// <param name="DeptId">Null = gabungan semua departemen (sheet ALL).</param>
/// <param name="LastMonth">Bulan terakhir yang sudah berjalan; kolom sesudahnya tidak bisa diisi.</param>
public sealed record KpiSummaryResponse(
    int Year, int PlantId, string PlantName, int? DeptId, string Title, bool CanEdit, int LastMonth, int ReviewMonth,
    IReadOnlyList<KpiRow> Rows, IReadOnlyList<KpiReviewRow> Review);

/// <param name="Field"><c>accident</c>, <c>disease</c>, <c>diseaseTypes</c>, atau <c>accidentNote</c>.</param>
/// <param name="Value">Kosong = hapus isian bulan itu.</param>
public sealed record KpiInputRequest(int PlantId, int DeptId, int Year, int Month, string? Field, string? Value);

public sealed record KpiDepartmentStatus(int DeptId, string DeptName, bool HasInput, bool HasKalender, bool HasPpt);

public sealed record KpiMonitoringPlant(
    int PlantId, string PlantName, int Accidents, int DiseaseCases, decimal DiseaseAverage, int Findings, int Closed, decimal? PercentClosed,
    IReadOnlyList<KpiDepartmentStatus> Departments);

public sealed record KpiMonitoringResponse(int Year, int Month, IReadOnlyList<KpiMonitoringPlant> Plants);

public sealed record KpiDocumentInfo(int DocumentId, int Month, string FileName, long SizeBytes, string UploadedByName, DateTime UploadedAt);

public sealed record KpiDocumentRow(int DeptId, string DeptCode, string DeptName, bool CanUpload, IReadOnlyList<KpiDocumentInfo> Files);

public sealed record KpiDocumentsResponse(string Kind, int Year, int PlantId, int LastMonth, IReadOnlyList<KpiDocumentRow> Departments);

public sealed record KpiPicaRequest(
    int PlantId, int DeptId, int Year, int Month, string? KpiType, string? ProblemIssue, string? CorrectiveAction, DateOnly? DueDate, string? PicName, string? Status);

public sealed record KpiPicaItem(
    int PicaId, int PlantId, string PlantName, int DeptId, string DeptCode, string DeptName, int Year, int Month, string KpiType,
    string ProblemIssue, string CorrectiveAction, DateOnly DueDate, string PicName, string Status, bool CanEdit);

public sealed record KpiParticipantsRequest(int PlantId, int[]? DeptIds);

public static class KpiDocumentKind
{
    public const string Kalender = "Kalender";
    public const string Ppt = "PPT";

    public static bool IsValid(string? kind) => kind is Kalender or Ppt;

    /// <summary>Nama file unduhan sesuai format panduan: "(bulan). KALENDER SAFETY - DEPARTEMEN &amp; PLANT".</summary>
    public static string DownloadName(string kind, int month, string deptName, string plantName, string extension)
    {
        var label = kind == Kalender ? "KALENDER SAFETY" : "PPT KPI K3";
        var name = $"{month:00}. {label} - {deptName} {plantName}".ToUpperInvariant();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, ' ');
        }

        return name + extension;
    }
}

/// <summary>Cakupan pengguna: tim HSE (<c>kpi.kelola</c>) semua plant dan departemen, user departemen hanya departemen sendiri di plant-nya.</summary>
public sealed record KpiScope(int UserId, int? DeptId, IReadOnlyList<int> PlantIds, bool CanManage, bool AllPlants, bool CanInput)
{
    public static KpiScope From(UserSession session)
    {
        var permissions = session.EffectivePermissions.ToHashSet(StringComparer.Ordinal);
        var manage = permissions.Contains(Permissions.KpiKelola);
        return new KpiScope(
            session.UserId,
            session.DeptId,
            [.. session.Plants.Select(p => p.PlantId)],
            manage,
            manage || permissions.Contains(Permissions.TemuanSemuaPlant),
            permissions.Contains(Permissions.KpiInput));
    }

    public bool CoversPlant(int plantId) => AllPlants || PlantIds.Contains(plantId);

    public bool CanEdit(int plantId, int deptId) => CoversPlant(plantId) && (CanManage || (CanInput && DeptId == deptId));
}
