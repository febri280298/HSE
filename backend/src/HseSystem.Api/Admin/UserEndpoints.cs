using System.Security.Claims;
using HseSystem.Api.Auth;
using HseSystem.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HseSystem.Api.Admin;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/admin/users").RequireAuthorization(Permissions.PenggunaKelola);
        users.MapGet("/", async (UserStore store, CancellationToken ct) => TypedResults.Ok(await store.ListAsync(ct)));
        users.MapGet("/options", async (UserStore store, CancellationToken ct) => TypedResults.Ok(await store.OptionsAsync(ct)));
        users.MapPost("/", CreateAsync);
        users.MapPut("/{userId:int}", UpdateAsync);
        users.MapPost("/{userId:int}/reset-password", ResetPasswordAsync);
        return users;
    }

    internal static async Task<Results<Ok<UserResponse>, ValidationProblem>> CreateAsync(
        CreateUserRequest request, UserStore store, IPasswordHasher<AuthAccount> hasher, CancellationToken ct)
    {
        try
        {
            var username = request.Username?.Trim() ?? string.Empty;
            if (PasswordRules.ValidateUsername(username) is { } usernameProblem)
            {
                throw new ValidationFailure("username", usernameProblem);
            }

            var data = Validate(request.DisplayName, request.Email, request.IsActive, request.RoleIds, request.PlantIds, request.DeptId);
            if (PasswordRules.Validate(request.Password, username) is { } passwordProblem)
            {
                throw new ValidationFailure("password", passwordProblem);
            }

            var userId = await store.CreateAsync(username, hasher.HashPassword(AuthAccount.Instance, request.Password!), data, ct);
            return TypedResults.Ok((await store.ListAsync(ct, userId)).Single());
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<Ok<UserResponse>, ValidationProblem, NotFound>> UpdateAsync(
        int userId, UpdateUserRequest request, ClaimsPrincipal user, UserStore store, UserSessions sessions, CancellationToken ct)
    {
        try
        {
            var data = Validate(request.DisplayName, request.Email, request.IsActive, request.RoleIds, request.PlantIds, request.DeptId);
            if (userId == user.GetUserId() && !data.IsActive)
            {
                throw new ValidationFailure("isActive", "Anda tidak bisa menonaktifkan akun sendiri.");
            }

            if (!await store.UpdateAsync(userId, data, ct))
            {
                return TypedResults.NotFound();
            }

            sessions.Invalidate(userId);
            return TypedResults.Ok((await store.ListAsync(ct, userId)).Single());
        }
        catch (ValidationFailure ex)
        {
            return Problems.Invalid(ex.Field, ex.Message);
        }
    }

    internal static async Task<Results<NoContent, ValidationProblem, NotFound>> ResetPasswordAsync(
        int userId, ResetPasswordRequest request, UserStore store, UserSessions sessions,
        IPasswordHasher<AuthAccount> hasher, CancellationToken ct)
    {
        if (await store.GetUsernameAsync(userId, ct) is not { } username)
        {
            return TypedResults.NotFound();
        }

        if (PasswordRules.Validate(request.NewPassword, username) is { } problem)
        {
            return Problems.Invalid("newPassword", problem);
        }

        await store.ResetPasswordAsync(userId, hasher.HashPassword(AuthAccount.Instance, request.NewPassword!), ct);
        sessions.Invalidate(userId);
        return TypedResults.NoContent();
    }

    private static UserData Validate(string? displayName, string? email, bool isActive, int[]? roleIds, int[]? plantIds, int? deptId)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
        {
            throw new ValidationFailure("displayName", "Nama wajib diisi (maks. 100 karakter).");
        }

        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (mail is not null && (mail.Length > 200 || !mail.Contains('@')))
        {
            throw new ValidationFailure("email", "Format email tidak valid.");
        }

        var roles = (roleIds ?? []).Distinct().ToList();
        if (roles.Count == 0)
        {
            throw new ValidationFailure("roleIds", "Pilih minimal satu peran.");
        }

        return new UserData(name, mail, isActive, roles, [.. (plantIds ?? []).Distinct()], deptId);
    }
}
