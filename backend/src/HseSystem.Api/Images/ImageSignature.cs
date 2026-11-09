namespace HseSystem.Api.Images;

/// <summary>Jenis foto yang diterima beserta ekstensi file yang dipakai saat disimpan.</summary>
public sealed record ImageFormat(string ContentType, string Extension);

/// <summary>
/// Mengenali jenis foto dari byte awal isi file. Nama file dan Content-Type dari browser tidak dipercaya,
/// karena keduanya bisa diisi sembarang oleh pengirim.
/// </summary>
public static class ImageSignature
{
    /// <summary>Jumlah byte awal yang perlu dibaca untuk mengenali semua format di bawah.</summary>
    public const int HeaderLength = 12;

    public static readonly ImageFormat Jpeg = new("image/jpeg", ".jpg");
    public static readonly ImageFormat Png = new("image/png", ".png");
    public static readonly ImageFormat Webp = new("image/webp", ".webp");

    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegMagic))
        {
            return Jpeg;
        }

        if (header.StartsWith(PngMagic))
        {
            return Png;
        }

        // WebP: "RIFF" <4 byte ukuran> "WEBP"
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }
}
