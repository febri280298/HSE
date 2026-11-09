using Dapper;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using HseSystem.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;

namespace HseSystem.Api.Master;

public sealed record AreaRequest(int PlantId, string? Name, bool IsActive);

public sealed record AreaResponse(int AreaId, int PlantId, string Name, bool IsActive);

public sealed record DepartmentRequest(string? Code, string? Name, bool IsActive);

public sealed record DepartmentResponse(int DeptId, string Code, string Name, bool IsActive);

/// <summary>Master area per plant dan departemen, untuk pilihan di form temuan.</summary>
public static class MasterDataEndpoints
{
    public static RouteGroupBuilder MapMasterDataEndpoints(this RouteGroupBuilder api)
    {
        var master = api.MapGroup("/master").RequireAuthorization(Permissions.MasterKelola);
        master.MapGet("/areas", ListAreasAsync);
        master.MapPost("/areas", CreateAreaAsync);
        master.MapPut("/areas/{areaId:int}", UpdateAreaAsync);
        master.MapGet("/departments", ListDepartmentsAsync);
        master.MapPost("/departments", CreateDepartmentAsync);
        master.MapPut("/departments/{deptId:int}", UpdateDepartmentAsync);
        return master;
    }

    internal static async Task<Ok<List<AreaResponse>>> ListAreasAsync(int? plantId, HseDb db, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var rows = await conn.QueryAsync<AreaResponse>(new CommandDefinition(
            "SELECT AreaId, PlantId, Name, IsActive FROM mst.Areas WHERE @plantId IS NULL OR PlantId = @plantId ORDER BY Name;",
            new { plantId }, cancellationToken: ct));
        return TypedResults.Ok(rows.ToList());
    }

    internal static async Task<Results<Ok<AreaResponse>, ValidationProblem>> CreateAreaAsync(AreaRequest request, HseDb db, CancellationToken ct)
    {
        var name = Text(request.Name, 100);
        if (name is null)
        {
            return Problems.Invalid("name", "Nama area wajib diisi (maks. 100 karakter).");
        }

        try
        {
            await using var conn = db.Connection();
            var plantExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM mst.Plants WHERE PlantId = @PlantId;", new { request.PlantId }, cancellationToken: ct));
            if (plantExists == 0)
            {
                return Problems.Invalid("plantId", "Plant tidak ditemukan.");
            }

            return TypedResults.Ok(await conn.QuerySingleAsync<AreaResponse>(new CommandDefinition(
                """
                INSERT INTO mst.Areas (PlantId, Name, IsActive)
                OUTPUT inserted.AreaId, inserted.PlantId, inserted.Name, inserted.IsActive
                VALUES (@PlantId, @name, 1);
                """,
                new { request.PlantId, name }, cancellationToken: ct)));
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            return Problems.Invalid("name", "Area ini sudah ada di plant tersebut.");
        }
    }

    internal static async Task<Results<Ok<AreaResponse>, ValidationProblem, NotFound>> UpdateAreaAsync(
        int areaId, AreaRequest request, HseDb db, CancellationToken ct)
    {
        var name = Text(request.Name, 100);
        if (name is null)
        {
            return Problems.Invalid("name", "Nama area wajib diisi (maks. 100 karakter).");
        }

        try
        {
            await using var conn = db.Connection();
            var row = await conn.QuerySingleOrDefaultAsync<AreaResponse>(new CommandDefinition(
                """
                UPDATE mst.Areas SET Name = @name, IsActive = @IsActive
                OUTPUT inserted.AreaId, inserted.PlantId, inserted.Name, inserted.IsActive
                WHERE AreaId = @areaId;
                """,
                new { areaId, name, request.IsActive }, cancellationToken: ct));
            return row is null ? TypedResults.NotFound() : TypedResults.Ok(row);
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            return Problems.Invalid("name", "Area ini sudah ada di plant tersebut.");
        }
    }

    internal static async Task<Ok<List<DepartmentResponse>>> ListDepartmentsAsync(HseDb db, CancellationToken ct)
    {
        await using var conn = db.Connection();
        var rows = await conn.QueryAsync<DepartmentResponse>(new CommandDefinition(
            "SELECT DeptId, Code, Name, IsActive FROM mst.Departments ORDER BY Name;", cancellationToken: ct));
        return TypedResults.Ok(rows.ToList());
    }

    internal static async Task<Results<Ok<DepartmentResponse>, ValidationProblem>> CreateDepartmentAsync(
        DepartmentRequest request, HseDb db, CancellationToken ct)
    {
        if (ValidateDepartment(request, out var code, out var name) is { } problem)
        {
            return problem;
        }

        try
        {
            await using var conn = db.Connection();
            return TypedResults.Ok(await conn.QuerySingleAsync<DepartmentResponse>(new CommandDefinition(
                """
                INSERT INTO mst.Departments (Code, Name, IsActive)
                OUTPUT inserted.DeptId, inserted.Code, inserted.Name, inserted.IsActive
                VALUES (@code, @name, 1);
                """,
                new { code, name }, cancellationToken: ct)));
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            return Problems.Invalid("code", "Kode departemen sudah dipakai.");
        }
    }

    internal static async Task<Results<Ok<DepartmentResponse>, ValidationProblem, NotFound>> UpdateDepartmentAsync(
        int deptId, DepartmentRequest request, HseDb db, CancellationToken ct)
    {
        if (ValidateDepartment(request, out var code, out var name) is { } problem)
        {
            return problem;
        }

        try
        {
            await using var conn = db.Connection();
            var row = await conn.QuerySingleOrDefaultAsync<DepartmentResponse>(new CommandDefinition(
                """
                UPDATE mst.Departments SET Code = @code, Name = @name, IsActive = @IsActive
                OUTPUT inserted.DeptId, inserted.Code, inserted.Name, inserted.IsActive
                WHERE DeptId = @deptId;
                """,
                new { deptId, code, name, request.IsActive }, cancellationToken: ct));
            return row is null ? TypedResults.NotFound() : TypedResults.Ok(row);
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            return Problems.Invalid("code", "Kode departemen sudah dipakai.");
        }
    }

    private static ValidationProblem? ValidateDepartment(DepartmentRequest request, out string code, out string name)
    {
        code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        name = request.Name?.Trim() ?? string.Empty;
        if (code.Length is < 2 or > 10)
        {
            return Problems.Invalid("code", "Kode departemen 2-10 karakter.");
        }

        return name.Length is < 1 or > 100 ? Problems.Invalid("name", "Nama departemen wajib diisi (maks. 100 karakter).") : null;
    }

    private static string? Text(string? value, int maxLength)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > maxLength ? null : text;
    }
}
