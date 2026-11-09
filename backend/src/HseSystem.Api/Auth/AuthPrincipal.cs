using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HseSystem.Api.Auth;

public static class AuthPrincipal
{
    public const string StampClaim = "hse:stamp";

    /// <summary>
    /// Isi cookie hanya id, username, dan SecurityStamp. Hak akses ditambahkan per request dari database
    /// (lihat <see cref="AuthSetup"/>), jadi perubahan peran berlaku tanpa login ulang.
    /// </summary>
    public static ClaimsPrincipal Create(UserSession session, bool includePermissions)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, session.UserId.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, session.Username),
            new(StampClaim, session.SecurityStamp.ToString("N")),
        ];

        if (includePermissions)
        {
            claims.AddRange(session.EffectivePermissions.Select(p => new Claim(Permissions.ClaimType, p)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static int? GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
