using JpScratch.Proofreading;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// OpenAI（GPT-5.6 Luna）の自動プロンプトキャッシュが、v3相当の長いシステム指示
/// （スタイルガイド＋カスタム指示＋few-shot）で実際に働くかを実APIで確かめる診断コマンド。
///
/// requirements.md 3.5.4の「未確認」は Gemini 固有の明示的キャッシュ（<c>cachedContents</c>）の
/// 最小トークン数・割引単価の話であり、この診断はそれを解決しない（OpenAIの結果からGeminiの値は
/// 分からない）。ここで確かめられるのは別の問い——要件3.5.3が意図する「変化の少ないシステム指示を
/// キャッシュ対象として設計しておく」が、OpenAIの自動キャッシュ（プロバイダ側が暗黙に適用する分。
/// 明示的な作成APIはない）でプロバイダ非依存に達成されているか、という点だけ。
///
/// 実APIを呼ぶため<c>--self-test</c>には絶対に組み込まない。診断であってアプリの利用ではないため、
/// <c>api_calls</c>への書き込みも行わない（課金履歴を汚さない）。
/// </summary>
internal static class OpenAiCacheProbeCommand
{
    internal static async Task<int> RunAsync()
    {
        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("OPENAI_API_KEY が設定されていません。");
            return 2;
        }

        string systemInstruction = ProofreadingPrompt.BuildSystemInstruction(
            SyntheticStyleGuide,
            SyntheticCustomInstruction,
            SyntheticFewShotExamples);
        Console.WriteLine($"システム指示の文字数: {systemInstruction.Length}");

        using HttpClient httpClient = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var client = new OpenAiProofreadingClient(() => apiKey, httpClient);

        const string document = "この文章ア、間違いが二ともある。";
        var request = new ProofreadingRequest(
            SourceStart: 0,
            SourceLength: document.Length,
            SourceText: document,
            BeforeContext: null,
            AfterContext: null,
            ContentHash: "probe",
            ParagraphIndex: 0,
            PartIndex: 0,
            PartCount: 1,
            SystemInstructionOverride: systemInstruction);

        decimal totalCost = 0;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            GeminiProofreadingResult result = await client.ProofreadAsync(request);
            GeminiUsage usage = result.Usage;
            // gpt-5.6-luna.md の標準単価（入力$0.20 / 出力$1.20 per 1M）。この診断はコストの目安表示のみで、
            // pricing.json やapi_calls とは無関係。
            decimal cost =
                usage.PromptTokens / 1_000_000m * 0.20m +
                usage.BillableOutputTokens / 1_000_000m * 1.20m;
            totalCost += cost;
            Console.WriteLine(
                $"[{attempt}回目] input={usage.PromptTokens} cached={usage.CachedContentTokens} " +
                $"output={usage.CandidateTokens} thinking={usage.ThoughtsTokens} " +
                $"${cost:F6} / {result.Elapsed.TotalMilliseconds:F0} ms");
        }

        Console.WriteLine($"推定合計料金: ${totalCost:F6}");
        Console.WriteLine(
            "注記: この診断は Gemini の明示的キャッシュ（cachedContents）の最小トークン数・割引単価には" +
            "回答しません（requirements.md 3.5.4の未確認事項はGemini固有のため）。");
        return 0;
    }

    // 本番のユーザーデータは使わず、合計で数千文字になる合成データでキャッシュ閾値を確実に超えさせる。
    private const string SyntheticStyleGuide =
        """
        このユーザーは体言止めや口語的な語尾を意図的に使うことが多い。「〜だ。」で終わる断定的な文や、
        「〜かな。」のような柔らかい疑問形は、誤りではなく文体なので修正しない。読点の位置がやや独特で、
        接続助詞の直後に読点を置く癖があるが、これも意図した表現として扱う。漢字とひらがなの使い分けは
        「出来る」より「できる」を好む傾向があるが、これは統一ルールではなく傾向なので、既に「出来る」と
        書かれている箇所を「できる」へ書き換えてはいけない。長い文を複数の短文へ分割することも、
        依頼されない限り行わない。
        """;

    private const string SyntheticCustomInstruction =
        """
        固有名詞や製品名らしきカタカナ表記（例:「クロード」「プロンプトバリデーション」）は、
        辞書に無い語であっても誤字として扱わない。半角英数字と全角文字の間のスペース有無も、
        既存の書き方を尊重してどちらかへ統一しない。
        """;

    private static readonly IReadOnlyList<FewShotExample> SyntheticFewShotExamples =
    [
        new("コミニュケーション", "コミュニケーション", ProofreadingReaction.Accept, null),
        new("美容につて", "美容について", ProofreadingReaction.Accept, null),
        new("防腐剤を問ふ", "防腐剤を問う", ProofreadingReaction.Accept, null),
        new("キーぼーぢ", "キーボード", ProofreadingReaction.Accept, null),
        new("行けたら行く", "行く予定はない", ProofreadingReaction.RejectWithReason, "意訳しすぎ"),
        new("〜だと思うんだけどな", "〜だと思います", ProofreadingReaction.RejectWithReason, "文体を変えないでほしい"),
        new("マジで無理", "とても難しい", ProofreadingReaction.RejectWithReason, "口語をそのまま残したい"),
        new("できない訳ではない", "できないわけではない", ProofreadingReaction.Reject, null),
        new("それな", "その通りです", ProofreadingReaction.RejectWithReason, "相槌の口語表現を維持したい"),
        new("食べれる", "食べられる", ProofreadingReaction.RejectWithReason, "ら抜き言葉は意図した表現"),
        new("見れる", "見られる", ProofreadingReaction.RejectWithReason, "ら抜き言葉は意図した表現"),
        new("なんだけど", "なのですが", ProofreadingReaction.Reject, null),
        new("すごい楽しい", "とても楽しい", ProofreadingReaction.Reject, null),
        new("違かった", "違った", ProofreadingReaction.RejectWithReason, "口語の言い回しをそのまま残したい"),
        new("だと思う。", "だと考えられる。", ProofreadingReaction.Reject, null),
    ];
}
