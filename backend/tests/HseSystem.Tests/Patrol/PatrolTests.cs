using System.Net.Http.Json;
using System.Text.Json;
using HseSystem.Api.Auth;
using HseSystem.Tests.Auth;
using HseSystem.Tests.Infrastructure;

namespace HseSystem.Tests.Patrol;

[Collection(ApiCollection.Name)]
public sealed class PatrolTests(HseApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Patroller, tim HSE, user departemen, dan pemeriksa di satu plant.</summary>
    private sealed record Team(HttpClient Admin, TestUser Patroller, TestUser Hse, TestUser DeptUser, TestUser Verifier, int PlantId, int AreaId, int DeptId, int OtherDeptId);

    private async Task<int> PlantIdAsync(HttpClient admin, string code)
    {
        var plants = await admin.GetAsync("/api/master/plants", Ct).OkJsonAsync();
        return plants.EnumerateArray().Single(p => p.GetProperty("code").GetString() == code).GetProperty("plantId").GetInt32();
    }

    private async Task<int> DeptIdAsync(HttpClient admin, string code)
    {
        var departments = await admin.GetAsync("/api/master/departments", Ct).OkJsonAsync();
        return departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == code).GetProperty("deptId").GetInt32();
    }

    private async Task<Team> TeamAsync(string plantCode = "BTI")
    {
        var admin = await api.AdminAsync();
        var plantId = await PlantIdAsync(admin, plantCode);
        var deptId = await DeptIdAsync(admin, "PRD");
        var otherDeptId = await DeptIdAsync(admin, "MKT");

        var patroller = await api.CreateUserAsync(admin, [Permissions.TemuanLihat, Permissions.TemuanInput], [plantId]);
        var hse = await api.CreateUserAsync(admin, [Permissions.TemuanLihat, Permissions.TemuanTugaskan], [plantId]);
        var deptUser = await api.CreateUserAsync(admin, [Permissions.PerbaikanInput], [plantId], deptId);
        var verifier = await api.CreateUserAsync(admin, [Permissions.TemuanLihat, Permissions.TemuanVerifikasi], [plantId]);
        var area = await admin.PostAsJsonAsync("/api/master/areas", new { plantId, name = "Area " + Guid.NewGuid().ToString("N")[..8], isActive = true }, Ct).OkJsonAsync();

        return new Team(admin, patroller, hse, deptUser, verifier, plantId, area.GetProperty("areaId").GetInt32(), deptId, otherDeptId);
    }

    private static Task<JsonElement> UploadAsync(HttpClient client, string category) =>
        client.PostAsync("/api/images", AuthTests.FakeJpegForm(category: category), Ct).OkJsonAsync();

    private static object[] Photos(JsonElement image) =>
        [new { path = image.GetProperty("path").GetString(), fileName = image.GetProperty("fileName").GetString() }];

    private static async Task<object> NewFindingAsync(Team team, DateOnly? patrolDate = null, string type = "Safety") => new
    {
        plantId = team.PlantId,
        patrolDate = patrolDate ?? Today,
        detail = "Barang tidak pada tempatnya",
        areaId = team.AreaId,
        type,
        photos = Photos(await UploadAsync(team.Patroller.Client, "temuan")),
    };

    private static async Task<int> CreateAsync(Team team, DateOnly? patrolDate = null, string type = "Safety")
    {
        var created = await team.Patroller.Client.PostAsJsonAsync("/api/temuan", await NewFindingAsync(team, patrolDate, type), Ct).OkJsonAsync();
        return created.GetProperty("findingId").GetInt32();
    }

    private static Task<JsonElement> AssignAsync(Team team, int id, DateOnly? dueDate = null, int? deptId = null, int? picUserId = null) =>
        team.Hse.Client.PostAsJsonAsync($"/api/temuan/{id}/tugaskan", new
        {
            deptId = deptId ?? team.DeptId,
            picUserId = picUserId ?? team.DeptUser.UserId,
            dueDate = dueDate ?? Today.AddDays(7),
        }, Ct).OkJsonAsync();

    private static async Task<int> CreateAssignedAsync(Team team, DateOnly? patrolDate = null, DateOnly? dueDate = null, string type = "Safety")
    {
        var id = await CreateAsync(team, patrolDate, type);
        await AssignAsync(team, id, dueDate);
        return id;
    }

    private static async Task<JsonElement> SubmitRepairAsync(TestUser user, int id) =>
        await user.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", new
        {
            countermeasure = "Barang sudah dirapikan",
            photos = Photos(await UploadAsync(user.Client, "perbaikan")),
            selesai = true,
        }, Ct).OkJsonAsync();

    private static async Task<List<int>> ListIdsAsync(HttpClient client, string query = "")
    {
        var list = await client.GetAsync($"/api/temuan?pageSize=100{query}", Ct).OkJsonAsync();
        return [.. list.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("findingId").GetInt32())];
    }

    [Fact]
    public async Task Alur_lengkap_dari_patroller_HSE_departemen_sampai_close()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();

        var id = await CreateAsync(team);
        var open = await team.Hse.Client.GetAsync($"/api/temuan/{id}", Ct).OkJsonAsync();
        Assert.Equal("Open", open.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, open.GetProperty("deptId").ValueKind);
        Assert.Equal(JsonValueKind.Null, open.GetProperty("dueDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, open.GetProperty("picUserId").ValueKind);
        Assert.True(open.GetProperty("canAssign").GetBoolean());

        var assigned = await AssignAsync(team, id);
        Assert.Equal("Ditugaskan", assigned.GetProperty("status").GetString());
        Assert.Equal("PRD", assigned.GetProperty("deptCode").GetString());
        Assert.Equal(team.DeptUser.UserId, assigned.GetProperty("picUserId").GetInt32());
        Assert.Contains(", PIC ", assigned.GetProperty("events")[1].GetProperty("note").GetString());

        var repaired = await SubmitRepairAsync(team.DeptUser, id);
        Assert.Equal("Verifikasi", repaired.GetProperty("status").GetString());
        Assert.False(repaired.GetProperty("canRepair").GetBoolean());

        var before = await team.Verifier.Client.GetAsync($"/api/temuan/{id}", Ct).OkJsonAsync();
        Assert.True(before.GetProperty("canVerify").GetBoolean());
        var closed = await team.Verifier.Client.PostAsJsonAsync($"/api/temuan/{id}/verifikasi", new { setuju = true }, Ct).OkJsonAsync();

        Assert.Equal("Close", closed.GetProperty("status").GetString());
        Assert.Equal(["Dibuat", "Ditugaskan", "Diajukan", "Ditutup"], closed.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("action").GetString()));
        Assert.Equal(["Temuan", "Perbaikan"], closed.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("kind").GetString()));
        Assert.StartsWith("/uploads/temuan/", closed.GetProperty("photos")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Temuan_baru_belum_muncul_di_departemen_sampai_ditugaskan()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var id = await CreateAsync(team);

        Assert.DoesNotContain(id, await ListIdsAsync(team.DeptUser.Client));
        Assert.Equal(404, await team.DeptUser.Client.GetAsync($"/api/temuan/{id}", Ct).StatusAsync());
        Assert.Equal(404, await team.DeptUser.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", new { countermeasure = "Coba", selesai = false }, Ct).StatusAsync());

        await AssignAsync(team, id);

        Assert.Contains(id, await ListIdsAsync(team.DeptUser.Client));
        var detail = await team.DeptUser.Client.GetAsync($"/api/temuan/{id}", Ct).OkJsonAsync();
        Assert.True(detail.GetProperty("canRepair").GetBoolean());
    }

    [Fact]
    public async Task Semua_user_departemen_itu_bisa_mengerjakan_departemen_lain_tidak()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var id = await CreateAssignedAsync(team);
        var colleague = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [team.PlantId], team.DeptId);
        var otherDept = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [team.PlantId], team.OtherDeptId);
        var otherDeptWithView = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat, Permissions.PerbaikanInput], [team.PlantId], team.OtherDeptId);
        var noDept = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat, Permissions.PerbaikanInput], [team.PlantId]);

        // Departemen lain tanpa hak lihat: tidak tahu temuan ini ada.
        Assert.DoesNotContain(id, await ListIdsAsync(otherDept.Client));
        Assert.Equal(404, await otherDept.Client.GetAsync($"/api/temuan/{id}", Ct).StatusAsync());

        var body = new { countermeasure = "Coba", selesai = false };
        Assert.Contains("departemen yang ditugaskan", await otherDeptWithView.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", body, Ct).ErrorAsync());
        Assert.Contains("departemen yang ditugaskan", await noDept.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", body, Ct).ErrorAsync());

        var repaired = await SubmitRepairAsync(colleague, id);
        Assert.StartsWith("Pengguna", repaired.GetProperty("repairedByName").GetString());
    }

    [Fact]
    public async Task Hanya_tim_HSE_yang_menugaskan_dan_bisa_diganti_selama_belum_dikerjakan()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var id = await CreateAsync(team);
        var otherPic = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [team.PlantId], team.OtherDeptId);
        var assign = new { deptId = team.DeptId, picUserId = team.DeptUser.UserId, dueDate = Today.AddDays(5) };

        Assert.Equal(403, await team.Patroller.Client.PostAsJsonAsync($"/api/temuan/{id}/tugaskan", assign, Ct).StatusAsync());
        Assert.Equal(403, await team.DeptUser.Client.PostAsJsonAsync($"/api/temuan/{id}/tugaskan", assign, Ct).StatusAsync());

        await AssignAsync(team, id, deptId: team.OtherDeptId, picUserId: otherPic.UserId);
        var changed = await AssignAsync(team, id, deptId: team.DeptId);
        Assert.Equal("PRD", changed.GetProperty("deptCode").GetString());
        Assert.Equal(team.DeptUser.UserId, changed.GetProperty("picUserId").GetInt32());

        await team.DeptUser.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", new { countermeasure = "Mulai", selesai = false }, Ct).OkJsonAsync();
        Assert.Contains("sudah dikerjakan", await team.Hse.Client.PostAsJsonAsync($"/api/temuan/{id}/tugaskan", assign, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Penugasan_dengan_data_salah_ditolak()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var id = await CreateAsync(team, Today.AddDays(-3));
        var pic = team.DeptUser.UserId;
        var url = $"/api/temuan/{id}/tugaskan";
        var due = Today.AddDays(1);

        Assert.Contains("sebelum tanggal patrol", await team.Hse.Client.PostAsJsonAsync(url, new { deptId = team.DeptId, picUserId = pic, dueDate = Today.AddDays(-5) }, Ct).ErrorAsync());
        Assert.Contains("Due date wajib", await team.Hse.Client.PostAsJsonAsync(url, new { deptId = team.DeptId, picUserId = pic }, Ct).ErrorAsync());
        Assert.Contains("PIC wajib", await team.Hse.Client.PostAsJsonAsync(url, new { deptId = team.DeptId, dueDate = due }, Ct).ErrorAsync());
        Assert.Contains("Departemen tidak ditemukan", await team.Hse.Client.PostAsJsonAsync(url, new { deptId = 999999, picUserId = pic, dueDate = due }, Ct).ErrorAsync());

        // PIC harus dari departemen yang sama, di plant temuan, dengan hak mengisi perbaikan.
        var kimu = await PlantIdAsync(team.Admin, "KIMU");
        var picDeptLain = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [team.PlantId], team.OtherDeptId);
        var picPlantLain = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [kimu], team.DeptId);
        var tanpaHak = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat], [team.PlantId], team.DeptId);
        foreach (var salah in new[] { picDeptLain, picPlantLain, tanpaHak })
        {
            Assert.Contains("PIC harus", await team.Hse.Client.PostAsJsonAsync(url, new { deptId = team.DeptId, picUserId = salah.UserId, dueDate = due }, Ct).ErrorAsync());
        }
    }

    [Fact]
    public async Task Pilihan_PIC_hanya_untuk_tim_HSE_dan_berisi_user_departemen_di_plant_itu()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();

        var hse = await team.Hse.Client.GetAsync($"/api/temuan/options?plantId={team.PlantId}", Ct).OkJsonAsync();
        var pic = hse.GetProperty("pics").EnumerateArray().Single(p => p.GetProperty("userId").GetInt32() == team.DeptUser.UserId);
        Assert.Equal(team.DeptId, pic.GetProperty("deptId").GetInt32());
        Assert.DoesNotContain(hse.GetProperty("pics").EnumerateArray(), p => p.GetProperty("userId").GetInt32() == team.Patroller.UserId);

        var patroller = await team.Patroller.Client.GetAsync($"/api/temuan/options?plantId={team.PlantId}", Ct).OkJsonAsync();
        Assert.Equal(0, patroller.GetProperty("pics").GetArrayLength());
    }

    [Fact]
    public async Task Filter_PIC_saya_hanya_temuan_yang_saya_jadi_PIC()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var colleague = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [team.PlantId], team.DeptId);
        var mine = await CreateAssignedAsync(team);
        var other = await CreateAsync(team);
        await AssignAsync(team, other, picUserId: colleague.UserId);

        var ids = await ListIdsAsync(team.DeptUser.Client, "&pic=true");

        Assert.Contains(mine, ids);
        Assert.DoesNotContain(other, ids);
        // Temuan departemen tetap terlihat tanpa filter PIC.
        Assert.Contains(other, await ListIdsAsync(team.DeptUser.Client));
    }

    [Fact]
    public async Task Ditolak_kembali_ke_departemen_lalu_bisa_diajukan_ulang()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var id = await CreateAssignedAsync(team);
        await SubmitRepairAsync(team.DeptUser, id);

        Assert.Contains("alasan", await team.Verifier.Client.PostAsJsonAsync($"/api/temuan/{id}/verifikasi", new { setuju = false }, Ct).ErrorAsync());

        var rejected = await team.Verifier.Client.PostAsJsonAsync($"/api/temuan/{id}/verifikasi", new { setuju = false, catatan = "Masih berantakan" }, Ct).OkJsonAsync();
        Assert.Equal("NotClose", rejected.GetProperty("status").GetString());
        Assert.Equal("Masih berantakan", rejected.GetProperty("verifyNote").GetString());

        var again = await SubmitRepairAsync(team.DeptUser, id);
        Assert.Equal("Verifikasi", again.GetProperty("status").GetString());
        Assert.Single(again.GetProperty("photos").EnumerateArray(), p => p.GetProperty("kind").GetString() == "Perbaikan");
        Assert.Equal(JsonValueKind.Null, again.GetProperty("verifyNote").ValueKind);
    }

    [Fact]
    public async Task Simpan_progres_tanpa_foto_boleh_tapi_mengajukan_wajib_foto()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var id = await CreateAssignedAsync(team);

        var progress = await team.DeptUser.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", new { countermeasure = "Menunggu material", selesai = false }, Ct).OkJsonAsync();
        Assert.Equal("OnProgress", progress.GetProperty("status").GetString());

        Assert.Contains("foto", await team.DeptUser.Client.PostAsJsonAsync($"/api/temuan/{id}/perbaikan", new { countermeasure = "Selesai", selesai = true }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Yang_memperbaiki_tidak_boleh_memeriksa_sendiri()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var both = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat, Permissions.PerbaikanInput, Permissions.TemuanVerifikasi], [team.PlantId], team.DeptId);
        var id = await CreateAssignedAsync(team);

        var repaired = await SubmitRepairAsync(both, id);

        Assert.False(repaired.GetProperty("canVerify").GetBoolean());
        Assert.Contains("orang lain", await both.Client.PostAsJsonAsync($"/api/temuan/{id}/verifikasi", new { setuju = true }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Temuan_plant_lain_tidak_terlihat_kecuali_punya_hak_semua_plant()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync("KIMU");
        var id = await CreateAsync(team);
        var tegal = await PlantIdAsync(team.Admin, "TEGAL");
        var outsider = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat, Permissions.TemuanInput, Permissions.TemuanTugaskan], [tegal]);
        var headOffice = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat, Permissions.TemuanSemuaPlant]);

        Assert.Equal(404, await outsider.Client.GetAsync($"/api/temuan/{id}", Ct).StatusAsync());
        Assert.DoesNotContain(id, await ListIdsAsync(outsider.Client));
        Assert.Contains("tidak terdaftar", await outsider.Client.PostAsJsonAsync("/api/temuan", await NewFindingAsync(team), Ct).ErrorAsync());
        Assert.Equal(404, await outsider.Client.PostAsJsonAsync($"/api/temuan/{id}/tugaskan", new { deptId = team.DeptId, picUserId = team.DeptUser.UserId, dueDate = Today.AddDays(1) }, Ct).StatusAsync());

        Assert.Equal(200, await headOffice.Client.GetAsync($"/api/temuan/{id}", Ct).StatusAsync());
    }

    [Fact]
    public async Task Departemen_sama_di_plant_lain_tidak_melihat_temuan_ini()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync("KIMU");
        var id = await CreateAssignedAsync(team);
        var tegal = await PlantIdAsync(team.Admin, "TEGAL");
        var samaDeptPlantLain = await api.CreateUserAsync(team.Admin, [Permissions.PerbaikanInput], [tegal], team.DeptId);

        Assert.DoesNotContain(id, await ListIdsAsync(samaDeptPlantLain.Client));
        Assert.Equal(404, await samaDeptPlantLain.Client.GetAsync($"/api/temuan/{id}", Ct).StatusAsync());
    }

    [Fact]
    public async Task Filter_departemen_saya_dan_status_Open_untuk_antrean_HSE()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var open = await CreateAsync(team);
        var assigned = await CreateAssignedAsync(team);

        // Grid memuat semua hasil filter sekaligus; batasnya 5000 baris.
        Assert.Equal(5000, (await team.Hse.Client.GetAsync("/api/temuan?pageSize=9999", Ct).OkJsonAsync()).GetProperty("pageSize").GetInt32());

        var queue = await ListIdsAsync(team.Hse.Client, "&status=Open");
        Assert.Contains(open, queue);
        Assert.DoesNotContain(assigned, queue);

        var both = await api.CreateUserAsync(team.Admin, [Permissions.TemuanLihat, Permissions.PerbaikanInput], [team.PlantId], team.DeptId);
        var mine = await ListIdsAsync(both.Client, "&mine=true");
        Assert.Contains(assigned, mine);
        Assert.DoesNotContain(open, mine);
    }

    [Fact]
    public async Task Lewat_due_date_otomatis_overdue_dan_temuan_Open_belum_bisa_overdue()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync("BTP");
        var late = await CreateAssignedAsync(team, Today.AddDays(-10), Today.AddDays(-3));
        var onTime = await CreateAssignedAsync(team, Today, Today.AddDays(3));
        var open = await CreateAsync(team, Today.AddDays(-30));

        var ids = await ListIdsAsync(team.Patroller.Client, $"&overdue=true&plantId={team.PlantId}");

        Assert.Contains(late, ids);
        Assert.DoesNotContain(onTime, ids);
        Assert.DoesNotContain(open, ids);
        Assert.True((await team.Patroller.Client.GetAsync($"/api/temuan/{late}", Ct).OkJsonAsync()).GetProperty("isOverdue").GetBoolean());
    }

    [Fact]
    public async Task Nomor_temuan_berurutan_per_plant_per_bulan()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync("TEGAL");
        var date = new DateOnly(Today.Year - 1, 3, 15);

        var first = await CreateAsync(team, date);
        var second = await CreateAsync(team, date);

        var a = await team.Patroller.Client.GetAsync($"/api/temuan/{first}", Ct).OkJsonAsync();
        var b = await team.Patroller.Client.GetAsync($"/api/temuan/{second}", Ct).OkJsonAsync();
        Assert.Equal($"TEGAL-{date.Year}03-001", a.GetProperty("number").GetString());
        Assert.Equal($"TEGAL-{date.Year}03-002", b.GetProperty("number").GetString());
    }

    [Fact]
    public async Task Path_foto_palsu_atau_dipakai_ulang_ditolak()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var image = await UploadAsync(team.Patroller.Client, "temuan");
        var wrongCategory = await UploadAsync(team.Patroller.Client, "perbaikan");

        object Body(object[] photos) => new { plantId = team.PlantId, detail = "Uji foto", areaId = team.AreaId, type = "5R", photos };
        Task<string> TryAsync(object[] photos) => team.Patroller.Client.PostAsJsonAsync("/api/temuan", Body(photos), Ct).ErrorAsync();

        Assert.Contains("tidak ditemukan", await TryAsync([new { path = "../../appsettings.json", fileName = "x" }]));
        Assert.Contains("tidak ditemukan", await TryAsync([new { path = "temuan/2026/11/00000000000000000000000000000000.jpg", fileName = "x" }]));
        Assert.Contains("tidak ditemukan", await TryAsync(Photos(wrongCategory)));
        Assert.Contains("minimal satu foto", await TryAsync([]));

        await team.Patroller.Client.PostAsJsonAsync("/api/temuan", Body(Photos(image)), Ct).OkJsonAsync();
        Assert.Contains("sudah dipakai", await TryAsync(Photos(image)));
    }

    [Fact]
    public async Task Area_plant_lain_dan_tanggal_salah_ditolak()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var kimu = await PlantIdAsync(team.Admin, "KIMU");
        var otherArea = await team.Admin.PostAsJsonAsync("/api/master/areas", new { plantId = kimu, name = "Lain " + Guid.NewGuid().ToString("N")[..6], isActive = true }, Ct).OkJsonAsync();

        async Task<string> TryAsync(Action<Dictionary<string, object>> change)
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(await NewFindingAsync(team), JsonSerializerOptions.Web))!;
            change(body);
            return await team.Patroller.Client.PostAsJsonAsync("/api/temuan", body, Ct).ErrorAsync();
        }

        Assert.Contains("Area tidak ditemukan", await TryAsync(b => b["areaId"] = otherArea.GetProperty("areaId").GetInt32()));
        Assert.Contains("masa depan", await TryAsync(b => b["patrolDate"] = Today.AddDays(1)));
        Assert.Contains("Safety atau 5R", await TryAsync(b => b["type"] = "Lain"));
    }

    [Fact]
    public async Task Dashboard_merekap_dan_user_departemen_hanya_melihat_miliknya()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync("BTI");
        var year = Today.Year - 2;
        var date = new DateOnly(year, 5, 10);

        var closed = await CreateAssignedAsync(team, date, date.AddDays(7));
        await SubmitRepairAsync(team.DeptUser, closed);
        await team.Verifier.Client.PostAsJsonAsync($"/api/temuan/{closed}/verifikasi", new { setuju = true }, Ct).OkJsonAsync();
        await CreateAssignedAsync(team, date, date.AddDays(7));
        await CreateAssignedAsync(team, date, date.AddDays(7), "5R");
        await CreateAsync(team, date);

        var dashboard = await team.Verifier.Client.GetAsync($"/api/dashboard?year={year}", Ct).OkJsonAsync();
        var rows = dashboard.GetProperty("rows").EnumerateArray().ToList();
        var safety = rows.Single(r => r.GetProperty("type").GetString() == "Safety" && r.GetProperty("month").GetInt32() == 5);
        Assert.Equal(3, safety.GetProperty("total").GetInt32());
        Assert.Equal(1, safety.GetProperty("closed").GetInt32());
        Assert.Equal(1, safety.GetProperty("overdue").GetInt32());
        Assert.Equal(0, safety.GetProperty("closedOnTime").GetInt32());

        // Rekap per departemen: temuan Open masuk baris tanpa departemen.
        var departments = dashboard.GetProperty("departments").EnumerateArray().ToList();
        var produksi = departments.Where(d => d.GetProperty("deptCode").ValueKind == JsonValueKind.String && d.GetProperty("deptCode").GetString() == "PRD").ToList();
        Assert.Equal(3, produksi.Sum(d => d.GetProperty("total").GetInt32()));
        Assert.Equal(1, produksi.Sum(d => d.GetProperty("closed").GetInt32()));
        Assert.Equal(2, produksi.Sum(d => d.GetProperty("overdue").GetInt32()));
        Assert.Equal(1, departments.Where(d => d.GetProperty("deptCode").ValueKind == JsonValueKind.Null).Sum(d => d.GetProperty("total").GetInt32()));

        var statuses = dashboard.GetProperty("statuses").EnumerateArray()
            .GroupBy(s => s.GetProperty("status").GetString()!)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.GetProperty("total").GetInt32()));
        Assert.Equal(1, statuses["Close"]);
        Assert.Equal(2, statuses["Ditugaskan"]);
        Assert.Equal(1, statuses["Open"]);

        // Daftar overdue: berisi nomor, PIC, dan lama keterlambatan; temuan Close dan Open tidak ikut.
        var overdue = dashboard.GetProperty("overdue").EnumerateArray().ToList();
        Assert.Equal(2, overdue.Count);
        Assert.All(overdue, o =>
        {
            Assert.Equal("PRD", o.GetProperty("deptCode").GetString());
            Assert.Equal(JsonValueKind.String, o.GetProperty("picName").ValueKind);
            Assert.Equal(Today.DayNumber - date.AddDays(7).DayNumber, o.GetProperty("daysLate").GetInt32());
        });
        Assert.DoesNotContain(overdue, o => o.GetProperty("findingId").GetInt32() == closed);

        // Temuan yang belum ditugaskan tidak masuk hitungan user departemen.
        var deptRows = (await team.DeptUser.Client.GetAsync($"/api/dashboard?year={year}", Ct).OkJsonAsync()).GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, deptRows.Single(r => r.GetProperty("type").GetString() == "Safety").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Master_area_dan_departemen_wajib_hak_kelola_dan_tidak_boleh_kembar()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();

        Assert.Equal(403, await team.Patroller.Client.GetAsync("/api/master/areas", Ct).StatusAsync());

        var name = "Gudang " + Guid.NewGuid().ToString("N")[..6];
        var area = await team.Admin.PostAsJsonAsync("/api/master/areas", new { plantId = team.PlantId, name, isActive = true }, Ct).OkJsonAsync();
        Assert.Contains("sudah ada", await team.Admin.PostAsJsonAsync("/api/master/areas", new { plantId = team.PlantId, name = name.ToLowerInvariant(), isActive = true }, Ct).ErrorAsync());

        // Area nonaktif hilang dari pilihan form.
        var areaId = area.GetProperty("areaId").GetInt32();
        await team.Admin.PutAsJsonAsync($"/api/master/areas/{areaId}", new { plantId = team.PlantId, name, isActive = false }, Ct).OkJsonAsync();
        var options = await team.Patroller.Client.GetAsync($"/api/temuan/options?plantId={team.PlantId}", Ct).OkJsonAsync();
        Assert.DoesNotContain(options.GetProperty("areas").EnumerateArray(), a => a.GetProperty("areaId").GetInt32() == areaId);

        Assert.Contains("sudah dipakai", await team.Admin.PostAsJsonAsync("/api/master/departments", new { code = "hse", name = "Kembar", isActive = true }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Pengguna_punya_departemen_dan_departemen_asing_ditolak()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();

        var me = await team.DeptUser.Client.GetAsync("/api/auth/me", Ct).OkJsonAsync();
        Assert.Equal(team.DeptId, me.GetProperty("deptId").GetInt32());
        Assert.Equal("Produksi", me.GetProperty("deptName").GetString());

        // Ganti departemen berlaku seketika.
        await team.Admin.PutAsJsonAsync($"/api/admin/users/{team.DeptUser.UserId}", new
        {
            displayName = "Pindah", isActive = true, roleIds = new[] { team.DeptUser.RoleId }, plantIds = new[] { team.PlantId }, deptId = team.OtherDeptId,
        }, Ct).OkJsonAsync();
        Assert.Equal(team.OtherDeptId, (await team.DeptUser.Client.GetAsync("/api/auth/me", Ct).OkJsonAsync()).GetProperty("deptId").GetInt32());

        Assert.Contains("Departemen tidak ditemukan", await team.Admin.PutAsJsonAsync($"/api/admin/users/{team.DeptUser.UserId}", new
        {
            displayName = "Pindah", isActive = true, roleIds = new[] { team.DeptUser.RoleId }, deptId = 999999,
        }, Ct).ErrorAsync());
    }

    [Fact]
    public async Task Jadwal_genba_satu_per_plant_per_bulan()
    {
        api.SkipIfUnavailable();
        var team = await TeamAsync();
        var manager = await api.CreateUserAsync(team.Admin, [Permissions.JadwalKelola], [team.PlantId]);
        var year = 2099;

        async Task<List<JsonElement>> ListAsync()
        {
            var rows = await team.Patroller.Client.GetAsync($"/api/genba?year={year}", Ct).OkJsonAsync();
            return [.. rows.EnumerateArray().Where(r => r.GetProperty("plantId").GetInt32() == team.PlantId)];
        }

        Assert.Equal(403, await team.Patroller.Client.PutAsJsonAsync("/api/genba", new { plantId = team.PlantId, year, month = 3, plannedDate = "2099-03-10" }, Ct).StatusAsync());
        Assert.Contains("di dalam bulan", await manager.Client.PutAsJsonAsync("/api/genba", new { plantId = team.PlantId, year, month = 3, plannedDate = "2099-04-10" }, Ct).ErrorAsync());

        await manager.Client.PutAsJsonAsync("/api/genba", new { plantId = team.PlantId, year, month = 3, plannedDate = "2099-03-10" }, Ct).OkJsonAsync();
        await manager.Client.PutAsJsonAsync("/api/genba", new { plantId = team.PlantId, year, month = 3, plannedDate = "2099-03-20" }, Ct).OkJsonAsync();

        var rows = await ListAsync();
        Assert.Equal("2099-03-20", Assert.Single(rows).GetProperty("plannedDate").GetString());

        await manager.Client.PutAsJsonAsync("/api/genba", new { plantId = team.PlantId, year, month = 3, plannedDate = (string?)null }, Ct).OkJsonAsync();
        Assert.Empty(await ListAsync());
    }
}
