namespace HseSystem.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record PlantInfo(int PlantId, string Code, string Name);

/// <summary>Keadaan pengguna terkini dari database (di-cache sebentar oleh <see cref="UserSessions"/>).</summary>
public sealed record UserSession(
    int UserId,
    string Username,
    string DisplayName,
    bool IsActive,
    bool MustChangePassword,
    Guid SecurityStamp,
    int? DeptId,
    string? DeptName,
    bool IsAdministrator,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<PlantInfo> Plants)
{
    /// <summary>Hak akses yang benar-benar berlaku: kosong selama pengguna belum mengganti password awal.</summary>
    public IReadOnlyList<string> EffectivePermissions => MustChangePassword ? [] : Permissions;
}

/// <summary>Balasan /api/auth/me dan login: dipakai frontend untuk menu, guard, dan header.</summary>
public sealed record CurrentUserResponse(
    int UserId,
    string Username,
    string DisplayName,
    int? DeptId,
    string? DeptName,
    bool MustChangePassword,
    bool IsAdministrator,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<PlantInfo> Plants)
{
    public static CurrentUserResponse From(UserSession s) => new(
        s.UserId, s.Username, s.DisplayName, s.DeptId, s.DeptName, s.MustChangePassword, s.IsAdministrator, s.Roles, s.EffectivePermissions, s.Plants);
}

/// <summary>Penanda tipe untuk <c>PasswordHasher&lt;T&gt;</c> (PBKDF2 bawaan ASP.NET Core).</summary>
public sealed class AuthAccount
{
    public static readonly AuthAccount Instance = new();

    private AuthAccount()
    {
    }
}
