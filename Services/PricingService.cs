using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

internal sealed class ModelPricing
{
    [JsonPropertyName("input_usd_per_1m")]
    public decimal InputUsdPerMillion { get; init; }

    [JsonPropertyName("output_usd_per_1m")]
    public decimal OutputUsdPerMillion { get; init; }

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = "";
}

internal sealed record PricingQuote(
    string Model,
    int PromptTokens,
    int OutputTokens,
    decimal UsdCost,
    ModelPricing Pricing);

/// <summary>
/// pricing.json（要件 3.5.2 / 4）を読み、API応答の実トークン数から料金を計算する。
/// 壊れたファイルは隔離し、既知モデルの既定単価へ安全に戻す。
/// </summary>
internal sealed class PricingService
{
    // 既存コード・検証との互換性を保つため、Geminiを既定モデルとして残す。
    internal const string DefaultModel = ProofreadingModelCatalog.GeminiModel;
    internal const string OpenAiModel = ProofreadingModelCatalog.OpenAiModel;

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

        // 既定モデルが消えると校正そのものが止まるため、他の壊れ方（負値・日付不正・重複）と
        // 同じ扱いで拒否する。
        if (!validated.ContainsKey(DefaultModel))
        {
            throw new InvalidDataException(
                $"既定モデル「{DefaultModel}」の単価は削除できません。");
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
            string json = File.ReadAllText(_pricingFile);
            Dictionary<string, ModelPricing>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, ModelPricing>>(
                    json,
                    JsonOptions);
            _models = Validate(loaded);

            // サポート対象モデルは常に計算可能にする。既存ユーザーの pricing.json に
            // 新モデルが無くても、起動時に既定単価を追加して選択できるようにする。
            bool addedDefault = false;
            if (!_models.ContainsKey(DefaultModel))
            {
                _models[DefaultModel] = CreateDefaultPricing();
                addedDefault = true;
            }
            if (!_models.ContainsKey(OpenAiModel))
            {
                _models[OpenAiModel] = CreateDefaultOpenAiPricing();
                addedDefault = true;
            }
            else if (IsPreviousOpenAiDefaultPricing(_models[OpenAiModel]))
            {
                // 前回のリリースで登録した値だけを公式価格へ更新し、ユーザーが編集した価格は保持する。
                _models[OpenAiModel] = CreateDefaultOpenAiPricing();
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
                pricing.InputUsdPerMillion < 0 ||
                pricing.OutputUsdPerMillion < 0 ||
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

    private static Dictionary<string, ModelPricing> CreateDefaults()
        => new(StringComparer.Ordinal)
        {
            [DefaultModel] = CreateDefaultPricing(),
            [OpenAiModel] = CreateDefaultOpenAiPricing(),
        };

    private static ModelPricing CreateDefaultPricing()
        => new()
        {
            InputUsdPerMillion = 0.30m,
            OutputUsdPerMillion = 2.50m,
            UpdatedAt = "2026-07-29",
        };

    private static ModelPricing CreateDefaultOpenAiPricing()
        => new()
        {
            InputUsdPerMillion = 0.20m,
            OutputUsdPerMillion = 1.20m,
            UpdatedAt = "2026-07-31",
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
