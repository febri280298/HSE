namespace HseSystem.Api.Images;

/// <summary>Pengaturan penyimpanan foto (bagian <c>ImageStorage</c> di appsettings).</summary>
public sealed class ImageStorageOptions
{
    public const string Section = "ImageStorage";

    /// <summary>
    /// Folder fisik tempat foto disimpan. Path relatif dihitung dari folder aplikasi (ContentRoot).
    /// Di server arahkan ke disk/NAS khusus foto, misalnya <c>D:\HSE-Storage\images</c>.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/images";

    /// <summary>Awalan URL tempat foto disajikan, misalnya <c>/uploads/temuan/2026/11/xxx.jpg</c>.</summary>
    public string RequestPath { get; set; } = "/uploads";

    /// <summary>Ukuran maksimal satu foto dalam byte.</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Salinan pengaturan dengan <see cref="RootPath"/> absolut dan <see cref="RequestPath"/> rapi.</summary>
    public ImageStorageOptions Resolve(string contentRoot) => new()
    {
        RootPath = Path.GetFullPath(Path.IsPathRooted(RootPath) ? RootPath : Path.Combine(contentRoot, RootPath)),
        RequestPath = "/" + RequestPath.Trim('/'),
        MaxBytes = MaxBytes,
    };
}
