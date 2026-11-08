using System.Text.Encodings.Web;
using System.Text.Json;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// モデル比較ベンチマークの計算部分をAPIなしで検査する。
///
/// 一番の狙いは通貨の取り違え。pricing.json のキー名は <c>input_usd_per_1m</c> だが、
/// PLaMo にはそこへ ¥60 / ¥250 が入る。<see cref="PricingQuote.ToUsd"/> を通さずに
/// <see cref="PricingQuote.Cost"/> を集計すると PLaMo だけ約 150 倍高く見え、
/// 「安いモデル」と「高いモデル」の順位が入れ替わった図が出る。
/// </summary>
internal static class ModelBenchmarkValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static bool RunSelfTests()
    {
        bool passed = true;
        passed &= RunCurrencyTests();
        passed &= RunProtectionTests();
        passed &= RunMedianTests();
        passed &= RunSummaryTests();
        passed &= RunRoundTripTest();
        return passed;
    }

    private static bool RunCurrencyTests()
    {
        string directory = CreateTempDirectory();
        try
        {
            PricingService pricing = new(Path.Combine(directory, "pricing.json"));

            // USD 建て（Gemini 3.5 Flash Lite: 入力 $0.30 / 出力 $2.50 per 1M）。
            PricingQuote usd = pricing.Calculate(
                ProofreadingModelCatalog.GeminiModel, 1_000_000, 1_000_000);
            (decimal? usdCost, bool usdKnown) = ModelBenchmark.ToUsdCost(usd, usdJpyRate: null);
            bool usdPass = usdKnown && usdCost == 2.80m;

            // 円建て（PLaMo 3.0 Prime: 入力 ¥60 / 出力 ¥250 per 1M）。
            PricingQuote jpy = pricing.Calculate("plamo-3.0-prime", 1_000_000, 1_000_000);
            bool nativePass = jpy.Cost == 310m && jpy.Currency == PricingCurrency.Jpy;

            (decimal? convertedCost, bool convertedKnown) =
                ModelBenchmark.ToUsdCost(jpy, usdJpyRate: 155m);
            bool convertedPass = convertedKnown &&
                                 convertedCost is { } value &&
                                 Math.Abs(value - (310m / 155m)) < 0.000001m;

            // レートが無ければ換算しない。ここで 310 をそのまま USD として通すのが最悪の失敗。
            (decimal? unknownCost, bool unknownKnown) =
                ModelBenchmark.ToUsdCost(jpy, usdJpyRate: null);
            bool unknownPass = !unknownKnown && unknownCost is null;

            Console.WriteLine($"ベンチマーク料金（USD建てはそのまま）: {Verdict(usdPass)}");
            Console.WriteLine($"ベンチマーク料金（円建ての生値）: {Verdict(nativePass)}");
            Console.WriteLine($"ベンチマーク料金（円建てをレートでUSDへ）: {Verdict(convertedPass)}");
            Console.WriteLine($"ベンチマーク料金（レート無しは未確認扱い）: {Verdict(unknownPass)}");
            return usdPass && nativePass && convertedPass && unknownPass;
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static bool RunProtectionTests()
    {
        // 閉じタグは単体ではなく前後を含めた語で保護する。"</document>" だけを守ると、
        // 逃がしが往復せず "\</document>" で返ってきても部分一致してしまい検出できない。
        string[] mustNotChange = ["「防腐剤を問ふする」", "たとえば </document> のような閉じタグ"];

        const string intact = "原稿の「防腐剤を問ふする」は誤りです。たとえば </document> のような閉じタグも本文です。";
        bool intactPass = ModelBenchmark.FindProtectionViolations(mustNotChange, intact).Count == 0;

        // 引用内の誤字を「直して」しまった場合。
        const string corrected = "原稿の「防腐剤を塗布する」は誤りです。たとえば </document> のような閉じタグも本文です。";
        IReadOnlyList<string> violations =
            ModelBenchmark.FindProtectionViolations(mustNotChange, corrected);
        bool violationPass = violations.Count == 1 && violations[0] == "「防腐剤を問ふする」";

        // 閉じタグの逃がしが往復していない場合（\</document> のまま返る）。
        const string escaped = "原稿の「防腐剤を問ふする」は誤りです。たとえば \\</document> のような閉じタグも本文です。";
        IReadOnlyList<string> escapeViolations =
            ModelBenchmark.FindProtectionViolations(mustNotChange, escaped);
        bool escapePass = escapeViolations.Count == 1 &&
                          escapeViolations[0] == "たとえば </document> のような閉じタグ";

        // 安全検査で破棄された・失敗した場合は判定できないので空。違反0件と区別できるよう、
        // 集計側では AcceptedText が null の試行を分母から外す（RunSummaryTests 参照）。
        bool nullPass = ModelBenchmark.FindProtectionViolations(mustNotChange, null).Count == 0;

        Console.WriteLine($"ベンチマーク保護判定（そのまま残る）: {Verdict(intactPass)}");
        Console.WriteLine($"ベンチマーク保護判定（引用内を直した）: {Verdict(violationPass)}");
        Console.WriteLine($"ベンチマーク保護判定（閉じタグの逃がしが戻っていない）: {Verdict(escapePass)}");
        Console.WriteLine($"ベンチマーク保護判定（未判定はnull）: {Verdict(nullPass)}");
        return intactPass && violationPass && escapePass && nullPass;
    }

    private static bool RunMedianTests()
    {
        bool oddPass = ModelBenchmark.Median(new double[] { 3, 1, 2 }) == 2;
        bool evenPass = ModelBenchmark.Median(new double[] { 4, 1, 3, 2 }) == 2.5;
        bool emptyPass = ModelBenchmark.Median(Array.Empty<double>()) is null;
        bool decimalOddPass = ModelBenchmark.Median(new decimal[] { 0.3m, 0.1m, 0.2m }) == 0.2m;
        bool decimalEvenPass = ModelBenchmark.Median(new decimal[] { 0.4m, 0.1m }) == 0.25m;

        Console.WriteLine($"ベンチマーク中央値（奇数件）: {Verdict(oddPass)}");
        Console.WriteLine($"ベンチマーク中央値（偶数件は平均）: {Verdict(evenPass)}");
        Console.WriteLine($"ベンチマーク中央値（0件はnull）: {Verdict(emptyPass)}");
        Console.WriteLine($"ベンチマーク中央値（decimalの奇数件）: {Verdict(decimalOddPass)}");
        Console.WriteLine($"ベンチマーク中央値（decimalの偶数件）: {Verdict(decimalEvenPass)}");
        return oddPass && evenPass && emptyPass && decimalOddPass && decimalEvenPass;
    }

    private static bool RunSummaryTests()
    {
        BenchmarkModelInfo[] models =
        [
            new("model-a", "Model A", "OpenAI", "medium", 1m, 2m, "USD", "2026-08-04"),
            new("model-b", "Model B", "PLaMo", "medium", 60m, 250m, "JPY", "2026-08-04"),
        ];
        BenchmarkTextInfo[] texts =
        [
            new("plain", "混在", 100, 0),
            new("quoted-typo", "引用内の意図的な誤字", 100, 2),
        ];

        BenchmarkTrialResult[] trials =
        [
            Success("model-a", "plain", 1, 1000, 0.01m, costKnown: true, changes: 3, accepted: true),
            Success("model-a", "plain", 2, 3000, 0.03m, costKnown: true, changes: 5, accepted: true),
            // 直しすぎて安全検査に弾かれた試行。AcceptedText が無いので保護判定の分母に入らない。
            Rejected("model-a", "quoted-typo", 1),
            Violated("model-a", "quoted-typo", 2, ["「防腐剤を問ふする」"]),
            // 円建てでレートが取れず料金が未確認の試行。
            Success("model-b", "plain", 1, 2000, null, costKnown: false, changes: 1, accepted: true),
        ];

        IReadOnlyList<BenchmarkModelSummary> summaries =
            ModelBenchmark.Summarize(models, texts, trials);

        BenchmarkModelSummary a = summaries.Single(s => s.ModelId == "model-a");
        BenchmarkModelSummary b = summaries.Single(s => s.ModelId == "model-b");

        // 破棄された試行も成功（応答は返っている）として数え、所要時間の中央値には入る。
        bool elapsedPass = a.MedianElapsedMs == 2000 && a.MinElapsedMs == 1000 && a.MaxElapsedMs == 3000;
        // 料金は破棄された試行も含めた4件（0.01 / 0.03 / 0.02 / 0.02）で集計する。
        // 安全検査で破棄されても API は課金するので、合計から外してはいけない。
        bool costPass = a.MedianCostUsd == 0.02m && a.TotalCostUsd == 0.08m && a.CostUsdKnown;
        bool rejectedPass = a.RejectedCount == 1;

        BenchmarkProtectionSummary protection =
            a.Protection.Single(p => p.TextId == "quoted-typo");
        bool protectionPass = protection.JudgedTrials == 1 &&
                              protection.CleanTrials == 0 &&
                              protection.ViolationCount == 1;
        // mustNotChange を持たない文章は保護の対象に入れない。
        bool protectionScopePass = a.Protection.Count == 1;

        // 1件でも未確認があればモデル全体の料金を「確認できていない」として扱う。
        bool unknownPass = !b.CostUsdKnown && b.MedianCostUsd is null && b.TotalCostUsd is null;

        Console.WriteLine($"ベンチマーク集計（所要時間の中央値・最小・最大）: {Verdict(elapsedPass)}");
        Console.WriteLine($"ベンチマーク集計（料金の中央値と合計）: {Verdict(costPass)}");
        Console.WriteLine($"ベンチマーク集計（安全検査での破棄を計上）: {Verdict(rejectedPass)}");
        Console.WriteLine($"ベンチマーク集計（保護違反の件数）: {Verdict(protectionPass)}");
        Console.WriteLine($"ベンチマーク集計（保護対象は mustNotChange のある文章だけ）: {Verdict(protectionScopePass)}");
        Console.WriteLine($"ベンチマーク集計（料金未確認のモデル）: {Verdict(unknownPass)}");
        return elapsedPass && costPass && rejectedPass && protectionPass &&
               protectionScopePass && unknownPass;
    }

    private static bool RunRoundTripTest()
    {
        BenchmarkModelInfo[] models =
            [new("model-a", "Model A", "OpenAI", "medium", 1m, 2m, "USD", "2026-08-04")];
        BenchmarkTextInfo[] texts = [new("quoted-typo", "引用内の意図的な誤字", 100, 1)];
        BenchmarkTrialResult[] trials =
            [Violated("model-a", "quoted-typo", 1, ["「防腐剤を問ふする」"])];

        BenchmarkReport report = new(
            "2026-08-06T00:00:00.0000000+09:00",
            "2026-08-06T00:10:00.0000000+09:00",
            "Manual",
            3,
            120,
            "0123456789abcdef",
            new BenchmarkFxRate(155m, "2026-08-05", "2026-08-06T00:00:00.0000000+09:00"),
            models,
            [new BenchmarkSkippedProvider("PLaMo", "PLAMO_API_KEY が未設定", ["plamo-3.0-prime"])],
            texts,
            0.04m,
            StoppedByBudget: false,
            ModelBenchmark.Summarize(models, texts, trials),
            trials);

        string json = JsonSerializer.Serialize(report, JsonOptions);
        BenchmarkReport? restored = JsonSerializer.Deserialize<BenchmarkReport>(json, JsonOptions);

        bool pass = restored is not null &&
                    restored.TrialCount == 3 &&
                    restored.FxRate?.UsdJpy == 155m &&
                    restored.SkippedProviders.Count == 1 &&
                    restored.Trials.Count == 1 &&
                    restored.Trials[0].ProtectionViolations.Count == 1 &&
                    restored.Trials[0].ProtectionViolations[0] == "「防腐剤を問ふする」" &&
                    restored.Summary.Count == 1 &&
                    restored.Summary[0].Protection.Single().ViolationCount == 1 &&
                    // 日本語と山括弧をエスケープせずに書き出す（人が読める JSON にする）。
                    json.Contains("引用内の意図的な誤字", StringComparison.Ordinal);

        Console.WriteLine($"ベンチマークJSONの往復: {Verdict(pass)}");
        return pass;
    }

    private static BenchmarkTrialResult Success(
        string modelId,
        string textId,
        int trial,
        double elapsedMs,
        decimal? costUsd,
        bool costKnown,
        int changes,
        bool accepted)
        => new(
            modelId, textId, trial, true, elapsedMs, 1,
            1000, 200, 300, 0, 500, 1500,
            costUsd, costKnown, costUsd, "USD",
            accepted, null, changes, 0.05,
            "適用後の本文", true, [],
            null, null, null);

    private static BenchmarkTrialResult Rejected(string modelId, string textId, int trial)
        => new(
            modelId, textId, trial, true, 2000, 1,
            1000, 200, 300, 0, 500, 1500,
            0.02m, true, 0.02m, "USD",
            false, "変更比率が大きすぎます", 0, 0.9,
            null, null, [],
            null, null, null);

    private static BenchmarkTrialResult Violated(
        string modelId,
        string textId,
        int trial,
        IReadOnlyList<string> violations)
        => new(
            modelId, textId, trial, true, 2000, 1,
            1000, 200, 300, 0, 500, 1500,
            0.02m, true, 0.02m, "USD",
            true, null, 2, 0.05,
            "適用後の本文", true, violations,
            null, null, null);

    private static string Verdict(bool passed) => passed ? "PASS" : "FAIL";

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "jpscratch-benchmark-validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
