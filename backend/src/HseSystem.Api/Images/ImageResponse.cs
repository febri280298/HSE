namespace HseSystem.Api.Images;

/// <summary>
/// Balasan setelah foto tersimpan.
/// <see cref="Path"/> yang disimpan ke database (tidak tergantung alamat server);
/// <see cref="Url"/> dipakai frontend untuk menampilkan foto.
/// </summary>
public sealed record ImageResponse(
    string Id,
    string Path,
    string Url,
    string FileName,
    string ContentType,
    long Size,
    string Category,
    DateTimeOffset UploadedAt);
