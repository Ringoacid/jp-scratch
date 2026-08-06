using JpScratch.Services;

namespace JpScratch.PromptValidation;

// モデル比較ベンチマークの出力形式と、そこに載る値を作る純粋関数。
// 実APIを呼ぶ ModelBenchmarkCommand から計算部分だけを切り出してあるので、
// --self-test（無課金・無キー）から ModelBenchmarkValidation で検証できる。

/// <summary>benchmark-texts.json の1件。</summary>
internal sealed record BenchmarkText(
    string Id,
    string Kind,
    string Description,
    string Text,
    IReadOnlyList<string> MustNotChange);

/// <summary>実行時のモデル1件のメタデータ。単価は実行時点のスナップショットを固定保存する。</summary>
internal sealed record BenchmarkModelInfo(
    string Id,
    string DisplayName,
    string Provider,
    string? Effort,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    string Currency,
    string PricingUpdatedAt);

internal sealed record BenchmarkFxRate(decimal UsdJpy, string RateDate, string FetchedAt);

internal sealed record BenchmarkSkippedProvider(
    string Provider,
    string Reason,
    IReadOnlyList<string> Models);

internal sealed record BenchmarkTextInfo(
    string Id,
    string Kind,
    int Length,
    int MustNotChangeCount);

/// <summary>1 モデル × 1 文章 × 1 試行の観測値。失敗も同じ形で残す。</summary>
internal sealed record BenchmarkTrialResult(
    string ModelId,
    string TextId,
    int Trial,
    bool Succeeded,
    double? ElapsedMs,
    int? Attempts,
    int? PromptTokens,
    int? CandidateTokens,
    int? ThoughtsTokens,
    int? CachedTokens,
    int? BillableOutputTokens,
    int? TotalTokens,
    decimal? CostUsd,
    bool CostUsdKnown,
    decimal? CostNative,
    string? CostCurrency,
    bool? Accepted,
    string? RejectionReason,
    int? ChangeCount,
    double? ChangedRatio,
    string? AcceptedText,
    bool? AcceptedTextMatchesCorrected,
    IReadOnlyList<string> ProtectionViolations,
    string? ErrorKind,
    int? ErrorStatusCode,
    string? ErrorMessage);

/// <summary>1 文章あたりの保護判定。mustNotChange を持つ文章だけが対象。</summary>
internal sealed record BenchmarkProtectionSummary(
    string TextId,
    int CleanTrials,
    int JudgedTrials,
    int ViolationCount);

internal sealed record BenchmarkModelSummary(
    string ModelId,
    string DisplayName,
    string Provider,
    int SuccessCount,
    int FailureCount,
    double? MedianElapsedMs,
    double? MinElapsedMs,
    double? MaxElapsedMs,
    decimal? MedianCostUsd,
    decimal? TotalCostUsd,
    bool CostUsdKnown,
    double? MeanChangeCount,
    int RejectedCount,
    IReadOnlyList<BenchmarkProtectionSummary> Protection);

internal sealed record BenchmarkReport(
    string RunStartedAt,
    string RunFinishedAt,
    string Purpose,
    int TrialCount,
    int TimeoutSeconds,
    string SystemInstructionSha256,
    BenchmarkFxRate? FxRate,
    IReadOnlyList<BenchmarkModelInfo> Models,
    IReadOnlyList<BenchmarkSkippedProvider> SkippedProviders,
    IReadOnlyList<BenchmarkTextInfo> Texts,
    decimal TotalCostUsd,
    bool StoppedByBudget,
    IReadOnlyList<BenchmarkModelSummary> Summary,
    IReadOnlyList<BenchmarkTrialResult> Trials);

internal static class ModelBenchmark
{
    /// <summary>
    /// 見積もり用の概算係数。日本語では 1 文字がおおむね 1 トークン前後になるという粗い前提で、
    /// 実行前の確認に出す桁だけを合わせる。実際の課金には一切使わない（実測の usage を使う）。
    /// </summary>
    internal const double EstimatedTokensPerChar = 0.9;

    /// <summary>思考トークンの概算。effort=medium で数百〜千程度を見込む。</summary>
    internal const int EstimatedThinkingTokens = 800;

    /// <summary>
    /// USD 建てのコストへ寄せる。円建てモデル（PLaMo）はレートが無ければ換算せず
    /// <c>(null, false)</c> を返す。推測レートで埋めると、キー名が <c>input_usd_per_1m</c> の
    /// まま ¥60 が入っている PLaMo の単価が USD として集計され、桁が 2 つずれる。
    /// </summary>
    internal static (decimal? CostUsd, bool Known) ToUsdCost(PricingQuote quote, decimal? usdJpyRate)
    {
        decimal? converted = quote.ToUsd(usdJpyRate);
        return converted is { } value ? (value, true) : (null, false);
    }

    /// <summary>
    /// 「触ってはいけない文字列」のうち、全提案を承諾した本文から消えたものを返す。
    /// 引用内の意図的な誤字を直してしまった場合と、本文中の指示に従って別物を返した場合の
    /// どちらもここで検出できる。<paramref name="acceptedText"/> が null（安全検査で破棄された、
    /// または失敗した）ときは判定できないので空を返す。
    /// </summary>
    internal static IReadOnlyList<string> FindProtectionViolations(
        IReadOnlyList<string> mustNotChange,
        string? acceptedText)
    {
        if (acceptedText is null || mustNotChange.Count == 0) return [];

        List<string> violations = [];
        foreach (string protectedText in mustNotChange)
        {
            if (string.IsNullOrEmpty(protectedText)) continue;
            if (!acceptedText.Contains(protectedText, StringComparison.Ordinal))
                violations.Add(protectedText);
        }

        return violations;
    }

    /// <summary>要素数が偶数なら中央 2 件の平均。空なら null。</summary>
    internal static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>料金は decimal のまま扱う（double へ落とすと桁が消える）。</summary>
    internal static decimal? Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return null;
        decimal[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2m;
    }

    internal static IReadOnlyList<BenchmarkModelSummary> Summarize(
        IReadOnlyList<BenchmarkModelInfo> models,
        IReadOnlyList<BenchmarkTextInfo> texts,
        IReadOnlyList<BenchmarkTrialResult> trials)
    {
        string[] protectedTextIds =
            [.. texts.Where(text => text.MustNotChangeCount > 0).Select(text => text.Id)];

        List<BenchmarkModelSummary> summaries = [];
        foreach (BenchmarkModelInfo model in models)
        {
            BenchmarkTrialResult[] own =
                [.. trials.Where(trial => trial.ModelId == model.Id)];
            if (own.Length == 0) continue;

            BenchmarkTrialResult[] succeeded = [.. own.Where(trial => trial.Succeeded)];
            double[] elapsed =
                [.. succeeded.Where(t => t.ElapsedMs is not null).Select(t => t.ElapsedMs!.Value)];
            decimal[] costs =
                [.. own.Where(t => t.CostUsdKnown && t.CostUsd is not null).Select(t => t.CostUsd!.Value)];

            // 料金が1件でも未確認（円建てでレートが取れない）なら、合計は「分かる範囲の合計」に
            // すぎないので、その事実を欄で持たせる。図と表では未確認を数値として扱わない。
            bool costKnown = own.All(t => !t.Succeeded || t.CostUsdKnown);

            int[] changeCounts =
                [.. succeeded.Where(t => t.ChangeCount is not null).Select(t => t.ChangeCount!.Value)];

            List<BenchmarkProtectionSummary> protection = [];
            foreach (string textId in protectedTextIds)
            {
                BenchmarkTrialResult[] judged =
                    [.. own.Where(t => t.TextId == textId && t.AcceptedText is not null)];
                protection.Add(new BenchmarkProtectionSummary(
                    textId,
                    judged.Count(t => t.ProtectionViolations.Count == 0),
                    judged.Length,
                    judged.Sum(t => t.ProtectionViolations.Count)));
            }

            summaries.Add(new BenchmarkModelSummary(
                model.Id,
                model.DisplayName,
                model.Provider,
                succeeded.Length,
                own.Length - succeeded.Length,
                Median(elapsed),
                elapsed.Length == 0 ? null : elapsed.Min(),
                elapsed.Length == 0 ? null : elapsed.Max(),
                Median(costs),
                costs.Length == 0 ? null : costs.Sum(),
                costKnown,
                changeCounts.Length == 0 ? null : changeCounts.Average(),
                succeeded.Count(t => t.Accepted == false),
                protection));
        }

        return summaries;
    }
}
