using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace HseSystem.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<HseApiFixture>
{
    public const string Name = "api";
}

/// <summary>
/// Database uji sementara (default: SQL Server LocalDB) berisi skrip db/002 dst., plus aplikasi lengkap.
/// Database dihapus setelah semua tes selesai. Server lain: atur environment variable HSE_TEST_SQL.
/// Kalau SQL Server tidak tersedia, tes API dilewati (skip), bukan gagal.
/// </summary>
public sealed class HseApiFixture : IAsyncLifetime
{
    // Nilai khusus tes (database sementara), bukan password sungguhan.
    public const string AdminUsername = "admin";
    public const string AdminPassword = "KelolaUji2026";
    private const string BootstrapPassword = "Bootstrap2026";
    public const string TempPassword = "Sementara1";
    public const string UserPassword = "Rahasia2026x";

    private readonly string _server = Environment.GetEnvironmentVariable("HSE_TEST_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
    private readonly string _database = "hse_test_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _imageRoot = Path.Combine(Path.GetTempPath(), "hse-img-api-" + Guid.NewGuid().ToString("N"));
    private readonly string _documentRoot = Path.Combine(Path.GetTempPath(), "hse-doc-api-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    public string? SkipReason { get; private set; }

    public string ImageRoot => _imageRoot;

    private string ConnectionString(string database) =>
        new SqlConnectionStringBuilder(_server) { InitialCatalog = database }.ConnectionString;

    public void SkipIfUnavailable() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException(SkipReason)).CreateClient();

    public async Task<HttpClient> AdminAsync()
    {
        var client = CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        return client;
    }

    /// <summary>Peran baru berisi <paramref name="permissions"/> + pengguna baru yang sudah mengganti password awal.</summary>
    public async Task<TestUser> CreateUserAsync(HttpClient admin, string[] permissions, int[]? plantIds = null, int? deptId = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Peran " + suffix, permissions }).OkJsonAsync();
        var roleId = role.GetProperty("roleId").GetInt32();

        var username = "u" + suffix;
        var user = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            username,
            displayName = "Pengguna " + suffix,
            isActive = true,
            password = TempPassword,
            roleIds = new[] { roleId },
            plantIds = plantIds ?? [],
            deptId,
        }).OkJsonAsync();

        var client = CreateClient();
        await client.LoginAsync(username, TempPassword);
        await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = TempPassword, newPassword = UserPassword }).OkJsonAsync();
        return new TestUser(client, user.GetProperty("userId").GetInt32(), roleId, username);
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var master = new SqlConnection(ConnectionString("master"));
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{_database}] COLLATE SQL_Latin1_General_CP1_CI_AS;", master);
            await create.ExecuteNonQueryAsync();
        }
        catch (SqlException ex)
        {
            SkipReason = $"SQL Server uji tidak tersedia ({ex.Message}). Atur HSE_TEST_SQL untuk menjalankan tes API.";
            return;
        }

        await using (var conn = new SqlConnection(ConnectionString(_database)))
        {
            await conn.OpenAsync();
            foreach (var script in DbScripts())
            {
                await using var cmd = new SqlCommand(await File.ReadAllTextAsync(script), conn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync();
            }
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("ConnectionStrings:Hse", ConnectionString(_database));
            web.UseSetting("Bootstrap:AdminUsername", AdminUsername);
            web.UseSetting("Bootstrap:AdminPassword", BootstrapPassword);
            web.UseSetting("Auth:LoginPermitPerMinute", "100000");
            web.UseSetting("ImageStorage:RootPath", _imageRoot);
            web.UseSetting("ImageStorage:MaxBytes", "2048");
            web.UseSetting("DocumentStorage:RootPath", _documentRoot);
            web.UseSetting("DocumentStorage:MaxBytes", "4096");
        });

        // Administrator dari Bootstrap wajib mengganti password pada login pertama.
        using var client = CreateClient();
        var me = await client.LoginAsync(AdminUsername, BootstrapPassword);
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = BootstrapPassword, newPassword = AdminPassword }).OkJsonAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        if (SkipReason is null)
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(ConnectionString("master"));
            await master.OpenAsync();
            await using var drop = new SqlCommand(
                $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];", master);
            await drop.ExecuteNonQueryAsync();
        }

        foreach (var root in new[] { _imageRoot, _documentRoot })
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Skrip db/002 dst. (001 membuat database + login di server sungguhan, tidak dipakai di tes).</summary>
    private static IEnumerable<string> DbScripts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "db", "002_auth_dan_master.sql")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("Folder db/ proyek tidak ditemukan.");
        }

        return Directory.GetFiles(Path.Combine(dir.FullName, "db"), "*.sql")
            .Where(f => !Path.GetFileName(f).StartsWith("001_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }
}

public sealed record TestUser(HttpClient Client, int UserId, int RoleId, string Username);

public static class ApiClientExtensions
{
    public static async Task<JsonElement> LoginAsync(this HttpClient client, string username, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { username, password }).OkJsonAsync();

    /// <summary>Pastikan 2xx lalu kembalikan body JSON (kosong untuk 204).</summary>
    public static async Task<JsonElement> OkJsonAsync(this Task<HttpResponseMessage> pending)
    {
        using var response = await pending;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Pastikan 400 lalu kembalikan pesan kesalahan pertama.</summary>
    public static async Task<string> ErrorAsync(this Task<HttpResponseMessage> pending)
    {
        using var response = await pending;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 400, $"Diharapkan 400, dapat {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateObject().First().Value[0].GetString()!;
    }

    public static async Task<int> StatusAsync(this Task<HttpResponseMessage> pending)
    {
        using var response = await pending;
        return (int)response.StatusCode;
    }
}
