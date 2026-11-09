using System.Text.RegularExpressions;
using HseSystem.Api.Auth;

namespace HseSystem.Tests.Auth;

public class PasswordRulesTests
{
    [Theory]
    [InlineData("Rahasia2026", null)]
    [InlineData("pendek1", "minimal")]
    [InlineData("hanyahurufsaja", "huruf dan angka")]
    [InlineData("1234567890", "huruf dan angka")]
    [InlineData("budi2026xyz", "username")]
    [InlineData("", "minimal")]
    [InlineData(null, "minimal")]
    public void Aturan_password(string? password, string? expectedProblem)
    {
        var problem = PasswordRules.Validate(password, "budi");
        if (expectedProblem is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.Contains(expectedProblem, problem);
        }
    }

    [Theory]
    [InlineData("budi.santoso", true)]
    [InlineData("hse_admin-1", true)]
    [InlineData("ab", false)]
    [InlineData("budi santoso", false)]
    [InlineData("budi@pt", false)]
    [InlineData(null, false)]
    public void Aturan_username(string? username, bool valid) =>
        Assert.Equal(valid, PasswordRules.ValidateUsername(username) is null);

    [Fact]
    public void Kode_hak_akses_unik_dan_rapi()
    {
        var codes = Permissions.All.Select(p => p.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
        // Kolom auth.RolePermissions.Permission varchar(64).
        Assert.All(codes, code => Assert.Matches(new Regex("^[a-z][a-z0-9.-]{2,63}$"), code));
    }
}
