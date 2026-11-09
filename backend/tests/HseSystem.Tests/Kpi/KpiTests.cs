using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HseSystem.Api.Auth;
using HseSystem.Tests.Auth;
using HseSystem.Tests.Infrastructure;

namespace HseSystem.Tests.Kpi;

[Collection(ApiCollection.Name)]
public sealed class KpiTests(HseApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int Year => DateTime.Now.Year;

    private static int ThisMonth => DateTime.Now.Month;

    private sealed record Team(HttpClient Admin, TestUser DeptUser, TestUser Other, TestUser Hse, int PlantId, int DeptId, int OtherDeptId, int OtherPlantId);

    private async Task<int> PlantAsync(HttpClient admin, string code)
    {
        var plants = await admin.GetAsync("/api/master/plants", Ct).OkJsonAsync();
        return plants.EnumerateArray().Single(p => p.GetProperty("code").GetString() == code).GetProperty("plantId").GetInt32();
    }

    private async Task<int> DeptAsync(HttpClient admin, string code)
    {
        var departments = await admin.GetAsync("/api/master/departments", Ct).OkJsonAsync();
        return departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == code).GetProperty("deptId").GetInt32();
    }

    private async Task<Team> TeamAsync()
    {
        var admin = await api.AdminAsync();
        var plantId = await PlantAsync(admin, "BTI");
        var deptId = await DeptAsync(admin, "PRD");
        var otherDeptId = await DeptAsync(admin, "MKT");
        var deptUser = await api.CreateUserAsync(admin, [Permissions.KpiInput], [plantId], deptId);
        var other = await api.CreateUserAsync(admin, [Permissions.KpiLihat], [plantId], otherDeptId);
        var hse = await api.CreateUserAsync(admin, [Permissions.KpiKelola], []);
        return new Team(admin, deptUser, other, hse, plantId, deptId, otherDeptId, await PlantAsync(admin, "KIMU"));
    }

    private static object Input(Team t, string field, string? value, int? deptId = null, int? month = null, int? plantId = null) =>
        new { plantId = plantId ?? t.PlantId, deptId = deptId ?? t.DeptId, year = Year, month = month ?? ThisMonth, field, value };

    private static Task<JsonElement> SummaryAsync(HttpClient client, Team t, int? deptId) =>
        client.GetAsync($"/api/kpi/summary?year={Year}&plantId={t.PlantId}{(deptId is { } id ? $"&deptId={id}" : string.Empty)}", Ct).OkJsonAsync();

    private static JsonElement Row(JsonElement summary, string key) =>
        summary.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static MultipartFormDataContent Upload(Team t, string kind, byte[] content, string fileName, int? deptId = null, int? month = null)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(kind), "kind" },
            { new StringContent(t.PlantId.ToString()), "plantId" },
            { new StringContent((deptId ?? t.DeptId).ToString()), "deptId" },
            { new StringContent(Year.ToString()), "year" },
            { new StringContent((month ?? ThisMonth).ToString()), "month" },
        };
    }

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 contoh");

    [Fact]
    public async Task Tanpa_hak_KPI_ditolak()
    {
        api.SkipIfUnavailable();
        using var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, [Permissions.TemuanLihat]);
        Assert.Equal(403, await user.Client.GetAsync("/api/kpi/options", Ct).StatusAsync());
    }

    [Fact]
    public async Task Departemen_mengisi_sendiri_dan_hasilnya_terhitung()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();

        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "disease", "2"), Ct).OkJsonAsync();
        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "diseaseTypes", "Demam & Sinus"), Ct).OkJsonAsync();
        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "0"), Ct).OkJsonAsync();

        var summary = await SummaryAsync(t.DeptUser.Client, t, t.DeptId);
        var i = ThisMonth - 1;
        Assert.True(summary.GetProperty("canEdit").GetBoolean());
        Assert.Equal(2m, Row(summary, "disease.act").GetProperty("values")[i].GetDecimal());
        Assert.Equal("NG", Row(summary, "disease.eval").GetProperty("texts")[i].GetString());
        Assert.Equal("Demam & Sinus", Row(summary, "disease.types").GetProperty("texts")[i].GetString());
        Assert.Equal(0m, Row(summary, "accident.act").GetProperty("values")[i].GetDecimal());

        var all = await SummaryAsync(t.DeptUser.Client, t, null);
        Assert.False(all.GetProperty("canEdit").GetBoolean());
        Assert.Contains(all.GetProperty("rows").EnumerateArray(), r => r.GetProperty("key").GetString() == "disease.avg");

        // Mengosongkan isian kembali ke belum diisi.
        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "disease", ""), Ct).OkJsonAsync();
        Assert.Equal(JsonValueKind.Null, Row(await SummaryAsync(t.DeptUser.Client, t, t.DeptId), "disease.act").GetProperty("values")[i].ValueKind);
    }

    [Fact]
    public async Task Tidak_bisa_mengisi_departemen_atau_plant_lain()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();

        Assert.Contains("departemen sendiri", await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "1", deptId: t.OtherDeptId), Ct).ErrorAsync());
        Assert.Contains("departemen sendiri", await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "1", plantId: t.OtherPlantId), Ct).ErrorAsync());
        Assert.Contains("departemen sendiri", await t.Other.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "1", deptId: t.OtherDeptId), Ct).ErrorAsync());
        Assert.Equal(404, await t.DeptUser.Client.GetAsync($"/api/kpi/summary?year={Year}&plantId={t.OtherPlantId}", Ct).StatusAsync());
    }

    [Fact]
    public async Task Tim_HSE_bisa_mengisi_semua_departemen_dan_plant()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();

        await t.Hse.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "0", deptId: t.OtherDeptId), Ct).OkJsonAsync();
        var options = await t.Hse.Client.GetAsync("/api/kpi/options", Ct).OkJsonAsync();
        Assert.True(options.GetProperty("canManage").GetBoolean());
        Assert.Equal(4, options.GetProperty("plants").GetArrayLength());
    }

    [Fact]
    public async Task Isian_tidak_valid_ditolak()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();

        Assert.Contains("angka", await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "abc"), Ct).ErrorAsync());
        Assert.Contains("angka", await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "-1"), Ct).ErrorAsync());
        Assert.Contains("tidak dikenal", await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "lainnya", "1"), Ct).ErrorAsync());
        Assert.Contains("belum berjalan", await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", new { plantId = t.PlantId, deptId = t.DeptId, year = Year + 1, month = 1, field = "accident", value = "0" }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Departemen_di_luar_daftar_plant_ditolak_sampai_didaftarkan()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();
        var kimu = t.OtherPlantId;
        var hrga = await DeptAsync(t.Admin, "HRGA");

        Assert.Contains("tidak terdaftar", await t.Hse.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "0", deptId: hrga, plantId: kimu), Ct).ErrorAsync());

        var current = await t.Admin.GetAsync($"/api/kpi/peserta?plantId={kimu}", Ct).OkJsonAsync();
        int[] original = [.. current.EnumerateArray().Select(d => d.GetInt32())];
        await t.Admin.PutAsJsonAsync("/api/kpi/peserta", new { plantId = kimu, deptIds = original.Append(hrga).ToArray() }, Ct).OkJsonAsync();
        await t.Hse.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "0", deptId: hrga, plantId: kimu), Ct).OkJsonAsync();
        await t.Admin.PutAsJsonAsync("/api/kpi/peserta", new { plantId = kimu, deptIds = original }, Ct).OkJsonAsync();

        Assert.Equal(403, await t.Hse.Client.GetAsync($"/api/kpi/peserta?plantId={kimu}", Ct).StatusAsync());
    }

    [Fact]
    public async Task Temuan_dihitung_otomatis_dari_safety_patrol()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();
        var patroller = await api.CreateUserAsync(t.Admin, [Permissions.TemuanLihat, Permissions.TemuanInput], [t.PlantId]);
        var assigner = await api.CreateUserAsync(t.Admin, [Permissions.TemuanLihat, Permissions.TemuanTugaskan], [t.PlantId]);
        var repairer = await api.CreateUserAsync(t.Admin, [Permissions.PerbaikanInput], [t.PlantId], t.DeptId);
        var area = await t.Admin.PostAsJsonAsync("/api/master/areas", new { plantId = t.PlantId, name = "KPI " + Guid.NewGuid().ToString("N")[..8], isActive = true }, Ct).OkJsonAsync();
        var image = await patroller.Client.PostAsync("/api/images", AuthTests.FakeJpegForm(category: "temuan"), Ct).OkJsonAsync();

        var before = Row(await SummaryAsync(t.DeptUser.Client, t, t.DeptId), "finding.qty").GetProperty("values")[ThisMonth - 1].GetDecimal();
        var created = await patroller.Client.PostAsJsonAsync("/api/temuan", new
        {
            plantId = t.PlantId,
            patrolDate = DateOnly.FromDateTime(DateTime.Now),
            detail = "Temuan untuk KPI",
            areaId = area.GetProperty("areaId").GetInt32(),
            type = "Safety",
            photos = new[] { new { path = image.GetProperty("path").GetString(), fileName = "a.jpg" } },
        }, Ct).OkJsonAsync();
        await assigner.Client.PostAsJsonAsync($"/api/temuan/{created.GetProperty("findingId").GetInt32()}/tugaskan", new
        {
            deptId = t.DeptId,
            picUserId = repairer.UserId,
            dueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        }, Ct).OkJsonAsync();

        var summary = await SummaryAsync(t.DeptUser.Client, t, t.DeptId);
        Assert.Equal(before + 1, Row(summary, "finding.qty").GetProperty("values")[ThisMonth - 1].GetDecimal());
        Assert.Equal("NG", Row(summary, "finding.eval").GetProperty("texts")[ThisMonth - 1].GetString());
    }

    [Fact]
    public async Task Upload_kalender_wajib_PDF_dan_upload_ulang_menggantikan()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();
        var client = t.DeptUser.Client;

        Assert.Contains("PDF", await client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", Encoding.ASCII.GetBytes("bukan pdf"), "a.pdf"), Ct).ErrorAsync());
        Assert.Contains("PDF", await client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", Pdf, "a.jpg"), Ct).ErrorAsync());
        Assert.Contains("maksimal", await client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", [.. Pdf, .. new byte[5000]], "a.pdf"), Ct).ErrorAsync());
        Assert.Contains("departemen sendiri", await client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", Pdf, "a.pdf", deptId: t.OtherDeptId), Ct).ErrorAsync());

        await client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", Pdf, "pertama.pdf"), Ct).OkJsonAsync();
        await client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", Pdf, "kedua.pdf"), Ct).OkJsonAsync();

        var list = await t.Other.Client.GetAsync($"/api/kpi/dokumen?kind=Kalender&year={Year}&plantId={t.PlantId}", Ct).OkJsonAsync();
        var dept = list.GetProperty("departments").EnumerateArray().Single(d => d.GetProperty("deptId").GetInt32() == t.DeptId);
        var files = dept.GetProperty("files").EnumerateArray().Where(f => f.GetProperty("month").GetInt32() == ThisMonth).ToList();
        Assert.Single(files);
        Assert.Equal("kedua.pdf", files[0].GetProperty("fileName").GetString());
        Assert.False(dept.GetProperty("canUpload").GetBoolean());

        using var download = await t.Other.Client.GetAsync($"/api/kpi/dokumen/{files[0].GetProperty("documentId").GetInt32()}/unduh", Ct);
        Assert.Equal(200, (int)download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Contains("KALENDER SAFETY", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Upload_PPT_memeriksa_isi_file()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();

        Assert.Contains(".pptx", await t.DeptUser.Client.PostAsync("/api/kpi/dokumen", Upload(t, "PPT", Pdf, "a.pptx"), Ct).ErrorAsync());
        await t.DeptUser.Client.PostAsync("/api/kpi/dokumen", Upload(t, "PPT", [0x50, 0x4B, 0x03, 0x04, 0x00], "laporan.pptx"), Ct).OkJsonAsync();
    }

    [Fact]
    public async Task Dokumen_plant_lain_tidak_bisa_diunduh()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();
        await t.DeptUser.Client.PostAsync("/api/kpi/dokumen", Upload(t, "Kalender", Pdf, "a.pdf"), Ct).OkJsonAsync();
        var list = await t.DeptUser.Client.GetAsync($"/api/kpi/dokumen?kind=Kalender&year={Year}&plantId={t.PlantId}", Ct).OkJsonAsync();
        var id = list.GetProperty("departments").EnumerateArray().Single(d => d.GetProperty("deptId").GetInt32() == t.DeptId)
            .GetProperty("files")[0].GetProperty("documentId").GetInt32();

        var outsider = await api.CreateUserAsync(t.Admin, [Permissions.KpiLihat], [t.OtherPlantId]);
        Assert.Equal(404, await outsider.Client.GetAsync($"/api/kpi/dokumen/{id}/unduh", Ct).StatusAsync());
    }

    [Fact]
    public async Task PICA_tambah_ubah_hapus_hanya_departemen_sendiri()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();
        var request = new
        {
            plantId = t.PlantId,
            deptId = t.DeptId,
            year = Year,
            month = ThisMonth,
            kpiType = "Penyakit",
            problemIssue = "Demam karena cuaca",
            correctiveAction = "Istirahat cukup",
            dueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
            picName = "PIC Departemen",
            status = "Open",
        };

        Assert.Contains("departemen sendiri", await t.Other.Client.PostAsJsonAsync("/api/kpi/pica", request, Ct).ErrorAsync());
        var id = (await t.DeptUser.Client.PostAsJsonAsync("/api/kpi/pica", request, Ct).OkJsonAsync()).GetProperty("picaId").GetInt32();

        Assert.Contains("departemen sendiri", await t.Other.Client.PutAsJsonAsync($"/api/kpi/pica/{id}", request, Ct).ErrorAsync());
        await t.DeptUser.Client.PutAsJsonAsync($"/api/kpi/pica/{id}", new { request.plantId, request.deptId, request.year, request.month, request.kpiType, request.problemIssue, request.correctiveAction, request.dueDate, request.picName, status = "Closed" }, Ct).OkJsonAsync();

        var list = await t.Other.Client.GetAsync($"/api/kpi/pica?year={Year}&plantId={t.PlantId}", Ct).OkJsonAsync();
        var row = list.EnumerateArray().Single(p => p.GetProperty("picaId").GetInt32() == id);
        Assert.Equal("Closed", row.GetProperty("status").GetString());
        Assert.False(row.GetProperty("canEdit").GetBoolean());

        Assert.Equal(404, await t.Other.Client.DeleteAsync($"/api/kpi/pica/{id}", Ct).StatusAsync());
        await t.DeptUser.Client.DeleteAsync($"/api/kpi/pica/{id}", Ct).OkJsonAsync();
    }

    [Fact]
    public async Task Monitoring_menampilkan_kelengkapan_per_departemen()
    {
        api.SkipIfUnavailable();
        var t = await TeamAsync();
        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "accident", "1"), Ct).OkJsonAsync();

        var plant = async () =>
        {
            var monitoring = await t.Hse.Client.GetAsync($"/api/kpi/monitoring?year={Year}&month={ThisMonth}", Ct).OkJsonAsync();
            return monitoring.GetProperty("plants").EnumerateArray().Single(p => p.GetProperty("plantId").GetInt32() == t.PlantId)
                .GetProperty("departments").EnumerateArray().Single(d => d.GetProperty("deptId").GetInt32() == t.DeptId);
        };

        // Belum lengkap selama kasus penyakit belum diisi, walau nilainya nol harus tetap diisi.
        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "disease", ""), Ct).OkJsonAsync();
        Assert.False((await plant()).GetProperty("hasInput").GetBoolean());
        await t.DeptUser.Client.PutAsJsonAsync("/api/kpi/input", Input(t, "disease", "0"), Ct).OkJsonAsync();
        Assert.True((await plant()).GetProperty("hasInput").GetBoolean());
    }
}
