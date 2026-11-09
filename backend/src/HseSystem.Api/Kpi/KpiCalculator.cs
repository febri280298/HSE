namespace HseSystem.Api.Kpi;

/// <summary>Data satu bulan. <c>Accident</c>/<c>Disease</c> null = belum diisi, dihitung sebagai 0 seperti sel kosong di Excel.</summary>
public sealed record KpiMonthData(int? Accident, int? Disease, string? DiseaseTypes, string? AccidentNote, int Findings, int Closed)
{
    public static readonly KpiMonthData Empty = new(null, null, null, null, 0, 0);
}

/// <summary>
/// Satu baris tabel Summary KPI. <paramref name="Field"/> terisi kalau barisnya diinput departemen (kolom kuning di Excel).
/// Baris <c>eval</c> dan <c>text</c> memakai <paramref name="Texts"/>, sisanya <paramref name="Values"/>.
/// </summary>
public sealed record KpiRow(string Key, int KpiNo, string Kpi, string Label, string Format, string? Field, decimal?[] Values, string?[] Texts);

/// <summary>Satu baris slide REVIEW KPI pada PPT P2K3L.</summary>
public sealed record KpiReviewRow(int No, string Kpi, string Target, string ThisMonth, string YearToEndAcc, decimal? YearToEndPercent, bool? Achieved);

/// <summary>
/// Rumus "MASTER KPI SUMMARY ALL DEPT 2026". Sheet departemen dan sheet ALL memang berbeda:
/// ALL memakai akumulasi accident dan rata-rata kasus penyakit (jumlah kasus dibagi 10).
/// Satu perbedaan yang disengaja: ACT ACCUMULATION kasus penyakit dihitung kumulatif sungguhan,
/// karena rumus di Excel tidak konsisten antar kolom. Baris itu tidak memengaruhi persentase dan evaluasi.
/// </summary>
public static class KpiCalculator
{
    public const int MonthCount = 12;

    /// <summary>Pembagi AVERAGE ACTUAL pada sheet ALL, sama untuk keempat plant.</summary>
    public const decimal AverageDivisor = 10m;

    private const string AccidentName = "Zero Accident";
    private const string DiseaseName = "Jumlah kasus penyakit";
    private const string FindingName = "Temuan Penyimpangan 5R & Safety";

    /// <param name="lastMonth">Bulan terakhir yang sudah berjalan (1-12, 0 = seluruh tahun belum berjalan); evaluasi bulan sesudahnya dikosongkan.</param>
    public static IReadOnlyList<KpiRow> Rows(IReadOnlyList<KpiMonthData> months, bool isAll, int lastMonth)
    {
        var c = Compute(months, isAll, lastMonth);
        var rows = new List<KpiRow>
        {
            Number("accident.target", 1, AccidentName, "TARGET", Repeat(0m)),
            Input("accident.act", 1, AccidentName, "ACT", isAll ? null : "accident", months.Select(m => (decimal?)m.Accident).ToArray(), c.Accident),
            Number("accident.accum", 1, AccidentName, "ACT ACCUMULATION", c.AccidentAccum),
            Percent("accident.month", 1, AccidentName, "MONTH ACCUMULATION", c.AccidentMonth),
            Percent("accident.ytd", 1, AccidentName, "YTD ACCUMULATION (%)", c.AccidentYtd),
            Eval("accident.eval", 1, AccidentName, c.AccidentEval),
            Text("accident.note", 1, AccidentName, "KETERANGAN", isAll ? null : "accidentNote", months.Select(m => m.AccidentNote).ToArray()),

            Number("disease.target", 2, DiseaseName, "TARGET", Repeat(1m)),
            Input("disease.act", 2, DiseaseName, "ACT", isAll ? null : "disease", months.Select(m => (decimal?)m.Disease).ToArray(), c.Disease),
            Number("disease.accum", 2, DiseaseName, "ACT ACCUMULATION", c.DiseaseAccum),
        };

        if (isAll)
        {
            rows.Add(new KpiRow("disease.avg", 2, DiseaseName, "AVERAGE ACTUAL", "decimal", null, Rounded(c.DiseaseAverage), NoTexts()));
        }

        rows.AddRange(
        [
            Percent("disease.month", 2, DiseaseName, "MONTH ACCUMULATION (%)", c.DiseaseMonth),
            Percent("disease.ytd", 2, DiseaseName, "YTD ACCUMULATION (%)", c.DiseaseYtd),
            Eval("disease.eval", 2, DiseaseName, c.DiseaseEval),
            Text("disease.types", 2, DiseaseName, "JENIS PENYAKIT", isAll ? null : "diseaseTypes", months.Select(m => m.DiseaseTypes).ToArray()),

            Percent("finding.target", 3, FindingName, "TARGET", Repeat(1m)),
            Percent("finding.act", 3, FindingName, "ACT", c.FindingAct),
            Number("finding.qty", 3, FindingName, "QTY FINDINGS", months.Select(m => (decimal?)m.Findings).ToArray()),
            Number("finding.close", 3, FindingName, "QTY CLOSE", months.Select(m => (decimal?)m.Closed).ToArray()),
            Percent("finding.month", 3, FindingName, "MONTH ACCUMULATION (%)", c.FindingAct),
            Percent("finding.ytd", 3, FindingName, "YTD ACCUMULATION (%)", c.FindingYtd),
            Eval("finding.eval", 3, FindingName, c.FindingEval),
        ]);
        return rows;
    }

    /// <summary>
    /// Sesuai panduan: Year to End (%) diambil dari YTD bulan Desember, khusus temuan dari ACT bulan berjalan.
    /// </summary>
    public static IReadOnlyList<KpiReviewRow> Review(IReadOnlyList<KpiMonthData> months, bool isAll, int month, int lastMonth)
    {
        var c = Compute(months, isAll, lastMonth);
        var i = month - 1;
        var data = months[i];
        var findingPercent = c.FindingAct[i];
        return
        [
            new KpiReviewRow(1, AccidentName, "0 Accident", $"{c.Accident[i]:0} Accident", $"{c.AccidentAccum[i]:0} Accident", Round(c.AccidentYtd[^1]), Achieved(c.AccidentYtd[^1])),
            new KpiReviewRow(2, DiseaseName, "1 Kasus penyakit", $"{c.Disease[i]:0} Kasus penyakit", $"{c.DiseaseAccum[i]:0} Kasus penyakit", Round(c.DiseaseYtd[^1]), Achieved(c.DiseaseYtd[^1])),
            new KpiReviewRow(3, FindingName, "100% countermeasured", $"Findings {data.Findings} · Close {data.Closed} · Open {data.Findings - data.Closed}", "100% countermeasured", Round(findingPercent), Achieved(findingPercent)),
        ];
    }

    private static Computed Compute(IReadOnlyList<KpiMonthData> months, bool isAll, int lastMonth)
    {
        if (months.Count != MonthCount)
        {
            throw new ArgumentException($"Harus {MonthCount} bulan.", nameof(months));
        }

        var c = new Computed();
        decimal accidentAccum = 0, diseaseAccum = 0, accidentSum = 0, diseaseSum = 0, findingSum = 0;
        for (var i = 0; i < MonthCount; i++)
        {
            var m = months[i];
            var running = i < lastMonth;

            var accident = (decimal)(m.Accident ?? 0);
            accidentAccum += accident;
            c.Accident[i] = accident;
            c.AccidentAccum[i] = accidentAccum;
            c.AccidentMonth[i] = 1m - ((isAll ? accidentAccum : accident) / MonthCount);
            accidentSum += c.AccidentMonth[i]!.Value;
            c.AccidentYtd[i] = accidentSum / MonthCount;
            c.AccidentEval[i] = !running ? null : accident == 0 ? "OK" : "NG";

            var disease = (decimal)(m.Disease ?? 0);
            var measured = isAll ? disease / AverageDivisor : disease;
            diseaseAccum += disease;
            c.Disease[i] = disease;
            c.DiseaseAccum[i] = diseaseAccum;
            c.DiseaseAverage[i] = disease / AverageDivisor;
            c.DiseaseMonth[i] = 1m - (measured / MonthCount);
            diseaseSum += c.DiseaseMonth[i]!.Value;
            c.DiseaseYtd[i] = diseaseSum / MonthCount;
            c.DiseaseEval[i] = !running ? null : measured <= 1m ? "OK" : "NG";

            if (m.Findings > 0)
            {
                var act = (decimal)m.Closed / m.Findings;
                findingSum += act;
                c.FindingAct[i] = act;
                c.FindingYtd[i] = findingSum / MonthCount;
                c.FindingEval[i] = m.Closed == m.Findings ? "OK" : "NG";
            }
        }

        return c;
    }

    private static bool? Achieved(decimal? percent) => percent is { } p ? Math.Round(p, 4) >= 1m : null;

    private static decimal? Round(decimal? value) => value is { } v ? Math.Round(v, 4) : null;

    private static decimal?[] Rounded(decimal?[] values) => [.. values.Select(Round)];

    private static decimal?[] Repeat(decimal value) => [.. Enumerable.Repeat<decimal?>(value, MonthCount)];

    private static string?[] NoTexts() => new string?[MonthCount];

    private static KpiRow Number(string key, int no, string kpi, string label, decimal?[] values) =>
        new(key, no, kpi, label, "number", null, values, NoTexts());

    /// <summary>Baris ACT: sheet departemen menampilkan isian apa adanya (kosong tetap kosong), sheet ALL menampilkan jumlahnya.</summary>
    private static KpiRow Input(string key, int no, string kpi, string label, string? field, decimal?[] entered, decimal?[] totals) =>
        new(key, no, kpi, label, "number", field, field is null ? totals : entered, NoTexts());

    private static KpiRow Percent(string key, int no, string kpi, string label, decimal?[] values) =>
        new(key, no, kpi, label, "percent", null, Rounded(values), NoTexts());

    private static KpiRow Eval(string key, int no, string kpi, string?[] texts) =>
        new(key, no, kpi, "EVALUATION", "eval", null, new decimal?[MonthCount], texts);

    private static KpiRow Text(string key, int no, string kpi, string label, string? field, string?[] texts) =>
        new(key, no, kpi, label, "text", field, new decimal?[MonthCount], texts);

    private sealed class Computed
    {
        public decimal?[] Accident { get; } = new decimal?[MonthCount];
        public decimal?[] AccidentAccum { get; } = new decimal?[MonthCount];
        public decimal?[] AccidentMonth { get; } = new decimal?[MonthCount];
        public decimal?[] AccidentYtd { get; } = new decimal?[MonthCount];
        public string?[] AccidentEval { get; } = new string?[MonthCount];
        public decimal?[] Disease { get; } = new decimal?[MonthCount];
        public decimal?[] DiseaseAccum { get; } = new decimal?[MonthCount];
        public decimal?[] DiseaseAverage { get; } = new decimal?[MonthCount];
        public decimal?[] DiseaseMonth { get; } = new decimal?[MonthCount];
        public decimal?[] DiseaseYtd { get; } = new decimal?[MonthCount];
        public string?[] DiseaseEval { get; } = new string?[MonthCount];
        public decimal?[] FindingAct { get; } = new decimal?[MonthCount];
        public decimal?[] FindingYtd { get; } = new decimal?[MonthCount];
        public string?[] FindingEval { get; } = new string?[MonthCount];
    }
}
