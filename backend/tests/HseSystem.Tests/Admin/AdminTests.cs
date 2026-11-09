using System.Net.Http.Json;
using HseSystem.Api.Auth;
using HseSystem.Tests.Auth;
using HseSystem.Tests.Infrastructure;

namespace HseSystem.Tests.Admin;

[Collection(ApiCollection.Name)]
public sealed class AdminTests(HseApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hak_akses_peran_berubah_seketika_tanpa_login_ulang()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, [Permissions.TemuanLihat]);
        Assert.Equal(403, await user.Client.PostAsync("/api/images", AuthTests.FakeJpegForm(), Ct).StatusAsync());

        await admin.PutAsJsonAsync($"/api/admin/roles/{user.RoleId}", new
        {
            name = "Patroller " + user.Username,
            description = "Boleh input temuan",
            permissions = new[] { Permissions.TemuanLihat, Permissions.TemuanInput },
        }, Ct).OkJsonAsync();

        Assert.Equal(200, await user.Client.PostAsync("/api/images", AuthTests.FakeJpegForm(), Ct).StatusAsync());
    }

    [Fact]
    public async Task Tanpa_hak_kelola_halaman_admin_dijawab_403()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, [Permissions.TemuanInput]);

        Assert.Equal(403, await user.Client.GetAsync("/api/admin/users", Ct).StatusAsync());
        Assert.Equal(403, await user.Client.GetAsync("/api/admin/roles", Ct).StatusAsync());
        Assert.Equal(403, await user.Client.PostAsJsonAsync("/api/admin/roles", new { name = "Coba", permissions = Array.Empty<string>() }, Ct).StatusAsync());
    }

    [Fact]
    public async Task Peran_Administrator_tidak_bisa_diubah_atau_dihapus()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var roles = await admin.GetAsync("/api/admin/roles", Ct).OkJsonAsync();
        var administrator = roles.EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean());

        // Administrator selalu memegang semua hak, termasuk hak yang ditambahkan kemudian.
        Assert.Equal(Permissions.All.Count, administrator.GetProperty("permissions").GetArrayLength());

        var id = administrator.GetProperty("roleId").GetInt32();
        Assert.Contains("tidak bisa diubah", await admin.PutAsJsonAsync($"/api/admin/roles/{id}", new { name = "Admin", permissions = Array.Empty<string>() }, Ct).ErrorAsync());
        Assert.Contains("tidak bisa dihapus", await admin.DeleteAsync($"/api/admin/roles/{id}", Ct).ErrorAsync());
    }

    [Fact]
    public async Task Peran_yang_masih_dipakai_tidak_bisa_dihapus()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, []);

        Assert.Contains("masih dipakai 1 pengguna", await admin.DeleteAsync($"/api/admin/roles/{user.RoleId}", Ct).ErrorAsync());

        var unused = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Sementara " + user.Username, permissions = Array.Empty<string>() }, Ct).OkJsonAsync();
        Assert.Equal(204, await admin.DeleteAsync($"/api/admin/roles/{unused.GetProperty("roleId").GetInt32()}", Ct).StatusAsync());
    }

    [Fact]
    public async Task Nama_peran_duplikat_dan_hak_akses_asing_ditolak()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();

        Assert.Equal("Nama peran sudah dipakai.", await admin.PostAsJsonAsync("/api/admin/roles", new { name = "administrator", permissions = Array.Empty<string>() }, Ct).ErrorAsync());
        Assert.Contains("tidak dikenal", await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Aneh", permissions = new[] { "hapus.semua" } }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Username_duplikat_ditolak_tanpa_peduli_huruf_besar()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, []);

        var error = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            username = user.Username.ToUpperInvariant(),
            displayName = "Kembar",
            isActive = true,
            password = HseApiFixture.TempPassword,
            roleIds = new[] { user.RoleId },
        }, Ct).ErrorAsync();

        Assert.Equal("Username sudah dipakai.", error);
    }

    [Fact]
    public async Task Admin_tidak_bisa_menonaktifkan_diri_atau_melepas_Administrator_terakhir()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var me = await admin.GetAsync("/api/auth/me", Ct).OkJsonAsync();
        var adminId = me.GetProperty("userId").GetInt32();
        var roles = await admin.GetAsync("/api/admin/roles", Ct).OkJsonAsync();
        var administratorRole = roles.EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("roleId").GetInt32();
        var otherRole = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Lain " + Guid.NewGuid().ToString("N")[..6], permissions = Array.Empty<string>() }, Ct).OkJsonAsync();

        Assert.Contains("akun sendiri", await admin.PutAsJsonAsync($"/api/admin/users/{adminId}", new
        {
            displayName = "Administrator", isActive = false, roleIds = new[] { administratorRole },
        }, Ct).ErrorAsync());

        Assert.Contains("Minimal harus ada satu Administrator", await admin.PutAsJsonAsync($"/api/admin/users/{adminId}", new
        {
            displayName = "Administrator", isActive = true, roleIds = new[] { otherRole.GetProperty("roleId").GetInt32() },
        }, Ct).ErrorAsync());

        // Tidak ada yang berubah: admin masih Administrator.
        Assert.True((await admin.GetAsync("/api/auth/me", Ct).OkJsonAsync()).GetProperty("isAdministrator").GetBoolean());
    }

    [Fact]
    public async Task Form_pengguna_mendapat_pilihan_peran_dan_empat_plant()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();

        var options = await admin.GetAsync("/api/admin/users/options", Ct).OkJsonAsync();
        var permissions = await admin.GetAsync("/api/admin/roles/permissions", Ct).OkJsonAsync();

        Assert.Equal(["KIMU", "TEGAL", "BTI", "BTP"], options.GetProperty("plants").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.Contains(options.GetProperty("roles").EnumerateArray(), r => r.GetProperty("name").GetString() == "Administrator");
        Assert.Equal(Permissions.All.Count, permissions.GetArrayLength());
    }
}
