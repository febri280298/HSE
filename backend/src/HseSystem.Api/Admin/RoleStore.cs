using Dapper;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using HseSystem.Api.Data;
using Microsoft.Data.SqlClient;

namespace HseSystem.Api.Admin;

internal sealed record RoleRow(int RoleId, string Name, string? Description, bool IsSystem, int UserCount);

internal sealed record RolePermissionRow(int RoleId, string Permission);

internal sealed record RoleGuardRow(bool IsSystem, int UserCount);

public sealed class RoleStore(HseDb db)
{
    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct)
    {
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT r.RoleId, r.Name, r.Description, r.IsSystem,
                   (SELECT COUNT(*) FROM auth.UserRoles AS ur WHERE ur.RoleId = r.RoleId) AS UserCount
            FROM auth.Roles AS r
            ORDER BY r.IsSystem DESC, r.Name;

            SELECT RoleId, Permission FROM auth.RolePermissions;
            """,
            cancellationToken: ct));

        var roles = (await grid.ReadAsync<RoleRow>()).ToList();
        var permissions = (await grid.ReadAsync<RolePermissionRow>()).ToLookup(p => p.RoleId, p => p.Permission);
        var allCodes = Permissions.All.Select(p => p.Code).ToList();

        return [.. roles.Select(r => new RoleResponse(
            r.RoleId, r.Name, r.Description, r.IsSystem, r.UserCount,
            // Administrator selalu punya semua hak; peran lain hanya hak yang masih dikenal aplikasi.
            r.IsSystem ? allCodes : [.. allCodes.Where(code => permissions[r.RoleId].Contains(code))]))];
    }

    public async Task<int> CreateAsync(string name, string? description, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            var roleId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "INSERT INTO auth.Roles (Name, Description) VALUES (@name, @description); SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { name, description }, tx, cancellationToken: ct));
            await ReplacePermissionsAsync(conn, tx, roleId, permissions, ct);
            await tx.CommitAsync(ct);
            return roleId;
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            throw new ValidationFailure("name", "Nama peran sudah dipakai.");
        }
    }

    /// <returns><c>false</c> kalau peran tidak ada.</returns>
    public async Task<bool> UpdateAsync(int roleId, string name, string? description, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var guard = await GuardAsync(conn, tx, roleId, ct);
        if (guard is null)
        {
            return false;
        }

        if (guard.IsSystem)
        {
            throw new ValidationFailure("name", "Peran Administrator tidak bisa diubah.");
        }

        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE auth.Roles SET Name = @name, Description = @description, UpdatedAt = SYSUTCDATETIME() WHERE RoleId = @roleId;",
                new { roleId, name, description }, tx, cancellationToken: ct));
            await ReplacePermissionsAsync(conn, tx, roleId, permissions, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            throw new ValidationFailure("name", "Nama peran sudah dipakai.");
        }
    }

    /// <returns><c>false</c> kalau peran tidak ada.</returns>
    public async Task<bool> DeleteAsync(int roleId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        var guard = await GuardAsync(conn, tx, roleId, ct);
        if (guard is null)
        {
            return false;
        }

        if (guard.IsSystem)
        {
            throw new ValidationFailure("role", "Peran Administrator tidak bisa dihapus.");
        }

        if (guard.UserCount > 0)
        {
            throw new ValidationFailure("role", $"Peran masih dipakai {guard.UserCount} pengguna. Lepaskan dulu dari pengguna tersebut.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM auth.Roles WHERE RoleId = @roleId;", new { roleId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    private static Task<RoleGuardRow?> GuardAsync(SqlConnection conn, SqlTransaction tx, int roleId, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<RoleGuardRow>(new CommandDefinition(
            """
            SELECT r.IsSystem, (SELECT COUNT(*) FROM auth.UserRoles AS ur WHERE ur.RoleId = r.RoleId) AS UserCount
            FROM auth.Roles AS r WITH (UPDLOCK) WHERE r.RoleId = @roleId;
            """,
            new { roleId }, tx, cancellationToken: ct));

    private static async Task ReplacePermissionsAsync(
        SqlConnection conn, SqlTransaction tx, int roleId, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM auth.RolePermissions WHERE RoleId = @roleId;", new { roleId }, tx, cancellationToken: ct));
        if (permissions.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO auth.RolePermissions (RoleId, Permission) VALUES (@RoleId, @Permission);",
                permissions.Select(p => new { RoleId = roleId, Permission = p }), tx, cancellationToken: ct));
        }
    }
}
