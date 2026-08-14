using System.Globalization;

namespace JpScratch.Models;

/// <summary>
/// 校正で使えるモデル 1 件の不変メタデータ。プロバイダーごとの差異は if 分岐ではなく
/// この表に持たせる（要件 3.5.1 / 3.5.4）。
/// </summary>
/// <param name="AutomaticEffort">
/// 自動用の思考量。意味はプロバイダーごとに異なる（Gemini は thinkingLevel、OpenAI と
/// Anthropic は effort、PLaMo は reasoning_effort）。<c>null</c> は「送らない」。
/// </param>
/// <param name="RecommendedTimeout">
/// 設定画面へ目安として表示するだけの値。ユーザーのタイムアウト設定を上書きしない
/// （モデルを切り替えるたびに設定値が黙って消えるのを避けるため）。
/// </param>
public sealed record ModelDescriptor(
    string Id,
    string DisplayName,
    ApiProvider Provider,
    TimeSpan RecommendedTimeout,
    string? AutomaticEffort,
    string? ManualEffort,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    string Currency,
    string PricingUpdatedAt,
    PromotionalModelPricing? PromotionalPricing = null)
{
    public string? EffortFor(ProofreadingPurpose purpose)
        => purpose == ProofreadingPurpose.Manual ? ManualEffort : AutomaticEffort;

    public EffectiveModelPricing PricingFor(DateOnly utcDate)
    {
        if (PromotionalPricing is { } promotional &&
            utcDate >= promotional.EffectiveFrom &&
            utcDate <= promotional.EndsOn)
        {
            return new EffectiveModelPricing(
                promotional.InputPricePerMillion,
                promotional.OutputPricePerMillion,
                Currency,
                promotional.PricingUpdatedAt);
        }

        return new EffectiveModelPricing(
            InputPricePerMillion,
            OutputPricePerMillion,
            Currency,
            PricingUpdatedAt);
    }

    public IReadOnlyList<CatalogPricingHistoryEntry> PricingHistory()
    {
        if (PromotionalPricing is not { } promotional)
        {
            return
            [
                new CatalogPricingHistoryEntry(
                    DateOnly.ParseExact(
                        PricingUpdatedAt,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    InputPricePerMillion,
                    OutputPricePerMillion,
                    Currency,
                    IsPromotional: false),
            ];
        }

        return
        [
            new CatalogPricingHistoryEntry(
                promotional.EffectiveFrom,
                promotional.InputPricePerMillion,
                promotional.OutputPricePerMillion,
                Currency,
                IsPromotional: true),
            new CatalogPricingHistoryEntry(
                promotional.EndsOn.AddDays(1),
                InputPricePerMillion,
                OutputPricePerMillion,
                Currency,
                IsPromotional: false),
        ];
    }
}

/// <summary>終了日を含む期間限定の標準API単価。</summary>
public sealed record PromotionalModelPricing(
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    string PricingUpdatedAt,
    DateOnly EndsOn)
{
    public DateOnly EffectiveFrom =>
        DateOnly.ParseExact(
            PricingUpdatedAt,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
}

public sealed record CatalogPricingHistoryEntry(
    DateOnly EffectiveFrom,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    string Currency,
    bool IsPromotional);

/// <summary>指定したUTC日付に適用されるモデル単価。</summary>
public sealed record EffectiveModelPricing(
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    string Currency,
    string PricingUpdatedAt);

/// <summary>校正で利用できるAIモデルと、そのAPIプロバイダーの対応。</summary>
public static class ProofreadingModelCatalog
{
    // v2 からの互換用の別名。pricing.json のキーや既存の検証コードが参照する。
    public const string GeminiModel = "gemini-3.5-flash-lite";
    public const string OpenAiModel = "gpt-5.6-luna";

    /// <summary>新規インストール時の既定。入力中の自動校正は高速・低価格のモデルを使う。</summary>
    public const string DefaultAutomaticModel = OpenAiModel;

    /// <summary>新規インストール時の既定。最終仕上がりの確認は品質の高いモデルを使う。</summary>
    public const string DefaultManualModel = "claude-sonnet-5";

    /// <summary>タイムアウト設定が無いときの保険。通常は設定値が使われる。</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromSeconds(300);

    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Medium = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(90);

    private static readonly ModelDescriptor[] Descriptors =
    [
        // ---- OpenAI（Responses API）----
        new("gpt-5.6-sol", "GPT 5.6 Sol", ApiProvider.OpenAi, Slow,
            "low", "medium", 5.00m, 30.00m, "USD", "2026-08-04"),
        new("gpt-5.6-terra", "GPT 5.6 Terra", ApiProvider.OpenAi, Medium,
            "low", "medium", 2.00m, 12.00m, "USD", "2026-08-04"),
        new(OpenAiModel, "GPT 5.6 Luna", ApiProvider.OpenAi, Fast,
            "low", "medium", 0.20m, 1.20m, "USD", "2026-07-31"),

        // ---- Google（generateContent）----
        // thinkingLevel を明示しないと 3.1 Pro は既定 high 思考になり、思考トークンが
        // 出力単価で課金される（要件 3.5.1）。
        new("gemini-3.1-pro-preview", "Gemini 3.1 Pro (Preview)", ApiProvider.Google, Slow,
            "low", "medium", 2.00m, 12.00m, "USD", "2026-08-04"),
        new("gemini-3.6-flash", "Gemini 3.6 Flash", ApiProvider.Google, Medium,
            "low", "medium", 1.50m, 7.50m, "USD", "2026-08-14",
            new(0.75m, 3.75m, "2026-08-14", new DateOnly(2026, 12, 31))),
        new("gemini-3.7-flash", "Gemini 3.7 Flash", ApiProvider.Google, Medium,
            "low", "medium", 1.50m, 7.50m, "USD", "2026-08-14",
            new(0.75m, 3.75m, "2026-08-14", new DateOnly(2026, 12, 31))),
        new(GeminiModel, "Gemini 3.5 Flash Lite", ApiProvider.Google, Fast,
            "low", "medium", 0.30m, 2.50m, "USD", "2026-07-29"),

        // ---- Anthropic（Messages API）----
        // Fable 5 は思考を無効化できない（disabled は 400）。Haiku 4.5 は adaptive 非対応で
        // effort も受け付けないため、effort を送らない（null）。
        new("claude-fable-5", "Claude Fable 5", ApiProvider.Anthropic, Slow,
            "low", "medium", 10.00m, 50.00m, "USD", "2026-08-04"),
        new("claude-opus-5", "Claude Opus 5", ApiProvider.Anthropic, Slow,
            "low", "medium", 5.00m, 25.00m, "USD", "2026-08-04"),
        new(DefaultManualModel, "Claude Sonnet 5", ApiProvider.Anthropic, Medium,
            "low", "medium", 3.00m, 15.00m, "USD", "2026-08-14",
            new(2.00m, 10.00m, "2026-08-14", new DateOnly(2026, 8, 31))),
        new("claude-haiku-4-5-20251001", "Claude Haiku 4.5", ApiProvider.Anthropic, Fast,
            null, null, 1.00m, 5.00m, "USD", "2026-08-04"),

        // ---- Preferred Networks（OpenAI 互換 Chat Completions）----
        // reasoning_effort は none / medium の 2 段階のみ。
        new("plamo-3.0-prime", "PLaMo 3.0 Prime", ApiProvider.PreferredNetworks, Medium,
            "none", "medium", 60m, 250m, "JPY", "2026-08-04"),
    ];

    private static readonly Dictionary<string, ModelDescriptor> ById =
        Descriptors.ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static IReadOnlyList<ModelDescriptor> All { get; } = Descriptors;

    public static IReadOnlyList<string> SupportedModels { get; } =
        [.. Descriptors.Select(d => d.Id)];

    public static bool IsSupported(string? model)
        => model is not null && ById.ContainsKey(model.Trim());

    /// <summary>未知のモデルIDでも落とさず、既定モデルの記述子へ寄せる。</summary>
    public static ModelDescriptor Get(string? model)
        => model is not null && ById.TryGetValue(model.Trim(), out ModelDescriptor? found)
            ? found
            : ById[DefaultAutomaticModel];

    public static bool TryGet(string? model, out ModelDescriptor descriptor)
    {
        if (model is not null && ById.TryGetValue(model.Trim(), out ModelDescriptor? found))
        {
            descriptor = found;
            return true;
        }

        descriptor = ById[DefaultAutomaticModel];
        return false;
    }

    public static ApiProvider ProviderOf(string? model) => Get(model).Provider;

    public static string DisplayName(string model)
        => TryGet(model, out ModelDescriptor descriptor) ? descriptor.DisplayName : model;

    public static EffectiveModelPricing GetEffectivePricing(string? model, DateOnly utcDate)
        => Get(model).PricingFor(utcDate);

    public static string ProviderDisplayName(ApiProvider provider)
        => provider switch
        {
            ApiProvider.Google => "Gemini",
            ApiProvider.OpenAi => "OpenAI",
            ApiProvider.Anthropic => "Anthropic",
            ApiProvider.PreferredNetworks => "PLaMo",
            _ => provider.ToString(),
        };

    /// <summary>プロバイダーごとの API キー環境変数名（要件 3.5.5）。</summary>
    public static string EnvironmentVariableName(ApiProvider provider)
        => provider switch
        {
            ApiProvider.Google => "GEMINI_API_KEY",
            ApiProvider.OpenAi => "OPENAI_API_KEY",
            ApiProvider.Anthropic => "ANTHROPIC_API_KEY",
            ApiProvider.PreferredNetworks => "PLAMO_API_KEY",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

    /// <summary>
    /// Gemini の <c>thinkingConfig.thinkingLevel</c>。Gemini 以外のモデルでは false を返す。
    /// </summary>
    public static bool TryGetGeminiThinkingLevel(
        string? model,
        ProofreadingPurpose purpose,
        out string? level)
    {
        ModelDescriptor descriptor = Get(model);
        level = descriptor.Provider == ApiProvider.Google
            ? descriptor.EffortFor(purpose)
            : null;
        return level is not null;
    }

    /// <summary>
    /// Anthropic で <c>thinking: {"type": "disabled"}</c> を送ってよいか。
    /// Fable 5 は無効化そのものが 400、Haiku 4.5 は adaptive 非対応なのでどちらも送らない。
    /// </summary>
    public static bool SupportsDisabledThinking(string? model)
        => Get(model).Id is "claude-opus-5" or DefaultManualModel;

    /// <summary>Anthropic で adaptive thinking を明示できるモデルか（Haiku 4.5 は非対応）。</summary>
    public static bool SupportsAdaptiveThinking(string? model)
        => Get(model).Id is "claude-fable-5" or "claude-opus-5" or DefaultManualModel;

    /// <summary>
    /// v3 までの単一モデル設定を、自動用・手動用の 2 枠へ移す（要件 3.5.1）。
    /// 既存ユーザーが移行前と同じ挙動で起動できるよう、旧設定の値を両方へコピーする。
    /// <see cref="AppSettings"/> へ依存しない純粋な関数にしてあるので、そのまま検証できる。
    /// </summary>
    /// <returns>移行後の（自動用モデル, 手動用モデル）。旧設定が空、または未知のモデルIDなら引数のまま。</returns>
    public static (string Automatic, string Manual) MigrateLegacyModel(
        string? legacyModel,
        string automatic,
        string manual)
    {
        if (string.IsNullOrWhiteSpace(legacyModel)) return (automatic, manual);
        if (!IsSupported(legacyModel)) return (automatic, manual);

        string migrated = legacyModel.Trim();
        return (migrated, migrated);
    }

    public static TimeSpan ClampTimeout(TimeSpan value)
        => value < MinimumRequestTimeout ? MinimumRequestTimeout
            : value > MaximumRequestTimeout ? MaximumRequestTimeout
                : value;
}
