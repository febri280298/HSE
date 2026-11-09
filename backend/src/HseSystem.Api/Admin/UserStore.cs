using Dapper;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using HseSystem.Api.Data;
using Microsoft.Data.SqlClient;

namespace HseSystem.Api.Admin;

internal sealed record UserRow(
    int UserId, string Username, string DisplayName, string? Email, int? DeptId, bool IsActive, bool MustChangePassword, bool IsLocked, DateTime? LastLoginAt);

internal sealed record UserLinkRow(int UserId, int LinkId);

/// <summary>Data pengguna untuk disimpan. Password hanya diisi saat membuat pengguna baru.</summary>
public sealed record UserData(
    string DisplayName, string? Email, bool IsActive, IReadOnlyList<int> RoleIds, IReadOnlyList<int> PlantIds, int? DeptId);

public sealed class UserStore(HseDb db)
{
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct, int? userId = null)
    {
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT UserId, Username, DisplayName, Email, DeptId, IsActive, MustChangePassword,
                   CAST(CASE WHEN LockoutUntil > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLocked, LastLoginAt
            FROM auth.Users WHERE @userId IS NULL OR UserId = @userId
            ORDER BY DisplayName, Username;

            SELECT UserId, RoleId AS LinkId FROM auth.UserRoles WHERE @userId IS NULL OR UserId = @userId;
            SELECT UserId, PlantId AS LinkId FROM auth.UserPlants WHERE @userId IS NULL OR UserId = @userId;
            """,
            new { userId },
            cancellationToken: ct));

        var users = await grid.ReadAsync<UserRow>();
        var roles = (await grid.ReadAsync<UserLinkRow>()).ToLookup(r => r.UserId, r => r.LinkId);
        var plants = (await grid.ReadAsync<UserLinkRow>()).ToLookup(r => r.UserId, r => r.LinkId);

        return [.. users.Select(u => new UserResponse(
            u.UserId, u.Username, u.DisplayName, u.Email, u.DeptId, u.IsActive, u.MustChangePassword, u.IsLocked,
            u.LastLoginAt is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null,
            [.. roles[u.UserId].Order()], [.. plants[u.UserId].Order()]))];
    }

    public async Task<UserOptionsResponse> OptionsAsync(CancellationToken ct)
    {
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT RoleId, Name, IsSystem FROM auth.Roles ORDER BY IsSystem DESC, Name;
            SELECT PlantId, Code, Name FROM mst.Plants WHERE IsActive = 1 ORDER BY SortOrder, Code;
            SELECT DeptId, Code, Name FROM mst.Departments WHERE IsActive = 1 ORDER BY Name;
            """,
            cancellationToken: ct));
        return new UserOptionsResponse(
            [.. await grid.ReadAsync<RoleOption>()],
            [.. await grid.ReadAsync<PlantInfo>()],
            [.. await grid.ReadAsync<HseSystem.Api.Patrol.DepartmentOption>()]);
    }

    public async Task<int> CreateAsync(string username, string passwordHash, UserData data, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        await EnsureLinksExistAsync(conn, tx, data, ct);
        try
        {
            var userId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO auth.Users (Username, DisplayName, Email, PasswordHash, IsActive, MustChangePassword, DeptId)
                VALUES (@username, @DisplayName, @Email, @passwordHash, @IsActive, 1, @DeptId);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """,
                new { username, passwordHash, data.DisplayName, data.Email, data.IsActive, data.DeptId }, tx, cancellationToken: ct));
            await ReplaceLinksAsync(conn, tx, userId, data, ct);
            await tx.CommitAsync(ct);
            return userId;
        }
        catch (SqlException ex) when (HseDb.IsUniqueViolation(ex))
        {
            throw new ValidationFailure("username", "Username sudah dipakai.");
        }
    }

    /// <returns><c>false</c> kalau pengguna tidak ada.</returns>
    public async Task<bool> UpdateAsync(int userId, UserData data, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        await EnsureLinksExistAsync(conn, tx, data, ct);

        // Dinonaktifkan → SecurityStamp baru, sesi pengguna itu langsung tidak berlaku.
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth.Users
            SET DisplayName = @DisplayName, Email = @Email, IsActive = @IsActive, DeptId = @DeptId,
                SecurityStamp = CASE WHEN IsActive = 1 AND @IsActive = 0 THEN NEWID() ELSE SecurityStamp END,
                UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @userId;
            """,
            new { userId, data.DisplayName, data.Email, data.IsActive, data.DeptId }, tx, cancellationToken: ct));
        if (updated == 0)
        {
            return false;
        }

        await ReplaceLinksAsync(conn, tx, userId, data, ct);
        await EnsureAdministratorRemainsAsync(conn, tx, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Password sementara dari admin: pengguna wajib menggantinya saat login berikutnya.</summary>
    public async Task<bool> ResetPasswordAsync(int userId, string passwordHash, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth.Users
            SET PasswordHash = @passwordHash, MustChangePassword = 1, SecurityStamp = NEWID(),
                FailedLoginCount = 0, LockoutUntil = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @userId;
            """,
            new { userId, passwordHash }, cancellationToken: ct)) > 0;
    }

    public async Task<string?> GetUsernameAsync(int userId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT Username FROM auth.Users WHERE UserId = @userId;", new { userId }, cancellationToken: ct));
    }

    private static async Task EnsureLinksExistAsync(SqlConnection conn, SqlTransaction tx, UserData data, CancellationToken ct)
    {
        var counts = await conn.QuerySingleAsync<(int Roles, int Plants, int Departments)>(new CommandDefinition(
            """
            SELECT (SELECT COUNT(*) FROM auth.Roles WHERE RoleId IN @RoleIds),
                   (SELECT COUNT(*) FROM mst.Plants WHERE PlantId IN @PlantIds),
                   (SELECT COUNT(*) FROM mst.Departments WHERE DeptId = @DeptId);
            """,
            new { data.RoleIds, data.PlantIds, data.DeptId }, tx, cancellationToken: ct));

        if (data.DeptId is not null && counts.Departments == 0)
        {
            throw new ValidationFailure("deptId", "Departemen tidak ditemukan.");
        }

        if (counts.Roles != data.RoleIds.Count)
        {
            throw new ValidationFailure("roleIds", "Ada peran yang tidak ditemukan. Muat ulang halaman lalu coba lagi.");
        }

        if (counts.Plants != data.PlantIds.Count)
        {
            throw new ValidationFailure("plantIds", "Ada plant yang tidak ditemukan. Muat ulang halaman lalu coba lagi.");
        }
    }

    private static async Task ReplaceLinksAsync(SqlConnection conn, SqlTransaction tx, int userId, UserData data, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM auth.UserRoles WHERE UserId = @userId; DELETE FROM auth.UserPlants WHERE UserId = @userId;",
            new { userId }, tx, cancellationToken: ct));

        if (data.RoleIds.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO auth.UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);",
                data.RoleIds.Select(roleId => new { UserId = userId, RoleId = roleId }), tx, cancellationToken: ct));
        }

        if (data.PlantIds.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO auth.UserPlants (UserId, PlantId) VALUES (@UserId, @PlantId);",
                data.PlantIds.Select(plantId => new { UserId = userId, PlantId = plantId }), tx, cancellationToken: ct));
        }
    }

    /// <summary>Jangan sampai tidak ada lagi Administrator aktif (tidak ada yang bisa mengelola sistem).</summary>
    private static async Task EnsureAdministratorRemainsAsync(SqlConnection conn, SqlTransaction tx, CancellationToken ct)
    {
        var administrators = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM auth.Users AS u
            WHERE u.IsActive = 1
              AND EXISTS (SELECT 1 FROM auth.UserRoles AS ur JOIN auth.Roles AS r ON r.RoleId = ur.RoleId
                          WHERE ur.UserId = u.UserId AND r.IsSystem = 1);
            """,
            transaction: tx, cancellationToken: ct));

        if (administrators == 0)
        {
            throw new ValidationFailure("roleIds", "Minimal harus ada satu Administrator aktif.");
        }
    }
}
