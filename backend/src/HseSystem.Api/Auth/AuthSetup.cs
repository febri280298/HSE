using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace HseSystem.Api.Auth;

public static class AuthSetup
{
    public const string LoginRateLimit = "login";

    public static IServiceCollection AddHseAuth(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IPasswordHasher<AuthAccount>, PasswordHasher<AuthAccount>>();
        services.AddSingleton<AuthStore>();
        services.AddSingleton<UserSessions>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "hse.auth";
                options.Cookie.HttpOnly = true;
                // Strict: cookie tidak ikut terkirim dari situs lain, sekaligus penahan CSRF.
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(10);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
                // API tidak me-redirect ke halaman login; Angular yang menangani 401/403.
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        var authorization = services.AddAuthorizationBuilder();
        foreach (var permission in Permissions.All)
        {
            authorization.AddPolicy(permission.Code, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(Permissions.ClaimType, permission.Code));
        }

        authorization.AddPolicy(Permissions.TemuanAkses, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(Permissions.ClaimType, Permissions.TemuanLihat, Permissions.PerbaikanInput, Permissions.TemuanVerifikasi, Permissions.TemuanTugaskan));
        authorization.AddPolicy(Permissions.KpiAkses, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(Permissions.ClaimType, Permissions.KpiLihat, Permissions.KpiInput, Permissions.KpiKelola));
        authorization.AddPolicy(Permissions.FotoUpload, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(Permissions.ClaimType, Permissions.TemuanInput, Permissions.PerbaikanInput));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Default maksimal 10 percobaan login per menit per alamat IP (Auth:LoginPermitPerMinute).
            options.AddPolicy(LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:LoginPermitPerMinute", 10),
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }

    /// <summary>
    /// Setiap request: pengguna harus masih ada, aktif, dan SecurityStamp-nya sama dengan cookie.
    /// Kalau lolos, hak akses terkini dari database dipasang ke principal.
    /// </summary>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var userId = context.Principal?.GetUserId();
        var stamp = context.Principal?.FindFirst(AuthPrincipal.StampClaim)?.Value;
        var sessions = context.HttpContext.RequestServices.GetRequiredService<UserSessions>();
        var session = userId is null ? null : await sessions.GetAsync(userId.Value, context.HttpContext.RequestAborted);

        if (session is null || !session.IsActive || session.SecurityStamp.ToString("N") != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        context.ReplacePrincipal(AuthPrincipal.Create(session, includePermissions: true));
    }

    /// <summary>
    /// Membuat Administrator pertama dari <c>Bootstrap:AdminUsername</c> + <c>Bootstrap:AdminPassword</c>,
    /// hanya kalau tabel pengguna masih kosong.
    /// </summary>
    public static async Task EnsureBootstrapAdminAsync(this WebApplication app)
    {
        var username = app.Configuration["Bootstrap:AdminUsername"]?.Trim();
        var password = app.Configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return;
        }

        var store = app.Services.GetRequiredService<AuthStore>();
        if (await store.AnyUserAsync(CancellationToken.None))
        {
            app.Logger.LogInformation("Bootstrap admin dilewati karena sudah ada pengguna. Bootstrap:AdminPassword boleh dihapus.");
            return;
        }

        if ((PasswordRules.ValidateUsername(username) ?? PasswordRules.Validate(password, username)) is { } problem)
        {
            throw new InvalidOperationException($"Bootstrap admin ditolak: {problem}");
        }

        var hasher = app.Services.GetRequiredService<IPasswordHasher<AuthAccount>>();
        await store.CreateBootstrapAdminAsync(username, "Administrator", hasher.HashPassword(AuthAccount.Instance, password), CancellationToken.None);
        app.Logger.LogWarning(
            "Administrator '{Username}' dibuat dari Bootstrap. Ganti password saat login pertama, lalu hapus Bootstrap:AdminPassword dari konfigurasi.",
            username);
    }
}
