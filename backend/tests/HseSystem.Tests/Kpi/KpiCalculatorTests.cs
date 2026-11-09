using HseSystem.Api.Kpi;

namespace HseSystem.Tests.Kpi;

/// <summary>Angka pembanding diambil dari file "MASTER KPI SUMMARY ALL DEPT 2026" di Drive HSE.</summary>
public sealed class KpiCalculatorTests
{
    private static KpiMonthData[] Months(int?[]? accident = null, int?[]? disease = null, (int F, int C)[]? findings = null) =>
        [.. Enumerable.Range(0, 12).Select(i => new KpiMonthData(
            accident is not null && i < accident.Length ? accident[i] : null,
            disease is not null && i < disease.Length ? disease[i] : null,
            null, null,
            findings is not null && i < findings.Length ? findings[i].F : 0,
            findings is not null && i < findings.Length ? findings[i].C : 0))];

    private static KpiRow Row(IReadOnlyList<KpiRow> rows, string key) => rows.Single(r => r.Key == key);

    [Fact]
    public void Departemen_accident_sama_dengan_sheet_PCD_KIMU()
    {
        var rows = KpiCalculator.Rows(Months(accident: [0, 0, 1, 0, 0, 0, 0, 0, 0, 0]), isAll: false, lastMonth: 10);

        Assert.Equal([0m, 0m, 1m, 1m], Row(rows, "accident.accum").Values.Take(4).Select(v => v!.Value));
        Assert.Equal([1m, 1m, 0.9167m, 1m], Row(rows, "accident.month").Values.Take(4).Select(v => Math.Round(v!.Value, 4)));
        Assert.Equal([0.0833m, 0.1667m, 0.2431m, 0.3264m], Row(rows, "accident.ytd").Values.Take(4).Select(v => Math.Round(v!.Value, 4)));
        Assert.Equal(0.9931m, Row(rows, "accident.ytd").Values[11]);
        Assert.Equal(["OK", "OK", "NG", "OK"], Row(rows, "accident.eval").Texts.Take(4));
    }

    [Fact]
    public void All_accident_memakai_akumulasi_sama_dengan_sheet_TEGAL()
    {
        var rows = KpiCalculator.Rows(Months(accident: [0, 1, 1, 0, 0, 0, 1, 0, 0, 1]), isAll: true, lastMonth: 10);

        Assert.Equal([1m, 0.9167m, 0.8333m, 0.8333m], Row(rows, "accident.month").Values.Take(4).Select(v => Math.Round(v!.Value, 4)));
        Assert.Equal(0.6667m, Row(rows, "accident.month").Values[11]);
        Assert.Equal(["OK", "NG", "NG", "OK"], Row(rows, "accident.eval").Texts.Take(4));
    }

    [Fact]
    public void Departemen_penyakit_lebih_dari_satu_NG()
    {
        var rows = KpiCalculator.Rows(Months(disease: [1, 1, 2]), isAll: false, lastMonth: 3);

        Assert.Equal([0.9167m, 0.9167m, 0.8333m], Row(rows, "disease.month").Values.Take(3).Select(v => Math.Round(v!.Value, 4)));
        Assert.Equal([0.0764m, 0.1528m, 0.2222m], Row(rows, "disease.ytd").Values.Take(3).Select(v => Math.Round(v!.Value, 4)));
        Assert.Equal(["OK", "OK", "NG"], Row(rows, "disease.eval").Texts.Take(3));
        Assert.DoesNotContain(rows, r => r.Key == "disease.avg");
    }

    [Fact]
    public void All_penyakit_memakai_rata_rata_dibagi_10_sama_dengan_sheet_TEGAL()
    {
        var rows = KpiCalculator.Rows(Months(disease: [7, 8, 7, 17, 10, 15]), isAll: true, lastMonth: 10);

        Assert.Equal([0.7m, 0.8m, 0.7m, 1.7m, 1m, 1.5m], Row(rows, "disease.avg").Values.Take(6).Select(v => v!.Value));
        Assert.Equal([0.9417m, 0.9333m, 0.9417m, 0.8583m], Row(rows, "disease.month").Values.Take(4).Select(v => Math.Round(v!.Value, 4)));
        Assert.Equal(["OK", "OK", "OK", "NG", "OK", "NG"], Row(rows, "disease.eval").Texts.Take(6));
    }

    [Fact]
    public void Temuan_harus_close_semua_supaya_OK()
    {
        var rows = KpiCalculator.Rows(Months(findings: [(26, 26), (37, 37), (13, 12)]), isAll: false, lastMonth: 12);

        Assert.Equal([1m, 1m, 0.9231m], Row(rows, "finding.act").Values.Take(3).Select(v => v!.Value));
        Assert.Equal(["OK", "OK", "NG"], Row(rows, "finding.eval").Texts.Take(3));
        Assert.Equal(0.2436m, Row(rows, "finding.ytd").Values[2]);
    }

    [Fact]
    public void Bulan_tanpa_temuan_kosong_bukan_error_seperti_DIV0_di_Excel()
    {
        var rows = KpiCalculator.Rows(Months(findings: [(5, 5)]), isAll: false, lastMonth: 12);

        Assert.Null(Row(rows, "finding.act").Values[1]);
        Assert.Null(Row(rows, "finding.eval").Texts[1]);
        Assert.Null(Row(rows, "finding.ytd").Values[1]);
    }

    [Fact]
    public void Bulan_yang_belum_berjalan_tidak_dievaluasi_tapi_ytd_tetap_dihitung()
    {
        var rows = KpiCalculator.Rows(Months(accident: [0, 0]), isAll: false, lastMonth: 2);

        Assert.Equal("OK", Row(rows, "accident.eval").Texts[1]);
        Assert.Null(Row(rows, "accident.eval").Texts[2]);
        Assert.Equal(1m, Row(rows, "accident.ytd").Values[11]);
    }

    [Fact]
    public void Akumulasi_penyakit_benar_benar_kumulatif()
    {
        var rows = KpiCalculator.Rows(Months(disease: [3, 4, 4]), isAll: true, lastMonth: 3);
        Assert.Equal([3m, 7m, 11m], Row(rows, "disease.accum").Values.Take(3).Select(v => v!.Value));
    }

    [Fact]
    public void Baris_input_hanya_bisa_diisi_di_sheet_departemen()
    {
        var dept = KpiCalculator.Rows(Months(), isAll: false, lastMonth: 12);
        var all = KpiCalculator.Rows(Months(), isAll: true, lastMonth: 12);

        Assert.Equal(["accident", "accidentNote", "disease", "diseaseTypes"], dept.Where(r => r.Field is not null).Select(r => r.Field));
        Assert.DoesNotContain(all, r => r.Field is not null);
    }

    [Fact]
    public void Isian_kosong_tetap_kosong_di_departemen_dan_dihitung_nol()
    {
        var dept = KpiCalculator.Rows(Months(), isAll: false, lastMonth: 12);
        Assert.Null(Row(dept, "accident.act").Values[0]);
        Assert.Equal(1m, Row(dept, "accident.month").Values[0]);
    }

    [Fact]
    public void Review_mengikuti_panduan_PPT()
    {
        var review = KpiCalculator.Review(Months(accident: [0, 0, 1], disease: [1, 1, 1], findings: [(0, 0), (0, 0), (13, 12)]), isAll: false, month: 3, lastMonth: 3);

        Assert.Equal("1 Accident", review[0].ThisMonth);
        Assert.Equal("1 Accident", review[0].YearToEndAcc);
        Assert.Equal(0.9931m, review[0].YearToEndPercent);
        Assert.False(review[0].Achieved);
        Assert.Equal("3 Kasus penyakit", review[1].YearToEndAcc);
        Assert.Equal("Findings 13 · Close 12 · Open 1", review[2].ThisMonth);
        Assert.Equal(0.9231m, review[2].YearToEndPercent);
        Assert.False(review[2].Achieved);
    }
}
