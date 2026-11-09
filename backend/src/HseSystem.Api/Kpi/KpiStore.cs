using Dapper;
using HseSystem.Api.Common;
using HseSystem.Api.Data;
using Microsoft.Data.SqlClient;

namespace HseSystem.Api.Kpi;

public sealed class KpiStore(HseDb db, TimeProvider time)
{
    private const int MaxCount = 999;

    /// <summary>Bulan terakhir yang sudah berjalan pada tahun itu: 12 untuk tahun lalu, 0 untuk tahun depan.</summary>
    public int LastMonth(int year)
    {
        var now = time.GetLocalNow();
        return year < now.Year ? 12 : year > now.Year ? 0 : now.Month;
    }

    public async Task<KpiOptions> OptionsAsync(KpiScope scope, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var plants = await PlantsAsync(conn, scope, ct);
        var departments = await DepartmentsAsync(conn, ct);
        return new KpiOptions(
            [.. plants.Select(p => new KpiPlantOption(p.PlantId, p.Code, p.Name, [.. departments.Where(d => d.PlantId == p.PlantId).Select(d => d.ToDepartment())]))],
            scope.CanManage,
            scope.DeptId);
    }

    public async Task<KpiSummaryResponse?> SummaryAsync(KpiScope scope, int year, int plantId, int? deptId, int? reviewMonth, CancellationToken ct)
    {
        if (!scope.CoversPlant(plantId))
        {
            return null;
        }

        await using var conn = db.Connection();
        var plant = await conn.QuerySingleOrDefaultAsync<PlantRow>(new CommandDefinition(
            "SELECT PlantId, Code, Name FROM mst.Plants WHERE PlantId = @plantId;", new { plantId }, cancellationToken: ct));
        if (plant is null)
        {
            return null;
        }

        var department = deptId is null ? null : (await DepartmentsAsync(conn, ct)).SingleOrDefault(d => d.PlantId == plantId && d.DeptId == deptId);
        if (deptId is not null && department is null)
        {
            return null;
        }

        var months = await MonthsAsync(conn, year, plantId, deptId, ct);
        var lastMonth = LastMonth(year);
        var review = Math.Clamp(reviewMonth ?? (lastMonth == 0 ? 1 : lastMonth), 1, 12);
        var title = $"SUMMARY KPI {(deptId is null ? "ALL DEPT" : "DEPT " + department!.Name.ToUpperInvariant())} FY {year}";
        return new KpiSummaryResponse(
            year, plantId, plant.Name, deptId, title, deptId is { } id && scope.CanEdit(plantId, id), lastMonth, review,
            KpiCalculator.Rows(months, deptId is null, lastMonth),
            KpiCalculator.Review(months, deptId is null, review, lastMonth));
    }

    public async Task SaveInputAsync(KpiScope scope, KpiInputRequest request, CancellationToken ct)
    {
        if (request.Year is < 2000 or > 2100 || request.Month is < 1 or > 12)
        {
            throw new ValidationFailure("month", "Bulan atau tahun tidak valid.");
        }

        if (request.Month > LastMonth(request.Year))
        {
            throw new ValidationFailure("month", "Bulan ini belum berjalan.");
        }

        if (!scope.CanEdit(request.PlantId, request.DeptId))
        {
            throw new ValidationFailure("deptId", "Anda hanya bisa mengisi KPI departemen sendiri.");
        }

        var value = request.Value?.Trim();
        value = string.IsNullOrEmpty(value) ? null : value;
        int? number = null;
        switch (request.Field)
        {
            case "accident" or "disease":
                if (value is not null)
                {
                    if (!int.TryParse(value, out var parsed) || parsed is < 0 or > MaxCount)
                    {
                        throw new ValidationFailure("value", $"Isi dengan angka 0 sampai {MaxCount}.");
                    }

                    number = parsed;
                }

                break;
            case "diseaseTypes" or "accidentNote":
                if (value?.Length > 200)
                {
                    throw new ValidationFailure("value", "Maksimal 200 karakter.");
                }

                break;
            default:
                throw new ValidationFailure("field", "Kolom tidak dikenal.");
        }

        await using var conn = db.Connection();
        await EnsureParticipantAsync(conn, request.PlantId, request.DeptId, ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            MERGE kpi.MonthlyInputs WITH (HOLDLOCK) AS t
            USING (SELECT @PlantId AS PlantId, @DeptId AS DeptId, @Year AS PeriodYear, @Month AS PeriodMonth) AS s
               ON t.PlantId = s.PlantId AND t.DeptId = s.DeptId AND t.PeriodYear = s.PeriodYear AND t.PeriodMonth = s.PeriodMonth
            WHEN MATCHED THEN UPDATE SET
                AccidentCount = CASE WHEN @Field = 'accident' THEN @number ELSE t.AccidentCount END,
                DiseaseCount  = CASE WHEN @Field = 'disease' THEN @number ELSE t.DiseaseCount END,
                DiseaseTypes  = CASE WHEN @Field = 'diseaseTypes' THEN @value ELSE t.DiseaseTypes END,
                AccidentNote  = CASE WHEN @Field = 'accidentNote' THEN @value ELSE t.AccidentNote END,
                UpdatedBy = @userId, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (PlantId, DeptId, PeriodYear, PeriodMonth, AccidentCount, DiseaseCount, DiseaseTypes, AccidentNote, UpdatedBy)
                VALUES (@PlantId, @DeptId, @Year, @Month,
                        CASE WHEN @Field = 'accident' THEN @number END,
                        CASE WHEN @Field = 'disease' THEN @number END,
                        CASE WHEN @Field = 'diseaseTypes' THEN @value END,
                        CASE WHEN @Field = 'accidentNote' THEN @value END,
                        @userId);
            """,
            new { request.PlantId, request.DeptId, request.Year, request.Month, request.Field, number, value, userId = scope.UserId },
            cancellationToken: ct));
    }

    public async Task<KpiMonitoringResponse> MonitoringAsync(KpiScope scope, int year, int? month, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var plants = await PlantsAsync(conn, scope, ct);
        var departments = await DepartmentsAsync(conn, ct);
        var last = LastMonth(year);
        var selected = Math.Clamp(month ?? (last == 0 ? 1 : last), 1, 12);

        var inputs = (await conn.QueryAsync<InputRow>(new CommandDefinition(
            "SELECT PlantId, DeptId, PeriodMonth AS Month, AccidentCount, DiseaseCount, DiseaseTypes, AccidentNote FROM kpi.MonthlyInputs WHERE PeriodYear = @year;",
            new { year }, cancellationToken: ct))).ToList();
        var findings = (await conn.QueryAsync<FindingRow>(new CommandDefinition(
            """
            SELECT PlantId, COUNT(*) AS Findings, SUM(CASE WHEN Status = 'Close' THEN 1 ELSE 0 END) AS Closed
            FROM patrol.Findings WHERE PeriodYear = @year GROUP BY PlantId;
            """,
            new { year }, cancellationToken: ct))).ToDictionary(f => f.PlantId);
        var documents = (await conn.QueryAsync<(string Kind, int PlantId, int DeptId)>(new CommandDefinition(
            "SELECT Kind, PlantId, DeptId FROM kpi.Documents WHERE PeriodYear = @year AND PeriodMonth = @selected;",
            new { year, selected }, cancellationToken: ct))).ToHashSet();

        return new KpiMonitoringResponse(year, selected, [.. plants.Select(plant =>
        {
            var rows = inputs.Where(i => i.PlantId == plant.PlantId).ToList();
            var finding = findings.GetValueOrDefault(plant.PlantId);
            var total = finding?.Findings ?? 0;
            var closed = finding?.Closed ?? 0;
            var cases = rows.Sum(r => r.DiseaseCount ?? 0);
            return new KpiMonitoringPlant(
                plant.PlantId, plant.Name, rows.Sum(r => r.AccidentCount ?? 0), cases,
                Math.Round(cases / KpiCalculator.AverageDivisor / KpiCalculator.MonthCount, 2),
                total, closed, total == 0 ? null : Math.Round((decimal)closed / total, 4),
                [.. departments.Where(d => d.PlantId == plant.PlantId).Select(d => new KpiDepartmentStatus(
                    d.DeptId, d.Name,
                    rows.Any(r => r.DeptId == d.DeptId && r.Month == selected && r.AccidentCount is not null && r.DiseaseCount is not null),
                    documents.Contains((KpiDocumentKind.Kalender, plant.PlantId, d.DeptId)),
                    documents.Contains((KpiDocumentKind.Ppt, plant.PlantId, d.DeptId))))]);
        })]);
    }

    public async Task<KpiDocumentsResponse?> DocumentsAsync(KpiScope scope, string kind, int year, int plantId, CancellationToken ct)
    {
        if (!scope.CoversPlant(plantId))
        {
            return null;
        }

        await using var conn = db.Connection();
        var departments = (await DepartmentsAsync(conn, ct)).Where(d => d.PlantId == plantId).ToList();
        var files = (await conn.QueryAsync<DocumentRow>(new CommandDefinition(
            """
            SELECT d.DocumentId, d.DeptId, d.PeriodMonth AS Month, d.FileName, d.SizeBytes, u.DisplayName AS UploadedByName, d.UploadedAt
            FROM kpi.Documents AS d JOIN auth.Users AS u ON u.UserId = d.UploadedBy
            WHERE d.Kind = @kind AND d.PeriodYear = @year AND d.PlantId = @plantId;
            """,
            new { kind, year, plantId }, cancellationToken: ct))).ToList();
        return new KpiDocumentsResponse(kind, year, plantId, LastMonth(year), [.. departments.Select(d => new KpiDocumentRow(
            d.DeptId, d.Code, d.Name, scope.CanEdit(plantId, d.DeptId),
            [.. files.Where(f => f.DeptId == d.DeptId).OrderBy(f => f.Month).Select(f => new KpiDocumentInfo(f.DocumentId, f.Month, f.FileName, f.SizeBytes, f.UploadedByName, f.UploadedAt))]))]);
    }

    /// <summary>Upload ulang menggantikan file bulan itu. Kembalikan path file lama supaya bisa dihapus setelah tersimpan.</summary>
    public async Task<string?> SaveDocumentAsync(KpiScope scope, string kind, int plantId, int deptId, int year, int month, StoredDocument file, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        var key = new { kind, plantId, deptId, year, month };
        var previous = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT StoredPath FROM kpi.Documents WITH (UPDLOCK, HOLDLOCK) WHERE Kind = @kind AND PlantId = @plantId AND DeptId = @deptId AND PeriodYear = @year AND PeriodMonth = @month;",
            key, tx, cancellationToken: ct));
        var values = new { kind, plantId, deptId, year, month, file.FileName, file.Path, file.ContentType, file.SizeBytes, userId = scope.UserId };
        await conn.ExecuteAsync(new CommandDefinition(
            previous is null
                ? """
                  INSERT INTO kpi.Documents (Kind, PlantId, DeptId, PeriodYear, PeriodMonth, FileName, StoredPath, ContentType, SizeBytes, UploadedBy)
                  VALUES (@kind, @plantId, @deptId, @year, @month, @FileName, @Path, @ContentType, @SizeBytes, @userId);
                  """
                : """
                  UPDATE kpi.Documents SET FileName = @FileName, StoredPath = @Path, ContentType = @ContentType, SizeBytes = @SizeBytes,
                         UploadedBy = @userId, UploadedAt = SYSUTCDATETIME()
                  WHERE Kind = @kind AND PlantId = @plantId AND DeptId = @deptId AND PeriodYear = @year AND PeriodMonth = @month;
                  """,
            values, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return previous;
    }

    public async Task EnsureUploadAllowedAsync(KpiScope scope, string? kind, int plantId, int deptId, int year, int month, CancellationToken ct)
    {
        if (!KpiDocumentKind.IsValid(kind))
        {
            throw new ValidationFailure("kind", "Jenis dokumen tidak dikenal.");
        }

        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            throw new ValidationFailure("month", "Bulan atau tahun tidak valid.");
        }

        if (month > LastMonth(year))
        {
            throw new ValidationFailure("month", "Bulan ini belum berjalan.");
        }

        if (!scope.CanEdit(plantId, deptId))
        {
            throw new ValidationFailure("deptId", "Anda hanya bisa mengunggah untuk departemen sendiri.");
        }

        await using var conn = db.Connection();
        await EnsureParticipantAsync(conn, plantId, deptId, ct);
    }

    public async Task<DownloadRow?> DownloadAsync(KpiScope scope, int documentId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var row = await conn.QuerySingleOrDefaultAsync<DownloadRow>(new CommandDefinition(
            """
            SELECT d.Kind, d.PlantId, d.PeriodMonth AS Month, d.StoredPath, d.ContentType, d.FileName, p.Name AS PlantName, m.Name AS DeptName
            FROM kpi.Documents AS d
            JOIN mst.Plants AS p ON p.PlantId = d.PlantId
            JOIN mst.Departments AS m ON m.DeptId = d.DeptId
            WHERE d.DocumentId = @documentId;
            """,
            new { documentId }, cancellationToken: ct));
        return row is not null && scope.CoversPlant(row.PlantId) ? row : null;
    }

    public async Task<IReadOnlyList<KpiPicaItem>> ListPicasAsync(KpiScope scope, int year, int? plantId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var rows = await conn.QueryAsync<PicaRow>(new CommandDefinition(
            """
            SELECT x.PicaId, x.PlantId, p.Name AS PlantName, x.DeptId, d.Code AS DeptCode, d.Name AS DeptName, x.PeriodYear AS [Year], x.PeriodMonth AS [Month],
                   x.KpiType, x.ProblemIssue, x.CorrectiveAction, x.DueDate, x.PicName, x.Status
            FROM kpi.Picas AS x
            JOIN mst.Plants AS p ON p.PlantId = x.PlantId
            JOIN mst.Departments AS d ON d.DeptId = x.DeptId
            WHERE x.PeriodYear = @year AND (@plantId IS NULL OR x.PlantId = @plantId)
            ORDER BY x.PeriodMonth DESC, x.PicaId DESC;
            """,
            new { year, plantId }, cancellationToken: ct));
        return [.. rows.Where(r => scope.CoversPlant(r.PlantId)).Select(r => new KpiPicaItem(
            r.PicaId, r.PlantId, r.PlantName, r.DeptId, r.DeptCode, r.DeptName, r.Year, r.Month, r.KpiType, r.ProblemIssue, r.CorrectiveAction,
            DateOnly.FromDateTime(r.DueDate), r.PicName, r.Status, scope.CanEdit(r.PlantId, r.DeptId)))];
    }

    public async Task<int> CreatePicaAsync(KpiScope scope, KpiPicaRequest request, CancellationToken ct)
    {
        var v = ValidatePica(scope, request);
        await using var conn = db.Connection();
        await EnsureParticipantAsync(conn, request.PlantId, request.DeptId, ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO kpi.Picas (PlantId, DeptId, PeriodYear, PeriodMonth, KpiType, ProblemIssue, CorrectiveAction, DueDate, PicName, Status, CreatedBy, UpdatedBy)
            OUTPUT inserted.PicaId
            VALUES (@PlantId, @DeptId, @Year, @Month, @KpiType, @Problem, @Action, @Due, @Pic, @Status, @userId, @userId);
            """,
            new { request.PlantId, request.DeptId, request.Year, request.Month, request.KpiType, v.Problem, v.Action, v.Due, v.Pic, request.Status, userId = scope.UserId },
            cancellationToken: ct));
    }

    public async Task<bool> UpdatePicaAsync(KpiScope scope, int picaId, KpiPicaRequest request, CancellationToken ct)
    {
        var v = ValidatePica(scope, request);
        await using var conn = db.Connection();
        if (!await CanTouchPicaAsync(conn, scope, picaId, ct))
        {
            return false;
        }

        await EnsureParticipantAsync(conn, request.PlantId, request.DeptId, ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE kpi.Picas SET PlantId = @PlantId, DeptId = @DeptId, PeriodYear = @Year, PeriodMonth = @Month, KpiType = @KpiType,
                   ProblemIssue = @Problem, CorrectiveAction = @Action, DueDate = @Due, PicName = @Pic, Status = @Status,
                   UpdatedBy = @userId, UpdatedAt = SYSUTCDATETIME()
            WHERE PicaId = @picaId;
            """,
            new { picaId, request.PlantId, request.DeptId, request.Year, request.Month, request.KpiType, v.Problem, v.Action, v.Due, v.Pic, request.Status, userId = scope.UserId },
            cancellationToken: ct));
        return true;
    }

    public async Task<bool> DeletePicaAsync(KpiScope scope, int picaId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        if (!await CanTouchPicaAsync(conn, scope, picaId, ct))
        {
            return false;
        }

        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM kpi.Picas WHERE PicaId = @picaId;", new { picaId }, cancellationToken: ct));
        return true;
    }

    public async Task<IReadOnlyList<int>> ParticipantsAsync(int plantId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return [.. await conn.QueryAsync<int>(new CommandDefinition(
            "SELECT DeptId FROM kpi.PlantDepartments WHERE PlantId = @plantId;", new { plantId }, cancellationToken: ct))];
    }

    public async Task SaveParticipantsAsync(KpiParticipantsRequest request, CancellationToken ct)
    {
        var deptIds = (request.DeptIds ?? []).Distinct().ToArray();
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        var plantExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM mst.Plants WHERE PlantId = @PlantId;", new { request.PlantId }, tx, cancellationToken: ct));
        if (plantExists == 0)
        {
            throw new ValidationFailure("plantId", "Plant tidak ditemukan.");
        }

        var known = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM mst.Departments WHERE DeptId IN @deptIds;", new { deptIds }, tx, cancellationToken: ct));
        if (known != deptIds.Length)
        {
            throw new ValidationFailure("deptIds", "Departemen tidak ditemukan.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM kpi.PlantDepartments WHERE PlantId = @PlantId;", new { request.PlantId }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO kpi.PlantDepartments (PlantId, DeptId) VALUES (@PlantId, @DeptId);",
            deptIds.Select(id => new { request.PlantId, DeptId = id }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private static (string Problem, string Action, DateTime Due, string Pic) ValidatePica(KpiScope scope, KpiPicaRequest r)
    {
        if (r.Year is < 2000 or > 2100 || r.Month is < 1 or > 12)
        {
            throw new ValidationFailure("month", "Bulan atau tahun tidak valid.");
        }

        if (!scope.CanEdit(r.PlantId, r.DeptId))
        {
            throw new ValidationFailure("deptId", "Anda hanya bisa mengisi PICA departemen sendiri.");
        }

        if (r.KpiType is not ("Accident" or "Penyakit"))
        {
            throw new ValidationFailure("kpiType", "Pilih KPI: Accident atau Penyakit.");
        }

        if (r.Status is not ("Open" or "Closed"))
        {
            throw new ValidationFailure("status", "Status harus Open atau Closed.");
        }

        if (r.DueDate is not { } due)
        {
            throw new ValidationFailure("dueDate", "Due date wajib diisi.");
        }

        return (Required(r.ProblemIssue, "problemIssue", "Problem issue", 1000), Required(r.CorrectiveAction, "correctiveAction", "Corrective action", 1000),
            due.ToDateTime(TimeOnly.MinValue), Required(r.PicName, "picName", "PIC", 100));
    }

    private static string Required(string? value, string field, string label, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ValidationFailure(field, $"{label} wajib diisi.");
        }

        return text.Length <= max ? text : throw new ValidationFailure(field, $"{label} maksimal {max} karakter.");
    }

    private static async Task<bool> CanTouchPicaAsync(SqlConnection conn, KpiScope scope, int picaId, CancellationToken ct)
    {
        var row = await conn.QuerySingleOrDefaultAsync<(int PlantId, int DeptId)?>(new CommandDefinition(
            "SELECT PlantId, DeptId FROM kpi.Picas WHERE PicaId = @picaId;", new { picaId }, cancellationToken: ct));
        return row is { } r && scope.CanEdit(r.PlantId, r.DeptId);
    }

    private static async Task EnsureParticipantAsync(SqlConnection conn, int plantId, int deptId, CancellationToken ct)
    {
        var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM kpi.PlantDepartments WHERE PlantId = @plantId AND DeptId = @deptId;", new { plantId, deptId }, cancellationToken: ct));
        if (count == 0)
        {
            throw new ValidationFailure("deptId", "Departemen ini tidak terdaftar untuk KPI di plant tersebut.");
        }
    }

    private static async Task<List<PlantRow>> PlantsAsync(SqlConnection conn, KpiScope scope, CancellationToken ct) =>
        [.. (await conn.QueryAsync<PlantRow>(new CommandDefinition(
            "SELECT PlantId, Code, Name FROM mst.Plants ORDER BY PlantId;", cancellationToken: ct))).Where(p => scope.CoversPlant(p.PlantId))];

    private static async Task<List<DepartmentRow>> DepartmentsAsync(SqlConnection conn, CancellationToken ct) =>
        [.. await conn.QueryAsync<DepartmentRow>(new CommandDefinition(
            """
            SELECT x.PlantId, d.DeptId, d.Code, d.Name
            FROM kpi.PlantDepartments AS x JOIN mst.Departments AS d ON d.DeptId = x.DeptId
            WHERE d.IsActive = 1
            ORDER BY d.Name;
            """, cancellationToken: ct))];

    /// <summary>12 bulan satu departemen, atau gabungan semua departemen kalau <paramref name="deptId"/> null.</summary>
    private static async Task<IReadOnlyList<KpiMonthData>> MonthsAsync(SqlConnection conn, int year, int plantId, int? deptId, CancellationToken ct)
    {
        var inputs = (await conn.QueryAsync<InputRow>(new CommandDefinition(
            """
            SELECT PlantId, DeptId, PeriodMonth AS Month, AccidentCount, DiseaseCount, DiseaseTypes, AccidentNote
            FROM kpi.MonthlyInputs
            WHERE PlantId = @plantId AND PeriodYear = @year AND (@deptId IS NULL OR DeptId = @deptId);
            """,
            new { plantId, year, deptId }, cancellationToken: ct))).ToLookup(i => i.Month);

        // ALL mencakup juga temuan yang belum ditugaskan ke departemen.
        var findings = (await conn.QueryAsync<FindingMonthRow>(new CommandDefinition(
            """
            SELECT PeriodMonth AS Month, COUNT(*) AS Findings, SUM(CASE WHEN Status = 'Close' THEN 1 ELSE 0 END) AS Closed
            FROM patrol.Findings
            WHERE PlantId = @plantId AND PeriodYear = @year AND (@deptId IS NULL OR DeptId = @deptId)
            GROUP BY PeriodMonth;
            """,
            new { plantId, year, deptId }, cancellationToken: ct))).ToDictionary(f => f.Month);

        return [.. Enumerable.Range(1, KpiCalculator.MonthCount).Select(month =>
        {
            var rows = inputs[month].ToList();
            var finding = findings.GetValueOrDefault(month);
            return new KpiMonthData(
                rows.Any(r => r.AccidentCount is not null) ? rows.Sum(r => r.AccidentCount ?? 0) : null,
                rows.Any(r => r.DiseaseCount is not null) ? rows.Sum(r => r.DiseaseCount ?? 0) : null,
                Join(rows.Select(r => r.DiseaseTypes)),
                Join(rows.Select(r => r.AccidentNote)),
                finding?.Findings ?? 0,
                finding?.Closed ?? 0);
        })];
    }

    private static string? Join(IEnumerable<string?> values)
    {
        var parts = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public sealed class DownloadRow
    {
        public string Kind { get; set; } = string.Empty;
        public int PlantId { get; set; }
        public int Month { get; set; }
        public string StoredPath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string PlantName { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
    }

    private sealed class PlantRow
    {
        public int PlantId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DepartmentRow
    {
        public int PlantId { get; set; }
        public int DeptId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public KpiDepartment ToDepartment() => new(DeptId, Code, Name);
    }

    private sealed class InputRow
    {
        public int PlantId { get; set; }
        public int DeptId { get; set; }
        public int Month { get; set; }
        public int? AccidentCount { get; set; }
        public int? DiseaseCount { get; set; }
        public string? DiseaseTypes { get; set; }
        public string? AccidentNote { get; set; }
    }

    private sealed class FindingRow
    {
        public int PlantId { get; set; }
        public int Findings { get; set; }
        public int Closed { get; set; }
    }

    private sealed class FindingMonthRow
    {
        public int Month { get; set; }
        public int Findings { get; set; }
        public int Closed { get; set; }
    }

    private sealed class DocumentRow
    {
        public int DocumentId { get; set; }
        public int DeptId { get; set; }
        public int Month { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string UploadedByName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    private sealed class PicaRow
    {
        public int PicaId { get; set; }
        public int PlantId { get; set; }
        public string PlantName { get; set; } = string.Empty;
        public int DeptId { get; set; }
        public string DeptCode { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public string KpiType { get; set; } = string.Empty;
        public string ProblemIssue { get; set; } = string.Empty;
        public string CorrectiveAction { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public string PicName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
