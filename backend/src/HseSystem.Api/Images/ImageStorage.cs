using System.Buffers;
using System.Text.RegularExpressions;

namespace HseSystem.Api.Images;

/// <summary>Kesalahan isian foto yang pesannya aman ditampilkan ke pengguna.</summary>
public sealed class ImageUploadException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>
/// Menyimpan foto ke disk dengan susunan <c>{kategori}/{tahun}/{bulan}/{id}.{ext}</c>.
/// Nama file selalu dibuat server (GUID v7), jadi file lama tidak pernah tertimpa dan aman di-cache.
/// </summary>
public sealed partial class ImageStorage(ImageStorageOptions options, TimeProvider time)
{
    public const string DefaultCategory = "umum";

    public ImageStorageOptions Options { get; } = options;

    public string TooLargeMessage => $"Ukuran foto maksimal {Options.MaxBytes / (1024 * 1024)} MB.";

    public async Task<ImageResponse> SaveAsync(
        Stream content, string? originalFileName, string? category, CancellationToken cancellationToken = default)
    {
        var folder = NormalizeCategory(category);

        var header = new byte[ImageSignature.HeaderLength];
        var headerLength = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var format = ImageSignature.Detect(header.AsSpan(0, headerLength))
            ?? throw new ImageUploadException("file", "File bukan foto yang didukung. Gunakan JPG, PNG, atau WebP.");

        var uploadedAt = time.GetUtcNow();
        var localNow = time.GetLocalNow();
        var id = Guid.CreateVersion7(uploadedAt).ToString("N");
        var relativePath = $"{folder}/{localNow:yyyy}/{localNow:MM}/{id}{format.Extension}";
        var fullPath = Path.Combine(Options.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Tulis ke file sementara dulu, supaya foto yang gagal di tengah jalan tidak pernah tampil setengah.
        var tempPath = fullPath + ".part";
        long size;
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
                size = headerLength + await CopyWithLimitAsync(content, file, Options.MaxBytes - headerLength, cancellationToken);
            }

            File.Move(tempPath, fullPath);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }

        return new ImageResponse(
            id,
            relativePath,
            $"{Options.RequestPath}/{relativePath}",
            CleanFileName(originalFileName, format),
            format.ContentType,
            size,
            folder,
            uploadedAt);
    }

    /// <summary>
    /// Path kiriman frontend benar-benar foto hasil upload di kategori itu. Polanya ketat (tanpa <c>..</c>),
    /// supaya data temuan tidak bisa menunjuk file lain di server.
    /// </summary>
    public bool IsStored(string? relativePath, string category) =>
        relativePath is not null
        && StoredPathPattern().IsMatch(relativePath)
        && relativePath.StartsWith(category + "/", StringComparison.Ordinal)
        && File.Exists(Path.Combine(Options.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>URL untuk menampilkan foto dari path yang tersimpan di database.</summary>
    public string UrlOf(string relativePath) => $"{Options.RequestPath}/{relativePath}";

    internal static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return DefaultCategory;
        }

        var value = category.Trim().ToLowerInvariant();
        return CategoryPattern().IsMatch(value)
            ? value
            : throw new ImageUploadException("category", "Kategori foto hanya boleh huruf kecil, angka, dan tanda minus (maks. 32 karakter).");
    }

    /// <summary>Nama asli hanya untuk tampilan: buang folder, karakter kontrol, dan samakan ekstensi dengan isi file.</summary>
    internal static string CleanFileName(string? originalFileName, ImageFormat format)
    {
        var name = (originalFileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var stem = new string([.. Path.GetFileNameWithoutExtension(name).Where(c => !char.IsControl(c))]).Trim();
        if (stem.Length == 0)
        {
            stem = "foto";
        }

        return (stem.Length > 100 ? stem[..100] : stem) + format.Extension;
    }

    private async Task<long> CopyWithLimitAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > limit)
                {
                    throw new ImageUploadException("file", TooLargeMessage);
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,31}/\d{4}/\d{2}/[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex StoredPathPattern();
}
