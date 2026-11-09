using System.Security.Claims;
using HseSystem.Api.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HseSystem.Api.Auth;

public static class AuthEndpoints
{
    private const string InvalidLogin = "Username atau password salah.";

    // Hash palsu untuk menyamakan waktu jawab ketika username tidak ada (mencegah menebak username dari waktu).
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<AuthAccount>().HashPassword(AuthAccount.Instance, Guid.NewGuid().ToString()));

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(AuthSetup.LoginRateLimit);
        // Cast ke Delegate: tanpa ini MapPost memilih overload RequestDelegate dan hasil NoContent terbuang (ASP0016).
        auth.MapPost("/logout", (Delegate)LogoutAsync).AllowAnonymous();
        auth.MapGet("/me", MeAsync);
        auth.MapPost("/password", ChangePasswordAsync);
        return auth;
    }

    internal static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem>> LoginAsync(
        LoginRequest request,
        HttpContext http,
        AuthStore store,
        UserSessions sessions,
        IPasswordHasher<AuthAccount> hasher,
        CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (username.Length == 0 || password.Length == 0)
        {
            return Problems.Invalid("username", "Isi username dan password.");
        }

        var account = await store.FindLoginAsync(username, ct);
        if (account is null)
        {
            hasher.VerifyHashedPassword(AuthAccount.Instance, DummyHash.Value, password);
            return Problems.Invalid("username", InvalidLogin);
        }

        if (account.LockedSeconds > 0)
        {
            return Problems.Invalid("username", LockedMessage(account.LockedSeconds));
        }

        var result = hasher.VerifyHashedPassword(AuthAccount.Instance, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            var lockedNow = await store.RecordFailedLoginAsync(account.UserId, ct);
            return Problems.Invalid("username", lockedNow ? LockedMessage(AuthStore.LockMinutes * 60) : InvalidLogin);
        }

        if (!account.IsActive)
        {
            return Problems.Invalid("username", "Akun tidak aktif. Hubungi admin HSE.");
        }

        var newHash = result == PasswordVerificationResult.SuccessRehashNeeded
            ? hasher.HashPassword(AuthAccount.Instance, password)
            : null;
        await store.RecordSuccessfulLoginAsync(account.UserId, newHash, ct);

        sessions.Invalidate(account.UserId);
        var session = await sessions.GetAsync(account.UserId, ct);
        if (session is null)
        {
            return Problems.Invalid("username", InvalidLogin);
        }

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthPrincipal.Create(session, includePermissions: false));
        return TypedResults.Ok(CurrentUserResponse.From(session));
    }

    internal static async Task<NoContent> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    internal static async Task<Results<Ok<CurrentUserResponse>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal user, UserSessions sessions, CancellationToken ct)
    {
        var session = user.GetUserId() is { } userId ? await sessions.GetAsync(userId, ct) : null;
        return session is null ? TypedResults.Unauthorized() : TypedResults.Ok(CurrentUserResponse.From(session));
    }

    internal static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal user,
        HttpContext http,
        AuthStore store,
        UserSessions sessions,
        IPasswordHasher<AuthAccount> hasher,
        CancellationToken ct)
    {
        if (user.GetUserId() is not { } userId || await sessions.GetAsync(userId, ct) is not { } session)
        {
            return TypedResults.Unauthorized();
        }

        var currentHash = await store.GetPasswordHashAsync(userId, ct);
        if (currentHash is null
            || hasher.VerifyHashedPassword(AuthAccount.Instance, currentHash, request.CurrentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return Problems.Invalid("currentPassword", "Password lama salah.");
        }

        if (PasswordRules.Validate(request.NewPassword, session.Username) is { } problem)
        {
            return Problems.Invalid("newPassword", problem);
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return Problems.Invalid("newPassword", "Password baru harus berbeda dari password lama.");
        }

        await store.SetPasswordAsync(userId, hasher.HashPassword(AuthAccount.Instance, request.NewPassword!), mustChangePassword: false, ct);
        sessions.Invalidate(userId);
        session = await sessions.GetAsync(userId, ct);
        if (session is null)
        {
            return TypedResults.Unauthorized();
        }

        // SecurityStamp baru: perangkat lain keluar, perangkat ini mendapat cookie baru.
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthPrincipal.Create(session, includePermissions: false));
        return TypedResults.Ok(CurrentUserResponse.From(session));
    }

    private static string LockedMessage(int seconds) =>
        $"Akun terkunci sementara karena terlalu banyak percobaan. Coba lagi dalam {Math.Max(1, (seconds + 59) / 60)} menit.";
}
