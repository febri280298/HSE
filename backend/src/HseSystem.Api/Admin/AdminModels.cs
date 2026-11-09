using HseSystem.Api.Auth;
using HseSystem.Api.Patrol;

namespace HseSystem.Api.Admin;

public sealed record RoleRequest(string? Name, string? Description, string[]? Permissions);

public sealed record RoleResponse(
    int RoleId, string Name, string? Description, bool IsSystem, int UserCount, IReadOnlyList<string> Permissions);

public sealed record CreateUserRequest(
    string? Username, string? DisplayName, string? Email, string? Password, bool IsActive, int[]? RoleIds, int[]? PlantIds, int? DeptId);

public sealed record UpdateUserRequest(string? DisplayName, string? Email, bool IsActive, int[]? RoleIds, int[]? PlantIds, int? DeptId);

public sealed record ResetPasswordRequest(string? NewPassword);

public sealed record UserResponse(
    int UserId,
    string Username,
    string DisplayName,
    string? Email,
    int? DeptId,
    bool IsActive,
    bool MustChangePassword,
    bool IsLocked,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<int> RoleIds,
    IReadOnlyList<int> PlantIds);

public sealed record RoleOption(int RoleId, string Name, bool IsSystem);

/// <summary>Pilihan untuk form pengguna.</summary>
public sealed record UserOptionsResponse(
    IReadOnlyList<RoleOption> Roles, IReadOnlyList<PlantInfo> Plants, IReadOnlyList<DepartmentOption> Departments);
