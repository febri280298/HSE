using System.Security.Claims;
using Dapper;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using HseSystem.Api.Data;
using HseSystem.Api.Images;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HseSystem.Api.Patrol;

public static class PatrolEndpoints
{
    public static RouteGroupBuilder MapPatrolEndpoints(this RouteGroupBuilder api)
    {
        var temuan = api.MapGroup("/temuan");
        temuan.MapGet("/options", OptionsAsync);
        temuan.MapGet("/", ListAsync).RequireAuthorization(Permissions.TemuanAkses);
        temuan.MapGet("/{findingId:int}", GetAsync).RequireAuthorization(Permissions.TemuanAkses);
        temuan.MapPost("/", CreateAsync).RequireAuthorization(Permissions.TemuanInput);
        temuan.MapPost("/{findingId:int}/tugaskan", AssignAsync).RequireAuthorization(Permissions.TemuanTugaskan);
        temuan.MapPost("/{findingId:int}/perbaikan", RepairAsync).RequireAuthorization(Permissions.PerbaikanInput);
        temuan.MapPost("/{findingId:int}/verifikasi", VerifyAsync).RequireAuthorization(Permissions.TemuanVerifikasi);
        api.MapGet("/dashboard", DashboardAsync).RequireAuthorization(Permissions.TemuanAkses);
        api.MapGet("/genba", ListGenbaAsync);
        api.MapPut("/genba", SaveGenbaAsync).RequireAuthorization(Permissions.JadwalKelola);
        return temuan;
    }

    internal static async Task<Ok<FindingOptions>> OptionsAsync(
        int? plantId, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, CancellationToken ct)
    {
        var scope = await ScopeAsync(user, sessions, ct);
        // Plant di luar cakupan: area-nya tidak diberikan.
        var allowedPlant = plantId is { } id && scope.CoversPlant(id) ? plantId : null;
        return TypedResults.Ok(await store.OptionsAsync(scope, allowedPlant, ct));
    }

    internal static async Task<Ok<PagedResult<FindingListItem>>> ListAsync(
        [AsParameters] FindingQuery query, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, CancellationToken ct) =>
        TypedResults.Ok(await store.ListAsync(await ScopeAsync(user, sessions, ct), query, ct));

    internal static async Task<Results<Ok<FindingDetail>, NotFound>> GetAsync(
        int findingId, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, CancellationToken ct)
    {
        var detail = await store.GetAsync(await ScopeAsync(user, sessions, ct), findingId, ct);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    internal static async Task<Results<Ok<CreatedFinding>, ValidationProblem>> CreateAsync(
        CreateFindingRequest request, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, ImageStorage images,
        TimeProvider time, CancellationToken ct)
    {
        try
        {
            var scope = await ScopeAsync(user, sessions, ct);
            if (!scope.CoversPlant(request.PlantId))
            {
                throw new ValidationFailure("plantId", "Anda tidak terdaftar di plant ini.");
            }

            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var patrolDate = request.PatrolDate ?? today;
            if (patrolDate > today)
            {
                throw new ValidationFailure("patrolDate", "Tanggal patrol tidak boleh di masa depan.");
            }

            var detail = Required(request.Detail, "detail", "Detail temuan", 500);
            if (!FindingType.IsValid(request.Type))
            {
                throw new ValidationFailure("type", "Tipe temuan harus Safety atau 5R.");
            }

            var photos = ValidatePhotos(request.Photos, "temuan", images, required: true, "Ambil minimal satu foto temuan.");
            var findingId = await store.CreateAsync(
                scope, new NewFinding(request.PlantId, patrolDate, detail, request.AreaId, request.Type!, photos), ct);

            // Patroller tanpa hak lihat tetap mendapat nomornya, walau tidak bisa membuka detail.
            var detailResult = await store.GetAsync(scope, findingId, ct);
            return TypedResults.Ok(new CreatedFinding(findingId, detailResult?.Number));
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<Ok<FindingDetail>, ValidationProblem, NotFound>> AssignAsync(
        int findingId, AssignRequest request, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, CancellationToken ct)
    {
        try
        {
            var scope = await ScopeAsync(user, sessions, ct);
            if (request.DueDate is not { } dueDate)
            {
                throw new ValidationFailure("dueDate", "Due date wajib diisi.");
            }

            if (request.PicUserId is not { } picUserId)
            {
                throw new ValidationFailure("picUserId", "PIC wajib dipilih.");
            }

            if (!await store.AssignAsync(scope, findingId, request.DeptId, picUserId, dueDate, ct))
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok((await store.GetAsync(scope, findingId, ct))!);
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<Ok<FindingDetail>, ValidationProblem, NotFound>> RepairAsync(
        int findingId, RepairRequest request, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, ImageStorage images, CancellationToken ct)
    {
        try
        {
            var scope = await ScopeAsync(user, sessions, ct);
            var countermeasure = Required(request.Countermeasure, "countermeasure", "Countermeasure", 1000);
            var photos = ValidatePhotos(request.Photos, "perbaikan", images, required: request.Selesai, "Ambil minimal satu foto sesudah perbaikan.");
            if (!await store.SaveRepairAsync(scope, findingId, countermeasure, photos, request.Selesai, ct))
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok((await store.GetAsync(scope, findingId, ct))!);
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<Ok<FindingDetail>, ValidationProblem, NotFound>> VerifyAsync(
        int findingId, VerifyRequest request, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, CancellationToken ct)
    {
        try
        {
            var scope = await ScopeAsync(user, sessions, ct);
            var note = string.IsNullOrWhiteSpace(request.Catatan) ? null : request.Catatan.Trim();
            if (!request.Setuju && note is null)
            {
                throw new ValidationFailure("catatan", "Tulis alasan penolakan supaya departemen tahu apa yang harus diperbaiki.");
            }

            if (note?.Length > 500)
            {
                throw new ValidationFailure("catatan", "Catatan maksimal 500 karakter.");
            }

            if (!await store.VerifyAsync(scope, findingId, request.Setuju, note, ct))
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok((await store.GetAsync(scope, findingId, ct))!);
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Ok<DashboardResponse>> DashboardAsync(
        int? year, ClaimsPrincipal user, UserSessions sessions, PatrolStore store, TimeProvider time, CancellationToken ct) =>
        TypedResults.Ok(await store.DashboardAsync(await ScopeAsync(user, sessions, ct), year ?? time.GetLocalNow().Year, ct));

    internal static async Task<Ok<List<GenbaScheduleItem>>> ListGenbaAsync(int year, HseDb db, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var rows = await conn.QueryAsync<GenbaRow>(new CommandDefinition(
            "SELECT PlantId, PeriodMonth AS Month, PlannedDate FROM patrol.GenbaSchedules WHERE PeriodYear = @year;",
            new { year }, cancellationToken: ct));
        return TypedResults.Ok(rows.Select(r => new GenbaScheduleItem(r.PlantId, r.Month, DateOnly.FromDateTime(r.PlannedDate))).ToList());
    }

    /// <summary>Satu tanggal per plant per bulan: menyimpan lagi berarti mengganti. Tanggal kosong = hapus jadwal.</summary>
    internal static async Task<Results<NoContent, ValidationProblem>> SaveGenbaAsync(
        GenbaScheduleRequest request, ClaimsPrincipal user, HseDb db, CancellationToken ct)
    {
        if (request.Month is < 1 or > 12 || request.Year is < 2000 or > 2100)
        {
            return Problems.Invalid("month", "Bulan atau tahun tidak valid.");
        }

        if (request.PlannedDate is { } date && (date.Year != request.Year || date.Month != request.Month))
        {
            return Problems.Invalid("plannedDate", "Tanggal genba harus di dalam bulan tersebut.");
        }

        await using var conn = db.Connection();
        var plantExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM mst.Plants WHERE PlantId = @PlantId;", new { request.PlantId }, cancellationToken: ct));
        if (plantExists == 0)
        {
            return Problems.Invalid("plantId", "Plant tidak ditemukan.");
        }

        var key = new { request.PlantId, request.Year, request.Month };
        if (request.PlannedDate is not { } planned)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM patrol.GenbaSchedules WHERE PlantId = @PlantId AND PeriodYear = @Year AND PeriodMonth = @Month;",
                key, cancellationToken: ct));
            return TypedResults.NoContent();
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            MERGE patrol.GenbaSchedules WITH (HOLDLOCK) AS t
            USING (SELECT @PlantId AS PlantId, @Year AS PeriodYear, @Month AS PeriodMonth) AS s
               ON t.PlantId = s.PlantId AND t.PeriodYear = s.PeriodYear AND t.PeriodMonth = s.PeriodMonth
            WHEN MATCHED THEN UPDATE SET PlannedDate = @date, UpdatedBy = @userId, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (PlantId, PeriodYear, PeriodMonth, PlannedDate, UpdatedBy)
                                  VALUES (@PlantId, @Year, @Month, @date, @userId);
            """,
            new { request.PlantId, request.Year, request.Month, date = planned.ToDateTime(TimeOnly.MinValue), userId = user.GetUserId()!.Value },
            cancellationToken: ct));
        return TypedResults.NoContent();
    }

    private static async Task<PatrolScope> ScopeAsync(ClaimsPrincipal user, UserSessions sessions, CancellationToken ct)
    {
        // Endpoint di sini wajib login, dan OnValidatePrincipal sudah memastikan pengguna masih ada.
        var session = await sessions.GetAsync(user.GetUserId()!.Value, ct);
        return PatrolScope.From(session!);
    }

    private static string Required(string? value, string field, string label, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ValidationFailure(field, $"{label} wajib diisi.");
        }

        return text.Length <= maxLength ? text : throw new ValidationFailure(field, $"{label} maksimal {maxLength} karakter.");
    }

    private static IReadOnlyList<PhotoRef> ValidatePhotos(PhotoRef[]? photos, string category, ImageStorage images, bool required, string requiredMessage)
    {
        var list = (photos ?? []).DistinctBy(p => p.Path).ToList();
        if (required && list.Count == 0)
        {
            throw new ValidationFailure("photos", requiredMessage);
        }

        if (list.Count > PatrolStore.MaxPhotos)
        {
            throw new ValidationFailure("photos", $"Maksimal {PatrolStore.MaxPhotos} foto.");
        }

        if (list.Any(p => !images.IsStored(p.Path, category)))
        {
            throw new ValidationFailure("photos", "Foto tidak ditemukan di server. Ambil ulang fotonya.");
        }

        return [.. list.Select(p => new PhotoRef(p.Path, CleanName(p.FileName)))];
    }

    private static string CleanName(string? name)
    {
        var clean = name?.Trim();
        return string.IsNullOrEmpty(clean) ? "foto.jpg" : clean.Length > 120 ? clean[..120] : clean;
    }

    private sealed class GenbaRow
    {
        public int PlantId { get; set; }
        public int Month { get; set; }
        public DateTime PlannedDate { get; set; }
    }
}

public sealed record CreatedFinding(int FindingId, string? Number);
