using HseSystem.Api.Images;

namespace HseSystem.Tests.Images;

public class ImageSignatureTests
{
    [Fact]
    public void Jpeg_dikenali() =>
        Assert.Equal(ImageSignature.Jpeg, ImageSignature.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]));

    [Fact]
    public void Png_dikenali() =>
        Assert.Equal(ImageSignature.Png, ImageSignature.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]));

    [Fact]
    public void Webp_dikenali() =>
        Assert.Equal(ImageSignature.Webp, ImageSignature.Detect("RIFF\x10\x00\x00\x00WEBPVP8 "u8));

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })] // PDF
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })] // EXE
    [InlineData(new byte[] { 0xFF, 0xD8 })] // JPEG terpotong
    [InlineData(new byte[] { })]
    public void Selain_jpeg_png_webp_ditolak(byte[] header) => Assert.Null(ImageSignature.Detect(header));

    [Fact]
    public void Riff_selain_webp_ditolak() => Assert.Null(ImageSignature.Detect("RIFF\x10\x00\x00\x00WAVEfmt "u8));
}
