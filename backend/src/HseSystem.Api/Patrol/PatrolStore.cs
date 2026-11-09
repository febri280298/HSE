using Dapper;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using HseSystem.Api.Data;
using HseSystem.Api.Images;
using Microsoft.Data.SqlClient;

namespace HseSystem.Api.Patrol;

internal sealed class FindingRow
{
    public int FindingId { get; set; }
    public int PlantId { get; set; }
    public string PlantCode { get; set; } = string.Empty;
    public string PlantName { get; set; } = string.Empty;
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public int SeqNo { get; set; }
    public DateTime PatrolDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public int? DeptId { get; set; }
    public string? DeptCode { get; set; }
    public string? DeptName { get; set; }
    public int? PicUserId { get; set; }
    public string? PicName { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Countermeasure { get; set; }
    public int? RepairedBy { get; set; }
    public string? RepairedByName { get; set; }
    public DateTime? RepairedAt { get; set; }
    public string? VerifiedByName { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifyNote { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsOverdue { get; set; }
    public string? ThumbnailPath { get; set; }

    public string Number => $"{PlantCode}-{PeriodYear}{PeriodMonth:00}-{SeqNo:000}";
}

internal sealed class PhotoRow
{
    public string Kind { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

internal sealed class EventRow
{
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

internal sealed class FindingLockRow
{
    public int PlantId { get; set; }
    public int? DeptId { get; set; }
    public DateTime PatrolDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? RepairedBy { get; set; }
}

public sealed record NewFinding(int PlantId, DateOnly PatrolDate, string Detail, int AreaId, string Type, IReadOnlyList<PhotoRef> Photos);

/// <summary>Penyimpanan temuan. Semua perubahan status terjadi di sini, dalam transaksi, dan dicatat ke riwayat.</summary>
public sealed class PatrolStore(HseDb db, ImageStorage images, TimeProvider time)
{
    public const int MaxPhotos = 3;

    private const string SelectFinding =
        """
        SELECT f.FindingId, f.PlantId, p.Code AS PlantCode, p.Name AS PlantName, f.PeriodYear, f.PeriodMonth, f.SeqNo,
               f.PatrolDate, f.Type, f.Detail, a.Name AS AreaName, f.DeptId, d.Code AS DeptCode, d.Name AS DeptName,
               f.PicUserId, pic.DisplayName AS PicName,
               f.DueDate, f.Status, f.Countermeasure,
               f.RepairedBy, rep.DisplayName AS RepairedByName, f.RepairedAt,
               ver.DisplayName AS VerifiedByName, f.VerifiedAt, f.VerifyNote,
               cre.DisplayName AS CreatedByName, f.CreatedAt,
               CAST(CASE WHEN f.Status <> 'Close' AND f.DueDate < @today THEN 1 ELSE 0 END AS bit) AS IsOverdue,
               thumb.Path AS ThumbnailPath
        FROM patrol.Findings AS f
        JOIN mst.Plants AS p ON p.PlantId = f.PlantId
        JOIN mst.Areas AS a ON a.AreaId = f.AreaId
        JOIN auth.Users AS cre ON cre.UserId = f.CreatedBy
        LEFT JOIN mst.Departments AS d ON d.DeptId = f.DeptId
        LEFT JOIN auth.Users AS pic ON pic.UserId = f.PicUserId
        LEFT JOIN auth.Users AS rep ON rep.UserId = f.RepairedBy
        LEFT JOIN auth.Users AS ver ON ver.UserId = f.VerifiedBy
        OUTER APPLY (SELECT TOP (1) ph.Path FROM patrol.FindingPhotos AS ph
                     WHERE ph.FindingId = f.FindingId AND ph.Kind = 'Temuan' ORDER BY ph.PhotoId) AS thumb
        """;

    private const string ScopeFilter =
        """
        ((@allPlants = 1 OR f.PlantId IN @plantIds) AND (@onlyDept = 0 OR f.DeptId = @deptId))
        """;

    /// <summary>Syarat calon PIC, untuk alias tabel pengguna <c>u</c>: punya hak mengisi perbaikan.</summary>
    private const string CanBePic =
        """
        EXISTS (SELECT 1 FROM auth.UserRoles AS ur
                JOIN auth.Roles AS r ON r.RoleId = ur.RoleId
                LEFT JOIN auth.RolePermissions AS rp ON rp.RoleId = r.RoleId AND rp.Permission = @permission
                WHERE ur.UserId = u.UserId AND (r.IsSystem = 1 OR rp.Permission IS NOT NULL))
        """;

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public async Task<FindingOptions> OptionsAsync(PatrolScope scope, int? plantId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            $"""
            SELECT PlantId, Code, Name FROM mst.Plants
            WHERE IsActive = 1 AND (@allPlants = 1 OR PlantId IN @plantIds) ORDER BY SortOrder, Code;

            SELECT AreaId, Name FROM mst.Areas WHERE IsActive = 1 AND PlantId = @plantId ORDER BY Name;

            SELECT DeptId, Code, Name FROM mst.Departments WHERE IsActive = 1 ORDER BY Name;

            SELECT u.UserId, u.DisplayName, u.DeptId
            FROM auth.Users AS u
            JOIN auth.UserPlants AS up ON up.UserId = u.UserId AND up.PlantId = @plantId
            WHERE @canAssign = 1 AND u.IsActive = 1 AND u.DeptId IS NOT NULL AND {CanBePic}
            ORDER BY u.DisplayName;
            """,
            new
            {
                allPlants = scope.AllPlants,
                plantIds = scope.PlantIds,
                plantId,
                canAssign = scope.Can(Permissions.TemuanTugaskan),
                permission = Permissions.PerbaikanInput,
            },
            cancellationToken: ct));

        return new FindingOptions(
            [.. await grid.ReadAsync<PlantInfo>()],
            [.. await grid.ReadAsync<AreaOption>()],
            [.. await grid.ReadAsync<DepartmentOption>()],
            [.. await grid.ReadAsync<PicOption>()]);
    }

    public async Task<PagedResult<FindingListItem>> ListAsync(PatrolScope scope, FindingQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page ?? 1, 1);
        // Grid di frontend memuat semua hasil filter sekaligus, lalu sort dan paging dikerjakan di browser.
        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 5000);
        var filter =
            $"""
            WHERE {ScopeFilter}
              AND (@plantId IS NULL OR f.PlantId = @plantId)
              AND (@year IS NULL OR f.PeriodYear = @year)
              AND (@month IS NULL OR f.PeriodMonth = @month)
              AND (@status IS NULL OR f.Status = @status)
              AND (@type IS NULL OR f.Type = @type)
              AND (@overdue = 0 OR (f.Status <> 'Close' AND f.DueDate < @today))
              AND (@mine = 0 OR f.DeptId = @deptId)
              AND (@pic = 0 OR f.PicUserId = @userId)
            """;

        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            $"""
            SELECT COUNT(*) FROM patrol.Findings AS f {filter};

            {SelectFinding}
            {filter}
            ORDER BY f.PatrolDate DESC, f.FindingId DESC
            OFFSET @skip ROWS FETCH NEXT @pageSize ROWS ONLY;
            """,
            new
            {
                deptId = scope.DeptId,
                onlyDept = scope.OnlyDept,
                allPlants = scope.AllPlants,
                plantIds = scope.PlantIds,
                query.PlantId,
                query.Year,
                query.Month,
                status = NullIfBlank(query.Status),
                type = NullIfBlank(query.Type),
                overdue = query.Overdue ?? false,
                mine = query.Mine ?? false,
                pic = query.Pic ?? false,
                userId = scope.UserId,
                today = Today.ToDateTime(TimeOnly.MinValue),
                skip = (page - 1) * pageSize,
                pageSize,
            },
            cancellationToken: ct));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<FindingRow>()).Select(ToListItem).ToList();
        return new PagedResult<FindingListItem>(items, total, page, pageSize);
    }

    public async Task<FindingDetail?> GetAsync(PatrolScope scope, int findingId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            $"""
            {SelectFinding}
            WHERE f.FindingId = @findingId AND {ScopeFilter};

            SELECT Kind, Path, FileName FROM patrol.FindingPhotos WHERE FindingId = @findingId ORDER BY PhotoId;

            SELECT e.Action, e.Note, u.DisplayName AS UserName, e.CreatedAt
            FROM patrol.FindingEvents AS e JOIN auth.Users AS u ON u.UserId = e.UserId
            WHERE e.FindingId = @findingId ORDER BY e.CreatedAt, e.EventId;
            """,
            new
            {
                findingId,
                deptId = scope.DeptId,
                onlyDept = scope.OnlyDept,
                allPlants = scope.AllPlants,
                plantIds = scope.PlantIds,
                today = Today.ToDateTime(TimeOnly.MinValue),
            },
            cancellationToken: ct));

        var row = await grid.ReadSingleOrDefaultAsync<FindingRow>();
        if (row is null)
        {
            return null;
        }

        var photos = await grid.ReadAsync<PhotoRow>();
        var events = await grid.ReadAsync<EventRow>();

        return new FindingDetail(
            row.FindingId, row.Number, row.PlantId, row.PlantCode, row.PlantName, DateOnly.FromDateTime(row.PatrolDate), row.Type, row.Detail,
            row.AreaName, row.DeptId, row.DeptCode, row.DeptName, row.PicUserId, row.PicName, ToDate(row.DueDate), row.Status, row.IsOverdue,
            row.Countermeasure, row.RepairedByName, Utc(row.RepairedAt), row.VerifiedByName, Utc(row.VerifiedAt), row.VerifyNote,
            row.CreatedByName, Utc(row.CreatedAt)!.Value,
            CanAssign: scope.Can(Permissions.TemuanTugaskan) && FindingStatus.CanAssign(row.Status) && scope.CoversPlant(row.PlantId),
            CanRepair: scope.Can(Permissions.PerbaikanInput) && scope.IsOwnDept(row.DeptId) && FindingStatus.CanRepair(row.Status),
            CanVerify: scope.Can(Permissions.TemuanVerifikasi) && row.Status == FindingStatus.Verifikasi
                       && row.RepairedBy != scope.UserId && scope.CoversPlant(row.PlantId),
            [.. photos.Select(p => new FindingPhotoResponse(p.Kind, p.Path, images.UrlOf(p.Path), p.FileName))],
            [.. events.Select(e => new FindingEventResponse(e.Action, e.Note, e.UserName, Utc(e.CreatedAt)!.Value))]);
    }

    public async Task<int> CreateAsync(PatrolScope scope, NewFinding finding, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var areas = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM mst.Areas WHERE AreaId = @AreaId AND PlantId = @PlantId AND IsActive = 1;",
            new { finding.AreaId, finding.PlantId }, tx, cancellationToken: ct));
        if (areas == 0)
        {
            throw new ValidationFailure("areaId", "Area tidak ditemukan di plant ini.");
        }

        var year = finding.PatrolDate.Year;
        var month = finding.PatrolDate.Month;
        try
        {
            // UPDLOCK + HOLDLOCK: dua patroller yang menyimpan bersamaan tidak mendapat nomor yang sama.
            var findingId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                DECLARE @seq int = (SELECT ISNULL(MAX(SeqNo), 0) + 1 FROM patrol.Findings WITH (UPDLOCK, HOLDLOCK)
                                    WHERE PlantId = @PlantId AND PeriodYear = @year AND PeriodMonth = @month);
                INSERT INTO patrol.Findings (PlantId, PeriodYear, PeriodMonth, SeqNo, PatrolDate, Type, Detail, AreaId, CreatedBy)
                VALUES (@PlantId, @year, @month, @seq, @patrolDate, @Type, @Detail, @AreaId, @userId);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """,
                new
                {
                    finding.PlantId,
                    year,
                    month,
                    patrolDate = finding.PatrolDate.ToDateTime(TimeOnly.MinValue),
                    finding.Type,
                    finding.Detail,
                    finding.AreaId,
                    userId = scope.UserId,
                },
                tx,
                cancellationToken: ct));

            await InsertPhotosAsync(conn, tx, findingId, PhotoKind.Temuan, finding.Photos, ct);
            await InsertEventAsync(conn, tx, findingId, "Dibuat", null, scope.UserId, ct);
            await tx.CommitAsync(ct);
            return findingId;
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            throw new ValidationFailure("photos", "Foto sudah dipakai di temuan lain. Ambil foto baru.");
        }
    }

    /// <summary>Tim HSE menentukan (atau mengganti, selama belum dikerjakan) departemen, PIC, dan due date.</summary>
    /// <returns><c>false</c> kalau temuan tidak ada / di luar cakupan pengguna.</returns>
    public async Task<bool> AssignAsync(PatrolScope scope, int findingId, int deptId, int picUserId, DateOnly dueDate, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var finding = await LockAsync(conn, tx, findingId, ct);
        if (finding is null || !scope.CoversPlant(finding.PlantId))
        {
            return false;
        }

        if (!FindingStatus.CanAssign(finding.Status))
        {
            throw new ValidationFailure("finding", "Temuan ini sudah dikerjakan departemen, penugasannya tidak bisa diubah.");
        }

        if (dueDate < DateOnly.FromDateTime(finding.PatrolDate))
        {
            throw new ValidationFailure("dueDate", "Due date tidak boleh sebelum tanggal patrol.");
        }

        var department = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT Name FROM mst.Departments WHERE DeptId = @deptId AND IsActive = 1;", new { deptId }, tx, cancellationToken: ct));
        if (department is null)
        {
            throw new ValidationFailure("deptId", "Departemen tidak ditemukan.");
        }

        var picName = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            $"""
            SELECT u.DisplayName
            FROM auth.Users AS u
            JOIN auth.UserPlants AS up ON up.UserId = u.UserId AND up.PlantId = @plantId
            WHERE u.UserId = @picUserId AND u.IsActive = 1 AND u.DeptId = @deptId AND {CanBePic};
            """,
            new { picUserId, deptId, plantId = finding.PlantId, permission = Permissions.PerbaikanInput }, tx, cancellationToken: ct));
        if (picName is null)
        {
            throw new ValidationFailure("picUserId", "PIC harus user aktif di departemen dan plant tersebut, dengan hak Input perbaikan.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE patrol.Findings
            SET Status = @status, DeptId = @deptId, PicUserId = @picUserId, DueDate = @dueDate, UpdatedAt = SYSUTCDATETIME()
            WHERE FindingId = @findingId;
            """,
            new { findingId, deptId, picUserId, dueDate = dueDate.ToDateTime(TimeOnly.MinValue), status = FindingStatus.Ditugaskan },
            tx,
            cancellationToken: ct));

        await InsertEventAsync(conn, tx, findingId, "Ditugaskan", $"{department}, PIC {picName}, due {dueDate:dd/MM/yyyy}", scope.UserId, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <returns><c>false</c> kalau temuan tidak ada / di luar cakupan pengguna.</returns>
    public async Task<bool> SaveRepairAsync(
        PatrolScope scope, int findingId, string countermeasure, IReadOnlyList<PhotoRef> photos, bool finished, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var finding = await LockAsync(conn, tx, findingId, ct);
        if (finding is null || !scope.CanSee(finding.PlantId, finding.DeptId))
        {
            return false;
        }

        if (!scope.IsOwnDept(finding.DeptId))
        {
            throw new ValidationFailure("finding", "Hanya departemen yang ditugaskan yang bisa mengisi perbaikan.");
        }

        if (!FindingStatus.CanRepair(finding.Status))
        {
            throw new ValidationFailure("finding", "Temuan ini sedang diperiksa atau sudah ditutup.");
        }

        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM patrol.FindingPhotos WHERE FindingId = @findingId AND Kind = 'Perbaikan';
                UPDATE patrol.Findings
                SET Status = @status, Countermeasure = @countermeasure, RepairedBy = @userId, RepairedAt = SYSUTCDATETIME(),
                    VerifyNote = NULL, UpdatedAt = SYSUTCDATETIME()
                WHERE FindingId = @findingId;
                """,
                new { findingId, countermeasure, userId = scope.UserId, status = finished ? FindingStatus.Verifikasi : FindingStatus.OnProgress },
                tx,
                cancellationToken: ct));

            await InsertPhotosAsync(conn, tx, findingId, PhotoKind.Perbaikan, photos, ct);
            await InsertEventAsync(conn, tx, findingId, finished ? "Diajukan" : "Progres", countermeasure, scope.UserId, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            throw new ValidationFailure("photos", "Foto sudah dipakai di temuan lain. Ambil foto baru.");
        }
    }

    /// <returns><c>false</c> kalau temuan tidak ada / di luar cakupan pengguna.</returns>
    public async Task<bool> VerifyAsync(PatrolScope scope, int findingId, bool approved, string? note, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var finding = await LockAsync(conn, tx, findingId, ct);
        if (finding is null || !scope.CoversPlant(finding.PlantId))
        {
            return false;
        }

        if (finding.Status != FindingStatus.Verifikasi)
        {
            throw new ValidationFailure("finding", "Temuan ini tidak sedang menunggu pemeriksaan.");
        }

        if (finding.RepairedBy == scope.UserId)
        {
            throw new ValidationFailure("finding", "Perbaikan harus diperiksa orang lain, bukan yang mengerjakannya.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE patrol.Findings
            SET Status = @status, VerifiedBy = @userId, VerifiedAt = SYSUTCDATETIME(), VerifyNote = @note, UpdatedAt = SYSUTCDATETIME()
            WHERE FindingId = @findingId;
            """,
            new { findingId, userId = scope.UserId, note, status = approved ? FindingStatus.Close : FindingStatus.NotClose },
            tx,
            cancellationToken: ct));

        await InsertEventAsync(conn, tx, findingId, approved ? "Ditutup" : "Ditolak", note, scope.UserId, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<DashboardResponse> DashboardAsync(PatrolScope scope, int year, CancellationToken ct)
    {
        var offsetMinutes = (int)time.GetLocalNow().Offset.TotalMinutes;
        var today = Today;
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            $"""
            SELECT f.PlantId, p.Code AS PlantCode, p.Name AS PlantName, f.PeriodMonth AS Month, f.Type,
                   COUNT(*) AS Total,
                   SUM(CASE WHEN f.Status = 'Close' THEN 1 ELSE 0 END) AS Closed,
                   SUM(CASE WHEN f.Status <> 'Close' AND f.DueDate < @today THEN 1 ELSE 0 END) AS Overdue,
                   SUM(CASE WHEN f.Status = 'Close' AND CAST(DATEADD(minute, @offsetMinutes, f.RepairedAt) AS date) <= f.DueDate THEN 1 ELSE 0 END) AS ClosedOnTime
            FROM patrol.Findings AS f
            JOIN mst.Plants AS p ON p.PlantId = f.PlantId
            WHERE f.PeriodYear = @year AND {ScopeFilter}
            GROUP BY f.PlantId, p.Code, p.Name, p.SortOrder, f.PeriodMonth, f.Type
            ORDER BY p.SortOrder, f.PeriodMonth, f.Type;

            SELECT f.PlantId, f.Type, d.Code AS DeptCode, d.Name AS DeptName,
                   COUNT(*) AS Total,
                   SUM(CASE WHEN f.Status = 'Close' THEN 1 ELSE 0 END) AS Closed,
                   SUM(CASE WHEN f.Status <> 'Close' AND f.DueDate < @today THEN 1 ELSE 0 END) AS Overdue
            FROM patrol.Findings AS f
            LEFT JOIN mst.Departments AS d ON d.DeptId = f.DeptId
            WHERE f.PeriodYear = @year AND {ScopeFilter}
            GROUP BY f.PlantId, f.Type, d.Code, d.Name;

            SELECT f.PlantId, f.Type, f.Status, COUNT(*) AS Total
            FROM patrol.Findings AS f
            WHERE f.PeriodYear = @year AND {ScopeFilter}
            GROUP BY f.PlantId, f.Type, f.Status;

            {SelectFinding}
            WHERE f.PeriodYear = @year AND {ScopeFilter} AND f.Status <> 'Close' AND f.DueDate < @today
            ORDER BY f.DueDate, f.FindingId
            OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY;
            """,
            new
            {
                year,
                offsetMinutes,
                deptId = scope.DeptId,
                onlyDept = scope.OnlyDept,
                allPlants = scope.AllPlants,
                plantIds = scope.PlantIds,
                today = today.ToDateTime(TimeOnly.MinValue),
            },
            cancellationToken: ct));

        var rows = await grid.ReadAsync<DashboardRow>();
        var departments = await grid.ReadAsync<DashboardDeptRow>();
        var statuses = await grid.ReadAsync<DashboardStatusRow>();
        var overdue = (await grid.ReadAsync<FindingRow>()).Select(row =>
        {
            var dueDate = ToDate(row.DueDate)!.Value;
            return new DashboardOverdueItem(
                row.FindingId, row.Number, row.PlantId, row.Type, row.Detail, row.DeptCode, row.PicName, dueDate, today.DayNumber - dueDate.DayNumber);
        });

        return new DashboardResponse(year, [.. rows], [.. departments], [.. statuses], [.. overdue]);
    }

    private static Task<FindingLockRow?> LockAsync(SqlConnection conn, SqlTransaction tx, int findingId, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<FindingLockRow>(new CommandDefinition(
            "SELECT PlantId, DeptId, PatrolDate, Status, RepairedBy FROM patrol.Findings WITH (UPDLOCK, ROWLOCK) WHERE FindingId = @findingId;",
            new { findingId }, tx, cancellationToken: ct));

    private static async Task InsertPhotosAsync(
        SqlConnection conn, SqlTransaction tx, int findingId, string kind, IReadOnlyList<PhotoRef> photos, CancellationToken ct)
    {
        if (photos.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO patrol.FindingPhotos (FindingId, Kind, Path, FileName) VALUES (@FindingId, @Kind, @Path, @FileName);",
                photos.Select(p => new { FindingId = findingId, Kind = kind, p.Path, p.FileName }),
                tx,
                cancellationToken: ct));
        }
    }

    private static Task InsertEventAsync(SqlConnection conn, SqlTransaction tx, int findingId, string action, string? note, int userId, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO patrol.FindingEvents (FindingId, Action, Note, UserId) VALUES (@findingId, @action, @note, @userId);",
            new { findingId, action, note, userId }, tx, cancellationToken: ct));

    private FindingListItem ToListItem(FindingRow row) => new(
        row.FindingId, row.Number, row.PlantCode, row.PlantName, DateOnly.FromDateTime(row.PatrolDate), row.Type, row.Detail,
        row.AreaName, row.DeptCode, row.PicName, ToDate(row.DueDate), row.Status, row.IsOverdue,
        row.ThumbnailPath is null ? null : images.UrlOf(row.ThumbnailPath));

    private static DateOnly? ToDate(DateTime? value) => value is { } t ? DateOnly.FromDateTime(t) : null;

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
