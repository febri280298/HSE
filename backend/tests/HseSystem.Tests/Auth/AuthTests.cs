using System.Net.Http.Json;
using HseSystem.Api.Auth;
using HseSystem.Tests.Infrastructure;

namespace HseSystem.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(HseApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tanpa_login_api_dan_foto_dijawab_401()
    {
        api.SkipIfUnavailable();
        using var client = api.CreateClient();

        Assert.Equal(200, await client.GetAsync("/api/health", Ct).StatusAsync());
        Assert.Equal(401, await client.GetAsync("/api/auth/me", Ct).StatusAsync());
        Assert.Equal(401, await client.GetAsync("/api/admin/users", Ct).StatusAsync());
        Assert.Equal(401, await client.GetAsync("/api/master/plants", Ct).StatusAsync());
        Assert.Equal(401, await client.PostAsync("/api/images", new MultipartFormDataContent(), Ct).StatusAsync());
        Assert.Equal(401, await client.GetAsync("/uploads/temuan/2026/11/apa-saja.jpg", Ct).StatusAsync());
    }

    [Fact]
    public async Task Password_salah_dan_username_tidak_ada_dijawab_sama()
    {
        api.SkipIfUnavailable();
        using var client = api.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "salah123" }, Ct).ErrorAsync();
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new { username = "tidak.ada", password = "salah123" }, Ct).ErrorAsync();

        Assert.Equal("Username atau password salah.", wrongPassword);
        Assert.Equal(wrongPassword, unknownUser);
    }

    [Fact]
    public async Task Lima_kali_salah_password_akun_terkunci()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, []);
        using var client = api.CreateClient();

        string message = string.Empty;
        for (var i = 0; i < AuthStore.MaxFailedLogins; i++)
        {
            message = await client.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = "salah1234" }, Ct).ErrorAsync();
        }

        Assert.Contains("terkunci", message);
        // Password benar pun ditolak selama terkunci.
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = HseApiFixture.UserPassword }, Ct).ErrorAsync();
        Assert.Contains("terkunci", locked);

        // Reset password oleh admin membuka kunci.
        await admin.PostAsJsonAsync($"/api/admin/users/{user.UserId}/reset-password", new { newPassword = "Baru2026abc" }, Ct).OkJsonAsync();
        await client.LoginAsync(user.Username, "Baru2026abc");
    }

    [Fact]
    public async Task Pengguna_baru_wajib_ganti_password_sebelum_dapat_hak_akses()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var role = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Patroller " + Guid.NewGuid().ToString("N")[..6], permissions = new[] { Permissions.TemuanInput } }, Ct).OkJsonAsync();
        var username = "baru" + Guid.NewGuid().ToString("N")[..6];
        await admin.PostAsJsonAsync("/api/admin/users", new
        {
            username,
            displayName = "Pengguna Baru",
            isActive = true,
            password = HseApiFixture.TempPassword,
            roleIds = new[] { role.GetProperty("roleId").GetInt32() },
        }, Ct).OkJsonAsync();

        using var client = api.CreateClient();
        var me = await client.LoginAsync(username, HseApiFixture.TempPassword);
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(0, me.GetProperty("permissions").GetArrayLength());
        Assert.Equal(403, await client.PostAsync("/api/images", FakeJpegForm(), Ct).StatusAsync());

        Assert.Equal("Password lama salah.", await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "keliru123", newPassword = "Baru2026abc" }, Ct).ErrorAsync());
        Assert.Contains("huruf dan angka", await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = HseApiFixture.TempPassword, newPassword = "hanyahuruf" }, Ct).ErrorAsync());

        var changed = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = HseApiFixture.TempPassword, newPassword = "Baru2026abc" }, Ct).OkJsonAsync();
        Assert.False(changed.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(Permissions.TemuanInput, changed.GetProperty("permissions")[0].GetString());
        Assert.Equal(200, await client.PostAsync("/api/images", FakeJpegForm(), Ct).StatusAsync());
    }

    [Fact]
    public async Task Me_berisi_peran_hak_akses_dan_plant()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var plants = await admin.GetAsync("/api/master/plants", Ct).OkJsonAsync();
        var bti = plants.EnumerateArray().Single(p => p.GetProperty("code").GetString() == "BTI").GetProperty("plantId").GetInt32();

        var user = await api.CreateUserAsync(admin, [Permissions.TemuanLihat], [bti]);
        var me = await user.Client.GetAsync("/api/auth/me", Ct).OkJsonAsync();

        Assert.Equal(user.Username, me.GetProperty("username").GetString());
        Assert.False(me.GetProperty("isAdministrator").GetBoolean());
        Assert.Equal([Permissions.TemuanLihat], me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("BTI", me.GetProperty("plants")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_mengakhiri_sesi()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();

        Assert.Equal(204, await admin.PostAsync("/api/auth/logout", null, Ct).StatusAsync());
        Assert.Equal(401, await admin.GetAsync("/api/auth/me", Ct).StatusAsync());
    }

    [Fact]
    public async Task Akun_dinonaktifkan_langsung_tidak_bisa_dipakai()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, [Permissions.TemuanLihat]);
        Assert.Equal(200, await user.Client.GetAsync("/api/auth/me", Ct).StatusAsync());

        await admin.PutAsJsonAsync($"/api/admin/users/{user.UserId}", new
        {
            displayName = "Sudah keluar",
            isActive = false,
            roleIds = new[] { user.RoleId },
        }, Ct).OkJsonAsync();

        Assert.Equal(401, await user.Client.GetAsync("/api/auth/me", Ct).StatusAsync());
        using var again = api.CreateClient();
        Assert.Contains("tidak aktif", await again.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = HseApiFixture.UserPassword }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Reset_password_oleh_admin_mengeluarkan_sesi_lama()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, [Permissions.TemuanLihat]);

        await admin.PostAsJsonAsync($"/api/admin/users/{user.UserId}/reset-password", new { newPassword = "Reset2026abc" }, Ct).OkJsonAsync();

        Assert.Equal(401, await user.Client.GetAsync("/api/auth/me", Ct).StatusAsync());
        using var client = api.CreateClient();
        var me = await client.LoginAsync(user.Username, "Reset2026abc");
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
    }

    internal static MultipartFormDataContent FakeJpegForm(int size = 600, string category = "temuan")
    {
        var bytes = new byte[size];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("image/jpeg");
        return new MultipartFormDataContent { { file, "file", "uji.jpg" }, { new StringContent(category), "category" } };
    }
}
