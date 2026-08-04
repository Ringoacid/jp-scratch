using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

internal sealed class ModelPricing
{
    /// <summary>
    /// 単価の通貨（要件 3.5.2）。省略時は USD として読むので、v3 までの pricing.json は
    /// そのまま使える。PLaMo だけが JPY。
    /// キー名の <c>_usd_</c> は既存ファイルとの互換のために残しており、実際の通貨はこの欄が決める。
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = PricingCurrency.Usd;

    [JsonPropertyName("input_usd_per_1m")]
    public decimal InputUsdPerMillion { get; init; }

    [JsonPropertyName("output_usd_per_1m")]
    public decimal OutputUsdPerMillion { get; init; }

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = "";
}

internal static class PricingCurrency
{
    internal const string Usd = "USD";
    internal const string Jpy = "JPY";

    internal static bool IsSupported(string? currency)
        => currency is Usd or Jpy;
}

internal sealed record PricingQuote(
    string Model,
    int PromptTokens,
    int OutputTokens,
    decimal Cost,
    string Currency,
    ModelPricing Pricing)
{
    internal bool IsUsd => string.Equals(Currency, PricingCurrency.Usd, StringComparison.Ordinal);

    /// <summary>
    /// USD 建てへ換算する。円建てモデルはレートが無ければ換算できないため <c>null</c> を返す
    /// （推測レートで換算して誤った金額を記録しないため。要件 3.5.2）。
    /// </summary>
    internal decimal? ToUsd(decimal? usdJpyRate)
        => IsUsd ? Cost
            : usdJpyRate is > 0m ? Cost / usdJpyRate.Value
                : null;
}

/// <summary>
/// pricing.json（要件 3.5.2 / 4）を読み、API応答の実トークン数から料金を計算する。
/// 壊れたファイルは隔離し、既知モデルの既定単価へ安全に戻す。
/// </summary>
internal sealed class PricingService
{
    // 既存コード・検証との互換性を保つため、Geminiを既定モデルとして残す。
    internal const string DefaultModel = ProofreadingModelCatalog.GeminiModel;
    internal const string OpenAiModel = ProofreadingModelCatalog.OpenAiModel;

    /// <summary>
    /// 単価（USD/1M tokens）の上限。実在するモデルよりはるかに大きいが、
    /// int.MaxValue トークン × この単価でも decimal のオーバーフロー（最大約 7.9e28）を
    /// 起こさない範囲。上限がないと、設定画面で巨大な値を保存したときに
    /// <see cref="Calculate"/> が OverflowException を投げて校正が落ちる。
    /// </summary>
    internal const decimal MaxUnitPriceUsdPerMillion = 1_000_000_000m;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _pricingFile;
    private Dictionary<string, ModelPricing> _models =
        new(StringComparer.Ordinal);

    internal PricingService(string? pricingFile = null)
    {
        _pricingFile = pricingFile ?? AppPaths.PricingFile;
        Load();
    }

    internal ModelPricing GetPricing(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("モデルIDが空です。", nameof(model));

        return _models.TryGetValue(model.Trim(), out ModelPricing? pricing)
            ? pricing
            : throw new KeyNotFoundException(
                $"モデル「{model.Trim()}」の単価がpricing.jsonにありません。");
    }

    /// <summary>現在の単価表のスナップショット（設定画面の読み込み用）。</summary>
    internal IReadOnlyDictionary<string, ModelPricing> Snapshot() =>
        new Dictionary<string, ModelPricing>(_models, StringComparer.Ordinal);

    /// <summary>
    /// 単価表を丸ごと差し替えて保存する。設定画面からの明示的な操作専用であり、
    /// <see cref="TrySave"/> と違ってIO例外を握りつぶさず呼び出し側へ投げる
    /// （保存できていないのに成功したように見せない）。検証・書き込みに失敗した場合は
    /// メモリ上の単価を一切変えない（部分適用を作らない）。
    /// </summary>
    internal void Replace(IReadOnlyDictionary<string, ModelPricing> models)
    {
        Dictionary<string, ModelPricing> validated =
            Validate(new Dictionary<string, ModelPricing>(models, StringComparer.Ordinal));

        // カタログ収録モデルの単価が消えると、そのモデルを選んだ瞬間に校正が止まる。
        // 他の壊れ方（負値・日付不正・重複）と同じ扱いで拒否する。
        foreach (string model in ProofreadingModelCatalog.SupportedModels)
        {
            if (!validated.ContainsKey(model))
            {
                throw new InvalidDataException(
                    $"モデル「{model}」の単価は削除できません。");
            }
        }

        AtomicFile.WriteAllText(
            _pricingFile,
            JsonSerializer.Serialize(validated, JsonOptions));

        // 書き込みに成功したときにだけメモリ上の単価を更新する。
        _models = validated;
    }

    internal PricingQuote Calculate(
        string model,
        int promptTokens,
        int outputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(promptTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        ModelPricing pricing = GetPricing(model);
        decimal cost =
            promptTokens / 1_000_000m * pricing.InputUsdPerMillion +
            outputTokens / 1_000_000m * pricing.OutputUsdPerMillion;
        return new PricingQuote(
            model.Trim(),
            promptTokens,
            outputTokens,
            cost,
            pricing.Currency,
            pricing);
    }

    private void Load()
    {
        if (!File.Exists(_pricingFile))
        {
            _models = CreateDefaults();
            TrySave();
            return;
        }

        try
        {
            // strict UTF-8 で読む。既定のデコーダは不正バイトを U+FFFD へ黙って置換するため、
            // CP932 で保存された pricing.json がモデルIDの化けた状態で「読めた」ことになり、
            // 直後の TrySave（既定モデルの補完）で化けたまま書き戻されてユーザーの単価が失われる。
            if (!AtomicFile.TryReadAllText(_pricingFile, out string json))
                throw new InvalidDataException("モデル単価ファイルを読み込めませんでした。");
            Dictionary<string, ModelPricing>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, ModelPricing>>(
                    json,
                    JsonOptions);
            _models = Validate(loaded);

            // サポート対象モデルは常に計算可能にする。既存ユーザーの pricing.json に
            // 新モデルが無くても、起動時に既定単価を追加して選択できるようにする。
            bool addedDefault = false;
            foreach (ModelDescriptor descriptor in ProofreadingModelCatalog.All)
            {
                if (_models.ContainsKey(descriptor.Id)) continue;
                _models[descriptor.Id] = CreateDefaultPricing(descriptor);
                addedDefault = true;
            }

            // v3 のリリースで登録した OpenAI の旧単価だけを公式価格へ更新する一度きりの移行。
            // 一般の仕組みではないので、新しいモデルではこの手当てを増やさない
            // （ユーザーが編集した価格は保持する）。
            if (_models.TryGetValue(OpenAiModel, out ModelPricing? openAi) &&
                IsPreviousOpenAiDefaultPricing(openAi))
            {
                _models[OpenAiModel] = CreateDefaultPricing(
                    ProofreadingModelCatalog.Get(OpenAiModel));
                addedDefault = true;
            }

            if (addedDefault) TrySave();
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException
                or InvalidDataException)
        {
            QuarantineBrokenFile();
            _models = CreateDefaults();
            TrySave();
        }
    }

    private static Dictionary<string, ModelPricing> Validate(
        Dictionary<string, ModelPricing>? models)
    {
        if (models is null || models.Count == 0)
            throw new InvalidDataException("モデル単価が1件もありません。");

        var validated = new Dictionary<string, ModelPricing>(
            StringComparer.Ordinal);
        foreach ((string model, ModelPricing? pricing) in models)
        {
            if (string.IsNullOrWhiteSpace(model) || pricing is null ||
                !PricingCurrency.IsSupported(pricing.Currency) ||
                pricing.InputUsdPerMillion < 0 ||
                pricing.InputUsdPerMillion > MaxUnitPriceUsdPerMillion ||
                pricing.OutputUsdPerMillion < 0 ||
                pricing.OutputUsdPerMillion > MaxUnitPriceUsdPerMillion ||
                !DateOnly.TryParseExact(
                    pricing.UpdatedAt,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                throw new InvalidDataException("モデル単価の形式が不正です。");
            }

            string normalizedModel = model.Trim();
            if (!validated.TryAdd(normalizedModel, pricing))
                throw new InvalidDataException("モデルIDが重複しています。");
        }

        return validated;
    }

    /// <summary>既定単価はモデル記述子の表が正典（要件 3.5.4）。ここに数値を持たない。</summary>
    private static Dictionary<string, ModelPricing> CreateDefaults()
        => ProofreadingModelCatalog.All.ToDictionary(
            descriptor => descriptor.Id,
            CreateDefaultPricing,
            StringComparer.Ordinal);

    private static ModelPricing CreateDefaultPricing(ModelDescriptor descriptor)
        => new()
        {
            Currency = descriptor.Currency,
            InputUsdPerMillion = descriptor.InputPricePerMillion,
            OutputUsdPerMillion = descriptor.OutputPricePerMillion,
            UpdatedAt = descriptor.PricingUpdatedAt,
        };

    private static bool IsPreviousOpenAiDefaultPricing(ModelPricing pricing)
        => pricing.InputUsdPerMillion == 1.00m &&
           pricing.OutputUsdPerMillion == 6.00m &&
           pricing.UpdatedAt == "2026-07-31";

    private void TrySave()
    {
        try
        {
            AtomicFile.WriteAllText(
                _pricingFile,
                JsonSerializer.Serialize(_models, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 単価ファイルを書けなくても、既定値による料金表示と本文編集は継続する。
        }
    }

    private void QuarantineBrokenFile()
    {
        try
        {
            string bad = _pricingFile + ".bad";
            if (File.Exists(bad))
            {
                bad += "." + DateTime.Now.ToString(
                    "yyyyMMddHHmmssfff",
                    CultureInfo.InvariantCulture);
            }
            File.Move(_pricingFile, bad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 隔離できなくても、メモリ上では既定値へ戻してアプリを継続する。
        }
    }
}
