using Dapper;
using HseSystem.Api.Data;

namespace HseSystem.Api.Auth;

internal sealed record LoginRow(int UserId, string PasswordHash, bool IsActive, int LockedSeconds);

internal sealed record SessionRow(
    int UserId, string Username, string DisplayName, bool IsActive, bool MustChangePassword, Guid SecurityStamp, int? DeptId, string? DeptName);

internal sealed record SessionRoleRow(string Name, bool IsSystem);

/// <summary>Akses tabel auth.* untuk login, sesi, dan password.</summary>
public sealed class AuthStore(HseDb db)
{
    /// <summary>Salah password sebanyak ini berturut-turut → akun dikunci <see cref="LockMinutes"/> menit.</summary>
    public const int MaxFailedLogins = 5;
    public const int LockMinutes = 5;

    internal async Task<LoginRow?> FindLoginAsync(string username, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.QuerySingleOrDefaultAsync<LoginRow>(new CommandDefinition(
            """
            SELECT UserId, PasswordHash, IsActive,
                   CASE WHEN LockoutUntil > SYSUTCDATETIME() THEN DATEDIFF(second, SYSUTCDATETIME(), LockoutUntil) ELSE 0 END AS LockedSeconds
            FROM auth.Users
            WHERE Username = @username;
            """,
            new { username },
            cancellationToken: ct));
    }

    /// <summary>Catat password salah. Mengembalikan <c>true</c> kalau akun baru saja dikunci.</summary>
    public async Task<bool> RecordFailedLoginAsync(int userId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            UPDATE auth.Users
            SET LockoutUntil     = CASE WHEN FailedLoginCount + 1 >= @max THEN DATEADD(minute, @minutes, SYSUTCDATETIME()) ELSE LockoutUntil END,
                FailedLoginCount = CASE WHEN FailedLoginCount + 1 >= @max THEN 0 ELSE FailedLoginCount + 1 END
            WHERE UserId = @userId;

            SELECT CAST(CASE WHEN LockoutUntil > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) FROM auth.Users WHERE UserId = @userId;
            """,
            new { userId, max = MaxFailedLogins, minutes = LockMinutes },
            cancellationToken: ct));
    }

    /// <summary>Login berhasil: reset hitungan salah; simpan hash baru kalau algoritma hash diperbarui.</summary>
    public async Task RecordSuccessfulLoginAsync(int userId, string? newPasswordHash, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth.Users
            SET FailedLoginCount = 0, LockoutUntil = NULL, LastLoginAt = SYSUTCDATETIME(),
                PasswordHash = COALESCE(@newPasswordHash, PasswordHash)
            WHERE UserId = @userId;
            """,
            new { userId, newPasswordHash },
            cancellationToken: ct));
    }

    public async Task<string?> GetPasswordHashAsync(int userId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT PasswordHash FROM auth.Users WHERE UserId = @userId;", new { userId }, cancellationToken: ct));
    }

    /// <summary>Ganti password. SecurityStamp diganti sehingga sesi login lain (perangkat lain) ikut keluar.</summary>
    public async Task<bool> SetPasswordAsync(int userId, string passwordHash, bool mustChangePassword, CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth.Users
            SET PasswordHash = @passwordHash, MustChangePassword = @mustChangePassword, SecurityStamp = NEWID(),
                FailedLoginCount = 0, LockoutUntil = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @userId;
            """,
            new { userId, passwordHash, mustChangePassword },
            cancellationToken: ct)) > 0;
    }

    public async Task<UserSession?> GetSessionAsync(int userId, CancellationToken ct)
    {
        await using var conn = db.Connection();
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT u.UserId, u.Username, u.DisplayName, u.IsActive, u.MustChangePassword, u.SecurityStamp, u.DeptId, d.Name AS DeptName
            FROM auth.Users AS u LEFT JOIN mst.Departments AS d ON d.DeptId = u.DeptId
            WHERE u.UserId = @userId;

            SELECT r.Name, r.IsSystem
            FROM auth.UserRoles AS ur JOIN auth.Roles AS r ON r.RoleId = ur.RoleId
            WHERE ur.UserId = @userId ORDER BY r.Name;

            SELECT DISTINCT rp.Permission
            FROM auth.UserRoles AS ur JOIN auth.RolePermissions AS rp ON rp.RoleId = ur.RoleId
            WHERE ur.UserId = @userId;

            SELECT p.PlantId, p.Code, p.Name
            FROM auth.UserPlants AS up JOIN mst.Plants AS p ON p.PlantId = up.PlantId
            WHERE up.UserId = @userId ORDER BY p.SortOrder, p.Code;
            """,
            new { userId },
            cancellationToken: ct));

        var user = await grid.ReadSingleOrDefaultAsync<SessionRow>();
        if (user is null)
        {
            return null;
        }

        var roles = (await grid.ReadAsync<SessionRoleRow>()).ToList();
        var granted = (await grid.ReadAsync<string>()).ToHashSet(StringComparer.Ordinal);
        var plants = (await grid.ReadAsync<PlantInfo>()).ToList();

        var isAdministrator = roles.Any(r => r.IsSystem);
        var permissions = Permissions.All
            .Select(p => p.Code)
            .Where(code => isAdministrator || granted.Contains(code))
            .ToList();

        return new UserSession(
            user.UserId, user.Username, user.DisplayName, user.IsActive, user.MustChangePassword, user.SecurityStamp,
            user.DeptId, user.DeptName, isAdministrator, [.. roles.Select(r => r.Name)], permissions, plants);
    }

    public async Task<bool> AnyUserAsync(CancellationToken ct)
    {
        await using var conn = db.Connection();
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM auth.Users) THEN 1 ELSE 0 END AS bit);", cancellationToken: ct));
    }

    /// <summary>Pengguna pertama, berperan Administrator dan wajib ganti password saat login pertama.</summary>
    public async Task CreateBootstrapAdminAsync(string username, string displayName, string passwordHash, CancellationToken ct)
    {
        await using var conn = db.Connection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @roleId int = (SELECT TOP (1) RoleId FROM auth.Roles WHERE IsSystem = 1 ORDER BY RoleId);
            IF @roleId IS NULL
                THROW 50000, 'Peran Administrator belum ada. Jalankan db/002_auth_dan_master.sql dulu.', 1;
            INSERT INTO auth.Users (Username, DisplayName, PasswordHash, MustChangePassword) VALUES (@username, @displayName, @passwordHash, 1);
            INSERT INTO auth.UserRoles (UserId, RoleId) VALUES (SCOPE_IDENTITY(), @roleId);
            COMMIT TRANSACTION;
            """,
            new { username, displayName, passwordHash },
            cancellationToken: ct));
    }
}
