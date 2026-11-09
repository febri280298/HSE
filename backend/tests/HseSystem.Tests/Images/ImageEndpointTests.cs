using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HseSystem.Api.Images;
using HseSystem.Tests.Infrastructure;

namespace HseSystem.Tests.Images;

/// <summary>Uji jalur lengkap: upload multipart ke /api/images lalu foto bisa dibuka dari URL balasan.</summary>
[Collection(ApiCollection.Name)]
public sealed class ImageEndpointTests(HseApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_foto_lalu_bisa_dibuka_dari_url()
    {
        api.SkipIfUnavailable();
        using var client = await api.AdminAsync();
        var bytes = ImageStorageTests.FakeJpeg(700).ToArray();

        using var response = await client.PostAsync("/api/images", Form(bytes, "temuan.jpg", "temuan"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var image = await response.Content.ReadFromJsonAsync<ImageResponse>(JsonSerializerOptions.Web, Ct);
        Assert.NotNull(image);
        Assert.StartsWith("/uploads/temuan/", image.Url);
        Assert.Equal(700, image.Size);

        using var download = await client.GetAsync(image.Url, Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("private", download.Headers.CacheControl?.ToString());

        // Tanpa login foto tidak bisa dibuka.
        using var anonymous = api.CreateClient();
        Assert.Equal(401, await anonymous.GetAsync(image.Url, Ct).StatusAsync());
    }

    [Fact]
    public async Task File_bukan_foto_dijawab_400_dengan_pesan()
    {
        api.SkipIfUnavailable();
        using var client = await api.AdminAsync();

        var error = await client.PostAsync("/api/images", Form("bukan foto"u8.ToArray(), "x.jpg", "temuan"), Ct).ErrorAsync();

        Assert.Contains("JPG", error);
    }

    [Fact]
    public async Task Foto_kebesaran_dijawab_400_dengan_pesan()
    {
        api.SkipIfUnavailable();
        using var client = await api.AdminAsync();

        var error = await client.PostAsync("/api/images", Form(ImageStorageTests.FakeJpeg(4096).ToArray(), "b.jpg", "temuan"), Ct).ErrorAsync();

        Assert.Contains("maksimal", error);
    }

    [Fact]
    public async Task Foto_yang_tidak_ada_dijawab_404_bukan_halaman_angular()
    {
        api.SkipIfUnavailable();
        using var client = await api.AdminAsync();

        Assert.Equal(404, await client.GetAsync("/uploads/temuan/2026/11/tidak-ada.jpg", Ct).StatusAsync());
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string category)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(category), "category" },
        };
    }
}
