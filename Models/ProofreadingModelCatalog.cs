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
/// <param name="Efforts">
/// 設定画面で選べる思考量。モデルごとに API が受け付ける値が違い、範囲外は 400 になるため
/// 表に持たせる。<c>null</c> は「指定不可」（Haiku 4.5・OpenAI互換）。
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
    PromotionalModelPricing? PromotionalPricing = null,
    IReadOnlyList<string>? Efforts = null)
{
    public string? EffortFor(ProofreadingPurpose purpose)
        => purpose == ProofreadingPurpose.Manual ? ManualEffort : AutomaticEffort;

    /// <summary>設定画面で選べる思考量（低い順）。空なら思考量を指定できないモデル。</summary>
    public IReadOnlyList<string> EffortChoices => Efforts ?? [];

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
    public const string DefaultAutomaticModel = "gpt-6-luna";

    /// <summary>新規インストール時の既定。比較計測で品質と価格のバランスを確認したモデル。</summary>
    public const string DefaultManualModel = "gpt-6-luna";

    /// <summary>タイムアウト設定が無いときの保険。通常は設定値が使われる。</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromSeconds(300);

    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Medium = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(90);

    // 思考量の選択肢は API が受け付ける値の範囲（公式ドキュメントで確認したもの）。
    // OpenAI は GPT-6 Astra が none、GPT-6.1 Sol が none / minimal を 400 にするため、
    // 全モデルで通る low〜high だけを出す。
    private static readonly string[] LowToHigh = ["low", "medium", "high"];
    private static readonly string[] MinimalToHigh = ["minimal", "low", "medium", "high"];
    private static readonly string[] LowToMax = ["low", "medium", "high", "xhigh", "max"];
    private static readonly string[] NoneOrMedium = ["none", "medium"];

    private static readonly ModelDescriptor[] Descriptors =
    [
        // ---- OpenAI（Responses API）----
        new("gpt-6-astra", "GPT 6 Astra", ApiProvider.OpenAi, Slow,
            "low", "medium", 10.00m, 50.00m, "USD", "2026-09-04", Efforts: LowToHigh),
        new("gpt-6.1-sol", "GPT 6.1 Sol", ApiProvider.OpenAi, Slow,
            "low", "medium", 2.00m, 10.00m, "USD", "2026-10-09", Efforts: LowToHigh),
        new("gpt-6-sol", "GPT 6 Sol", ApiProvider.OpenAi, Slow,
            "low", "medium", 2.00m, 10.00m, "USD", "2026-09-23", Efforts: LowToHigh),
        new("gpt-6-luna", "GPT 6 Luna", ApiProvider.OpenAi, Fast,
            "low", "medium", 0.10m, 0.50m, "USD", "2026-09-23", Efforts: LowToHigh),
        // Sol は「少なくとも 2026-11-21 まで」の割引中で終了日が確定していない。期限付きの
        // PromotionalPricing にすると延長された場合にその日以降を過大見積もりするため、
        // 通常単価として持ち、値上げが公表されたら差し替える。
        new("gpt-5.6-sol", "GPT 5.6 Sol", ApiProvider.OpenAi, Slow,
            "low", "medium", 4.00m, 20.00m, "USD", "2026-08-24", Efforts: LowToHigh),
        new("gpt-5.6-terra", "GPT 5.6 Terra", ApiProvider.OpenAi, Medium,
            "low", "medium", 2.00m, 12.00m, "USD", "2026-08-04", Efforts: LowToHigh),
        new(OpenAiModel, "GPT 5.6 Luna", ApiProvider.OpenAi, Fast,
            "low", "medium", 0.20m, 1.20m, "USD", "2026-07-31", Efforts: LowToHigh),

        // ---- Google（generateContent）----
        // thinkingLevel を明示しないと 3.1 Pro は既定 high 思考になり、思考トークンが
        // 出力単価で課金される（要件 3.5.1）。minimal を受け付けるのは 3.5 / 3.6 系のみ。
        new("gemini-3.1-pro-preview", "Gemini 3.1 Pro (Preview)", ApiProvider.Google, Slow,
            "low", "medium", 2.00m, 12.00m, "USD", "2026-08-04", Efforts: LowToHigh),
        new("gemini-3.6-flash", "Gemini 3.6 Flash", ApiProvider.Google, Medium,
            "low", "medium", 1.50m, 7.50m, "USD", "2026-08-14",
            new(0.75m, 3.75m, "2026-08-14", new DateOnly(2026, 12, 31)), MinimalToHigh),
        new("gemini-3.7-flash", "Gemini 3.7 Flash", ApiProvider.Google, Medium,
            "low", "medium", 1.50m, 7.50m, "USD", "2026-08-14",
            new(0.75m, 3.75m, "2026-08-14", new DateOnly(2026, 12, 31)), LowToHigh),
        new("gemini-3.8-flash", "Gemini 3.8 Flash", ApiProvider.Google, Medium,
            "low", "medium", 1.50m, 7.50m, "USD", "2026-09-04",
            new(0.75m, 3.75m, "2026-09-04", new DateOnly(2026, 12, 31)), LowToHigh),
        new("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite", ApiProvider.Google, Fast,
            "low", "medium", 0.25m, 1.50m, "USD", "2026-03-03", Efforts: LowToHigh),
        new(GeminiModel, "Gemini 3.5 Flash Lite", ApiProvider.Google, Fast,
            "low", "medium", 0.30m, 2.50m, "USD", "2026-07-29", Efforts: MinimalToHigh),

        // ---- Anthropic（Messages API）----
        // Fable 5 は思考を無効化できない（disabled は 400）。Haiku 4.5 は adaptive 非対応で
        // effort も受け付けないため、effort を送らない（null）。
        new("claude-fable-5", "Claude Fable 5", ApiProvider.Anthropic, Slow,
            "low", "medium", 10.00m, 50.00m, "USD", "2026-08-04", Efforts: LowToMax),
        new("claude-fable-5-1", "Claude Fable 5.1", ApiProvider.Anthropic, Slow,
            "low", "medium", 10.00m, 50.00m, "USD", "2026-09-01", Efforts: LowToMax),
        new("claude-opus-5", "Claude Opus 5", ApiProvider.Anthropic, Slow,
            "low", "medium", 5.00m, 25.00m, "USD", "2026-08-04", Efforts: LowToMax),
        new("claude-opus-5-5", "Claude Opus 5.5", ApiProvider.Anthropic, Slow,
            "low", "medium", 4.00m, 20.00m, "USD", "2026-09-22", Efforts: LowToMax),
        // 導入価格の $2 / $10 がそのまま通常価格に確定し、2026-09-01 に予定されていた
        // $3 / $15 への値上げは行われないと公表された。期間限定扱いのままだと 9/1 から
        // 実価格の 1.5 倍で見積もるため、通常単価へ移して PromotionalPricing を外す。
        new("claude-sonnet-5", "Claude Sonnet 5", ApiProvider.Anthropic, Medium,
            "low", "medium", 2.00m, 10.00m, "USD", "2026-08-24", Efforts: LowToMax),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5", ApiProvider.Anthropic, Medium,
            "low", "medium", 2.00m, 10.00m, "USD", "2026-10-09", Efforts: LowToMax),
        // 100K トークン超のプロンプトは $0.50 / $2.50 になるが、校正の 1 リクエストは
        // 2,000 文字以下（要件 3.3.2）で届かないため、基本単価だけを持つ。
        new("claude-haiku-5-5", "Claude Haiku 5.5", ApiProvider.Anthropic, Fast,
            "low", "medium", 0.10m, 0.50m, "USD", "2026-10-09", Efforts: LowToMax),
        new("claude-haiku-4-5-20251001", "Claude Haiku 4.5", ApiProvider.Anthropic, Fast,
            null, null, 1.00m, 5.00m, "USD", "2026-08-04"),

        // ---- Preferred Networks（OpenAI 互換 Chat Completions）----
        // reasoning_effort は none / medium の 2 段階のみ。
        new("plamo-3.0-prime", "PLaMo 3.0 Prime", ApiProvider.PreferredNetworks, Medium,
            "none", "medium", 60m, 250m, "JPY", "2026-08-04", Efforts: NoneOrMedium),
    ];

    private static readonly Dictionary<string, ModelDescriptor> ById =
        Descriptors.ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static IReadOnlyList<ModelDescriptor> All { get; } = Descriptors;

    public static IReadOnlyList<string> SupportedModels { get; } =
        [.. Descriptors.Select(d => d.Id)];

    public static bool IsSupported(string? model)
        => model is not null &&
           (ById.ContainsKey(model.Trim()) || OpenAiCompatibleProfile.TryGetProfileId(model.Trim(), out _));

    /// <summary>未知のモデルIDでも落とさず、既定モデルの記述子へ寄せる。</summary>
    public static ModelDescriptor Get(string? model)
        => model is not null && ById.TryGetValue(model.Trim(), out ModelDescriptor? found)
            ? found
            : OpenAiCompatibleProfile.TryGetProfileId(model, out _)
                ? new ModelDescriptor(model!.Trim(), "OpenAI API互換", ApiProvider.OpenAiCompatible,
                    Medium, null, null, 0m, 0m, "USD", "2026-09-23")
            : ById[DefaultAutomaticModel];

    public static bool TryGet(string? model, out ModelDescriptor descriptor)
    {
        if (model is not null && ById.TryGetValue(model.Trim(), out ModelDescriptor? found))
        {
            descriptor = found;
            return true;
        }

        if (OpenAiCompatibleProfile.TryGetProfileId(model, out _))
        {
            descriptor = Get(model);
            return true;
        }

        descriptor = ById[DefaultAutomaticModel];
        return false;
    }

    public static ApiProvider ProviderOf(string? model) => Get(model).Provider;

    public static string DisplayName(string model)
        => TryGet(model, out ModelDescriptor descriptor) ? descriptor.DisplayName : model;

    /// <summary>
    /// 校正用途では性能を持て余しやすく、継続利用時の料金も大きくなりやすい高価格モデル。
    /// 個別IDではなく単価で判定し、同等価格のモデルが追加されたときも注意表示を漏らさない。
    /// </summary>
    public static bool IsHighCostForProofreading(string? model)
    {
        ModelDescriptor descriptor = Get(model);
        return descriptor.Currency == "USD" &&
               (descriptor.InputPricePerMillion >= 10m ||
                descriptor.OutputPricePerMillion >= 50m);
    }

    public static EffectiveModelPricing GetEffectivePricing(string? model, DateOnly utcDate)
        => Get(model).PricingFor(utcDate);

    public static string ProviderDisplayName(ApiProvider provider)
        => provider switch
        {
            ApiProvider.Google => "Gemini",
            ApiProvider.OpenAi => "OpenAI",
            ApiProvider.Anthropic => "Anthropic",
            ApiProvider.PreferredNetworks => "PLaMo",
            ApiProvider.OpenAiCompatible => "OpenAI API互換",
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
        out string? level,
        string? chosenEffort = null)
    {
        level = Get(model).Provider == ApiProvider.Google
            ? ResolveEffort(model, purpose, chosenEffort)
            : null;
        return level is not null;
    }

    /// <summary>
    /// 設定画面で選ばれた思考量を、そのモデルが受け付ける値に解決する。
    /// 未選択（空）・そのモデルに無い値・指定不可のモデルは、用途別の既定（<see cref="ModelDescriptor.EffortFor"/>）へ戻す。
    /// モデルを切り替えたあとに古い値が残っていても 400 にならないようにする。
    /// </summary>
    public static string? ResolveEffort(string? model, ProofreadingPurpose purpose, string? chosen)
    {
        ModelDescriptor descriptor = Get(model);
        string? trimmed = chosen?.Trim();
        return !string.IsNullOrEmpty(trimmed) &&
               descriptor.EffortChoices.Contains(trimmed, StringComparer.Ordinal)
            ? trimmed
            : descriptor.EffortFor(purpose);
    }

    /// <summary>
    /// 保存する思考量の正規化。そのモデルが受け付けない値・空白だけの値は空文字（＝既定）にする。
    /// 設定ファイルを手で書き換えた場合や、モデルを変えたあとに古い値が残った場合の保険。
    /// </summary>
    public static string NormalizeEffort(string? model, string? effort)
    {
        string trimmed = effort?.Trim() ?? "";
        return Get(model).EffortChoices.Contains(trimmed, StringComparer.Ordinal) ? trimmed : "";
    }

    /// <summary>
    /// Anthropic で <c>thinking: {"type": "disabled"}</c> を送ってよいか。
    /// Fable 5・Opus 5.5 は無効化そのものが 400、Haiku 4.5 は adaptive 非対応、
    /// Sonnet 5.5 は <c>disabled</c> の代わりに <c>between_tools</c> を使うので、いずれも送らない。
    /// </summary>
    public static bool SupportsDisabledThinking(string? model)
        => Get(model).Id is "claude-opus-5" or "claude-sonnet-5" or "claude-haiku-5-5";

    /// <summary>Anthropic で adaptive thinking を明示できるモデルか（Haiku 4.5 は非対応）。</summary>
    public static bool SupportsAdaptiveThinking(string? model)
        => Get(model).Id is "claude-fable-5" or "claude-fable-5-1" or "claude-opus-5" or "claude-opus-5-5"
            or "claude-sonnet-5" or "claude-sonnet-5-5" or "claude-haiku-5-5";

    /// <summary>
    /// 自動用で思考を最小にするために Anthropic へ送る <c>thinking.type</c>。送れないときは <c>null</c>。
    /// どのモデルも <c>xhigh</c> / <c>max</c> では思考を切ると 400 になるため、その場合は送らない
    /// （思考量を上げた時点で「思考を切る」指定は意味を失う）。
    /// </summary>
    public static string? ThinkingOffType(string? model, string? effort)
    {
        if (effort is "xhigh" or "max") return null;
        if (SupportsDisabledThinking(model)) return "disabled";
        return Get(model).Id == "claude-sonnet-5-5" ? "between_tools" : null;
    }

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
