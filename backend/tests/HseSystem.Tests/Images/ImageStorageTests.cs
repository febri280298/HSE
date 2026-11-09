using HseSystem.Api.Images;

namespace HseSystem.Tests.Images;

public sealed class ImageStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hse-img-test-" + Guid.NewGuid().ToString("N"));
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero));

    private ImageStorage CreateStorage(long maxBytes = 4096) =>
        new(new ImageStorageOptions { RootPath = _root, RequestPath = "/uploads", MaxBytes = maxBytes }, _time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Foto_disimpan_per_kategori_tahun_bulan()
    {
        var result = await CreateStorage().SaveAsync(FakeJpeg(500), @"C:\fakepath\Temuan Lemari.JPG", "temuan", Ct);

        Assert.Equal("temuan", result.Category);
        Assert.Matches("^temuan/2026/11/[0-9a-f]{32}\\.jpg$", result.Path);
        Assert.Equal("/uploads/" + result.Path, result.Url);
        Assert.Equal("Temuan Lemari.jpg", result.FileName);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(500, result.Size);
        Assert.Equal(_time.GetUtcNow(), result.UploadedAt);
        Assert.Equal(500, new FileInfo(Path.Combine(_root, result.Path)).Length);
    }

    [Fact]
    public async Task Setiap_foto_dapat_nama_unik()
    {
        var storage = CreateStorage();
        var a = await storage.SaveAsync(FakeJpeg(100), "sama.jpg", "temuan", Ct);
        var b = await storage.SaveAsync(FakeJpeg(100), "sama.jpg", "temuan", Ct);

        Assert.NotEqual(a.Path, b.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Tanpa_kategori_masuk_folder_umum(string? category)
    {
        var result = await CreateStorage().SaveAsync(FakeJpeg(100), "a.jpg", category, Ct);

        Assert.StartsWith("umum/", result.Path);
    }

    [Theory]
    [InlineData("../luar")]
    [InlineData("temuan/../../x")]
    [InlineData("C:")]
    [InlineData("-awal-minus")]
    [InlineData("kategori-yang-namanya-terlalu-panjang-sekali")]
    public async Task Kategori_tidak_aman_ditolak(string category)
    {
        var ex = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateStorage().SaveAsync(FakeJpeg(100), "a.jpg", category, Ct));

        Assert.Equal("category", ex.Field);
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root).Any());
    }

    [Fact]
    public async Task File_bukan_foto_ditolak_walau_namanya_jpg()
    {
        var ex = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateStorage().SaveAsync(new MemoryStream("%PDF-1.7 bukan foto"u8.ToArray()), "foto.jpg", "temuan", Ct));

        Assert.Equal("file", ex.Field);
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Foto_melebihi_batas_ditolak_tanpa_sisa_file()
    {
        var ex = await Assert.ThrowsAsync<ImageUploadException>(
            () => CreateStorage(maxBytes: 1000).SaveAsync(FakeJpeg(1001), "besar.jpg", "temuan", Ct));

        Assert.Equal("file", ex.Field);
        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Foto_tepat_di_batas_diterima()
    {
        var result = await CreateStorage(maxBytes: 1000).SaveAsync(FakeJpeg(1000), "pas.jpg", "temuan", Ct);

        Assert.Equal(1000, result.Size);
    }

    [Theory]
    [InlineData("foto.png", "foto.jpg")] // ekstensi mengikuti isi file, bukan nama
    [InlineData("", "foto.jpg")]
    [InlineData(null, "foto.jpg")]
    [InlineData("/var/tmp/a\u0001b.jpeg", "ab.jpg")]
    public void Nama_file_dibersihkan(string? original, string expected) =>
        Assert.Equal(expected, ImageStorage.CleanFileName(original, ImageSignature.Jpeg));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    internal static MemoryStream FakeJpeg(int size)
    {
        var bytes = new byte[size];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return new MemoryStream(bytes);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
