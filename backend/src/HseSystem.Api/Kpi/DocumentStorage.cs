using HseSystem.Api.Common;

namespace HseSystem.Api.Kpi;

/// <summary>Pengaturan penyimpanan dokumen KPI (bagian <c>DocumentStorage</c> di appsettings).</summary>
public sealed class DocumentStorageOptions
{
    public const string Section = "DocumentStorage";

    /// <summary>Folder fisik dokumen; tidak disajikan langsung, hanya lewat endpoint unduh yang memeriksa cakupan plant.</summary>
    public string RootPath { get; set; } = "App_Data/documents";

    public long MaxBytes { get; set; } = 50 * 1024 * 1024;

    public DocumentStorageOptions Resolve(string contentRoot) => new()
    {
        RootPath = Path.GetFullPath(Path.IsPathRooted(RootPath) ? RootPath : Path.Combine(contentRoot, RootPath)),
        MaxBytes = MaxBytes,
    };
}

public sealed record StoredDocument(string Path, string FileName, string Extension, string ContentType, long SizeBytes);

/// <summary>Menyimpan Kalender Safety (PDF) dan PPT P2K3L. Jenis file diperiksa dari isinya, bukan hanya ekstensi.</summary>
public sealed class DocumentStorage(DocumentStorageOptions options)
{
    private const string PdfType = "application/pdf";
    private const string PptxType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string PptType = "application/vnd.ms-powerpoint";

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0];

    public DocumentStorageOptions Options => options;

    public async Task<StoredDocument> SaveAsync(IFormFile file, string kind, int year, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            throw new ValidationFailure("file", "Pilih file terlebih dahulu.");
        }

        if (file.Length > options.MaxBytes)
        {
            throw new ValidationFailure("file", $"Ukuran file maksimal {options.MaxBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var header = new byte[8];
        await using (var peek = file.OpenReadStream())
        {
            await peek.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        }

        var contentType = Detect(kind, extension, header);
        var relative = Path.Combine(kind.ToLowerInvariant(), year.ToString(), Guid.CreateVersion7().ToString("N") + extension);
        var full = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var target = File.Create(full))
        {
            await file.CopyToAsync(target, ct);
        }

        var name = Path.GetFileName(file.FileName.Trim());
        return new StoredDocument(relative.Replace('\\', '/'), name.Length > 200 ? name[^200..] : name, extension, contentType, file.Length);
    }

    public string FullPath(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(options.RootPath, relative));
        return full.StartsWith(options.RootPath, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new InvalidOperationException("Path dokumen di luar folder penyimpanan.");
    }

    public void Delete(string relative)
    {
        var full = FullPath(relative);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    private static string Detect(string kind, string extension, byte[] header)
    {
        if (kind == KpiDocumentKind.Kalender)
        {
            return extension == ".pdf" && header.AsSpan().StartsWith(PdfMagic)
                ? PdfType
                : throw new ValidationFailure("file", "Kalender Safety wajib berupa file PDF.");
        }

        return extension switch
        {
            ".pptx" when header.AsSpan().StartsWith(ZipMagic) => PptxType,
            ".ppt" when header.AsSpan().StartsWith(OleMagic) => PptType,
            _ => throw new ValidationFailure("file", "Laporan PPT wajib berupa file .pptx atau .ppt."),
        };
    }
}
