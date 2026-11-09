using HseSystem.Api.Admin;
using HseSystem.Api.Auth;
using HseSystem.Api.Data;
using HseSystem.Api.Images;
using HseSystem.Api.Kpi;
using HseSystem.Api.Master;
using HseSystem.Api.Patrol;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Connection string tidak disimpan di repo: isi lewat `dotnet user-secrets` (dev) atau environment variable (server).
builder.Services.AddSingleton(sp => new HseDb(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Hse") is { Length: > 0 } connection
        ? connection
        : throw new InvalidOperationException("ConnectionStrings:Hse belum diisi. Lihat README bagian 'Database'.")));
builder.Services.AddSingleton<RoleStore>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddHseAuth();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
    (sp.GetRequiredService<IConfiguration>().GetSection(ImageStorageOptions.Section).Get<ImageStorageOptions>() ?? new())
        .Resolve(sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<ImageStorage>();
builder.Services.AddSingleton<PatrolStore>();
builder.Services.AddSingleton(sp =>
    (sp.GetRequiredService<IConfiguration>().GetSection(DocumentStorageOptions.Section).Get<DocumentStorageOptions>() ?? new())
        .Resolve(sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddSingleton<KpiStore>();

// Batas body multipart sedikit di atas batas file terbesar, supaya file yang kebesaran dijawab dengan pesan yang jelas
// dari endpoint upload, bukan error mentah dari server.
builder.Services.AddOptions<FormOptions>()
    .Configure<ImageStorageOptions, DocumentStorageOptions>((form, images, documents) =>
        form.MultipartBodyLengthLimit = Math.Max(images.MaxBytes, documents.MaxBytes) + (1024 * 1024));

builder.Services.AddProblemDetails();

var app = builder.Build();

var imageOptions = app.Services.GetRequiredService<ImageStorageOptions>();
Directory.CreateDirectory(imageOptions.RootPath);

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();

// Foto temuan hanya untuk pengguna yang sudah login.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments(imageOptions.RequestPath),
    branch => branch.Use(async (ctx, next) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(ctx);
    }));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imageOptions.RootPath),
    RequestPath = imageOptions.RequestPath,
    OnPrepareResponse = ctx =>
    {
        // Nama file foto unik dan tidak pernah ditimpa: aman di-cache lama, tapi hanya di browser pengguna (private).
        ctx.Context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
    },
});

app.UseAuthorization();
app.UseRateLimiter();

// Semua /api wajib login, kecuali yang diberi AllowAnonymous (health, login, logout).
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/health", () => TypedResults.Ok(new { status = "ok" })).AllowAnonymous();
api.MapAuthEndpoints();
api.MapImageEndpoints();
api.MapPlantEndpoints();
api.MapMasterDataEndpoints();
api.MapPatrolEndpoints();
api.MapKpiEndpoints();
api.MapRoleEndpoints();
api.MapUserEndpoints();

// Alamat /api yang tidak dikenal harus 404, bukan dibalas halaman Angular.
// Foto yang tidak ada (/uploads/...jpg) otomatis 404: MapFallbackToFile hanya menangkap alamat tanpa ekstensi file.
// Jangan tambahkan fallback "/uploads/{**rest}": rute itu dicocokkan sebelum UseStaticFiles sehingga foto tidak tersaji.
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.EnsureBootstrapAdminAsync();
await app.RunAsync();

/// <summary>Dibuka untuk WebApplicationFactory di HseSystem.Tests.</summary>
public partial class Program;
