using System.Text.RegularExpressions;

namespace HseSystem.Api.Auth;

public static partial class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    /// <summary>Pesan kesalahan, atau <c>null</c> kalau password boleh dipakai.</summary>
    public static string? Validate(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Password minimal {MinLength} karakter.";
        }

        if (password.Length > MaxLength)
        {
            return $"Password maksimal {MaxLength} karakter.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "Password harus berisi huruf dan angka.";
        }

        if (!string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            return "Password tidak boleh memuat username.";
        }

        return null;
    }

    /// <summary>Pesan kesalahan, atau <c>null</c> kalau username boleh dipakai.</summary>
    public static string? ValidateUsername(string? username) =>
        username is not null && UsernamePattern().IsMatch(username)
            ? null
            : "Username 3-50 karakter: huruf, angka, titik, minus, atau garis bawah.";

    [GeneratedRegex("^[A-Za-z0-9._-]{3,50}$")]
    private static partial Regex UsernamePattern();
}
