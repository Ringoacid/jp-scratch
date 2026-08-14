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

    /// <summary>
    /// true のときは保存値をユーザー編集値として固定せず、モデルカタログのUTC日付別単価を使う。
    /// 旧pricing.jsonにこの項目は無いため、既定値falseで後方互換を保つ。
    /// </summary>
    [JsonPropertyName("catalog_managed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CatalogManaged { get; init; }
}

internal enum PricingEventType
{
    Override,
    UseCatalog,
}

internal sealed record PricingEvent(
    DateOnly EffectiveFrom,
    PricingEventType Type,
    decimal? InputPricePerMillion = null,
    decimal? OutputPricePerMillion = null);

internal sealed record PricingHistoryEntry(
    string Model,
    DateOnly EffectiveFrom,
    string Currency,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    bool IsCatalog,
    bool IsPromotional,
    bool IsEffective,
    PricingEventType? UserEventType = null);

internal sealed class PricingDocument
{
    [JsonPropertyName("version")] public int Version { get; init; } = 5;
    [JsonPropertyName("models")] public Dictionary<string, PricingModelHistory> Models { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class PricingModelHistory
{
    [JsonPropertyName("currency")] public string Currency { get; init; } = PricingCurrency.Usd;
    [JsonPropertyName("events")] public List<PersistedPricingEvent> Events { get; init; } = [];
}

internal sealed class PersistedPricingEvent
{
    [JsonPropertyName("type")] public string Type { get; init; } = "override";
    [JsonPropertyName("effective_from")] public string EffectiveFrom { get; init; } = "";
    [JsonPropertyName("input_usd_per_1m")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? InputPricePerMillion { get; init; }
    [JsonPropertyName("output_usd_per_1m")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? OutputPricePerMillion { get; init; }
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
    internal const int CurrentVersion = 5;
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
    private readonly Func<DateOnly> _utcTodayProvider;
    private Dictionary<string, ModelPricing> _models =
        new(StringComparer.Ordinal);
    private Dictionary<string, PricingModelHistory> _history =
        new(StringComparer.Ordinal);

    internal PricingService(
        string? pricingFile = null,
        Func<DateOnly>? utcTodayProvider = null)
    {
        _pricingFile = pricingFile ?? AppPaths.PricingFile;
        _utcTodayProvider = utcTodayProvider ??
            (() => DateOnly.FromDateTime(DateTime.UtcNow));
        Load();
    }

    internal ModelPricing GetPricing(string model)
    {
        if (_history.Count > 0) return GetPricing(model, _utcTodayProvider());
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("モデルIDが空です。", nameof(model));

        string normalized = model.Trim();
        if (!_models.TryGetValue(normalized, out ModelPricing? pricing))
        {
            throw new KeyNotFoundException(
                $"モデル「{normalized}」の単価がpricing.jsonにありません。");
        }

        if (!pricing.CatalogManaged ||
            !ProofreadingModelCatalog.TryGet(normalized, out ModelDescriptor descriptor))
        {
            return pricing;
        }

        return CreateDefaultPricing(descriptor, _utcTodayProvider());
    }

    internal ModelPricing GetPricing(string model, DateOnly utcDate)
    {
        string normalized = NormalizeModel(model);
        if (!_history.TryGetValue(normalized, out PricingModelHistory? history))
            throw new KeyNotFoundException($"モデル「{normalized}」の単価がpricing.jsonにありません。");
        PricingEvent? e = UserEvents(normalized)
            .LastOrDefault(x => x.EffectiveFrom <= utcDate);
        if (e is { Type: PricingEventType.Override })
            return EventPricing(history.Currency, e, false);

        if (ProofreadingModelCatalog.TryGet(normalized, out ModelDescriptor descriptor))
        {
            return CreateDefaultPricing(descriptor, utcDate);
        }

        throw new KeyNotFoundException(
            $"カスタムモデル「{normalized}」には{utcDate:yyyy-MM-dd}時点の単価がありません。");
    }

    internal IReadOnlyList<PricingEvent> GetUserEvents(string model)
        => UserEvents(NormalizeModel(model));

    internal IReadOnlyList<PricingHistoryEntry> GetHistory(string model)
    {
        string id = NormalizeModel(model);
        if (!_history.ContainsKey(id)) throw new KeyNotFoundException($"モデル「{id}」の単価がありません。");
        var rows = new List<PricingHistoryEntry>();
        if (ProofreadingModelCatalog.TryGet(id, out ModelDescriptor d))
            foreach (CatalogPricingHistoryEntry c in d.PricingHistory())
            {
                PricingEvent? controlling = UserEvents(id)
                    .LastOrDefault(e => e.EffectiveFrom <= c.EffectiveFrom);
                rows.Add(new(id, c.EffectiveFrom, c.Currency, c.InputPricePerMillion, c.OutputPricePerMillion,
                    true, c.IsPromotional,
                    controlling is null || controlling.Type == PricingEventType.UseCatalog));
            }
        foreach (PricingEvent e in UserEvents(id))
        {
            ModelPricing p = GetPricing(id, e.EffectiveFrom);
            rows.Add(new(id, e.EffectiveFrom, p.Currency, p.InputUsdPerMillion, p.OutputUsdPerMillion,
                false, false, true, e.Type));
        }
        return rows.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.IsCatalog ? 0 : 1).ToArray();
    }

    internal IReadOnlyList<PricingHistoryEntry> GetPricingHistory(string model) => GetHistory(model);

    internal void ReplaceUserEvents(string model, IReadOnlyList<PricingEvent> events)
    {
        string id = NormalizeModel(model);
        if (!_history.ContainsKey(id)) throw new KeyNotFoundException($"モデル「{id}」の単価がありません。");
        List<PricingEvent> valid = ValidateEvents(_history[id].Currency, events);
        var candidate = new Dictionary<string, PricingModelHistory>(_history, StringComparer.Ordinal)
        {
            [id] = ToHistory(_history[id].Currency, valid)
        };
        SaveHistory(candidate);
    }

    internal void AddOverride(string model, DateOnly date, decimal input, decimal output)
    {
        var events = GetUserEvents(model).ToList();
        events.Add(new(date, PricingEventType.Override, input, output));
        ReplaceUserEvents(model, events);
    }

    internal void AddUseCatalog(string model, DateOnly date)
    {
        var events = GetUserEvents(model).ToList();
        events.Add(new(date, PricingEventType.UseCatalog));
        ReplaceUserEvents(model, events);
    }

    /// <summary>現在の単価表のスナップショット（設定画面の読み込み用）。</summary>
    internal IReadOnlyDictionary<string, ModelPricing> Snapshot() =>
        SnapshotSafeModels();

    internal IReadOnlyDictionary<string, IReadOnlyList<PricingEvent>> SnapshotUserEvents() =>
        _history.Keys.ToDictionary(
            model => model,
            model => (IReadOnlyList<PricingEvent>)UserEvents(model).ToArray(),
            StringComparer.Ordinal);

    internal void ReplaceAllUserEvents(
        IReadOnlyDictionary<string, IReadOnlyList<PricingEvent>> eventsByModel)
    {
        var candidate = new Dictionary<string, PricingModelHistory>(StringComparer.Ordinal);
        foreach ((string model, PricingModelHistory current) in _history)
        {
            IReadOnlyList<PricingEvent> supplied = eventsByModel.TryGetValue(model, out var events)
                ? events
                : UserEvents(model);
            candidate[model] = ToHistory(
                current.Currency,
                ValidateEvents(current.Currency, supplied));
        }
        if (eventsByModel.Keys.Any(model => !candidate.ContainsKey(model)))
            throw new InvalidDataException("登録されていないモデルの料金履歴は保存できません。");
        SaveHistory(candidate);
    }

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

        SaveHistory(MigrateLegacy(validated));
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
        if (TryLoadV5()) return;
        if (!File.Exists(_pricingFile))
        {
            _models = CreateDefaults();
            _history = MigrateLegacy(_models);
            TrySaveHistory();
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
            foreach (ModelDescriptor descriptor in ProofreadingModelCatalog.All)
            {
                if (_models.ContainsKey(descriptor.Id)) continue;
                _models[descriptor.Id] = CreateDefaultPricing(
                    descriptor,
                    _utcTodayProvider());
            }

            // v3 のリリースで登録した OpenAI の旧単価だけを公式価格へ更新する一度きりの移行。
            // 一般の仕組みではないので、新しいモデルではこの手当てを増やさない
            // （ユーザーが編集した価格は保持する）。
            if (_models.TryGetValue(OpenAiModel, out ModelPricing? openAi) &&
                IsPreviousOpenAiDefaultPricing(openAi))
            {
                _models[OpenAiModel] = CreateDefaultPricing(
                    ProofreadingModelCatalog.Get(OpenAiModel),
                    _utcTodayProvider());
            }

            // v4初期版は期間限定価格を意図的に通常価格で登録していた。値と更新日がその既定値に
            // 完全一致する場合だけカタログ管理へ移し、ユーザーが編集した単価は固定値のまま保持する。
            foreach (string model in new[] { "gemini-3.6-flash", "claude-sonnet-5" })
            {
                if (_models.TryGetValue(model, out ModelPricing? pricing) &&
                    IsPreviousPromotionalModelDefault(model, pricing))
                {
                    _models[model] = CreateDefaultPricing(
                        ProofreadingModelCatalog.Get(model),
                        _utcTodayProvider());
                }
            }

            _history = MigrateLegacy(_models);
            TrySaveHistory();
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException
                or InvalidDataException)
        {
            QuarantineBrokenFile();
            _models = CreateDefaults();
            _history = MigrateLegacy(_models);
            TrySaveHistory();
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
    private Dictionary<string, ModelPricing> CreateDefaults()
        => ProofreadingModelCatalog.All.ToDictionary(
            descriptor => descriptor.Id,
            descriptor => CreateDefaultPricing(descriptor, _utcTodayProvider()),
            StringComparer.Ordinal);

    private static ModelPricing CreateDefaultPricing(
        ModelDescriptor descriptor,
        DateOnly utcDate)
    {
        EffectiveModelPricing effective = descriptor.PricingFor(utcDate);
        return new ModelPricing
        {
            Currency = effective.Currency,
            InputUsdPerMillion = effective.InputPricePerMillion,
            OutputUsdPerMillion = effective.OutputPricePerMillion,
            UpdatedAt = effective.PricingUpdatedAt,
            CatalogManaged = true,
        };
    }

    private static bool IsPreviousOpenAiDefaultPricing(ModelPricing pricing)
        => pricing.InputUsdPerMillion == 1.00m &&
           pricing.OutputUsdPerMillion == 6.00m &&
           pricing.UpdatedAt == "2026-07-31";

    private static bool IsPreviousPromotionalModelDefault(
        string model,
        ModelPricing pricing)
    {
        if (pricing.CatalogManaged ||
            pricing.Currency != PricingCurrency.Usd ||
            pricing.UpdatedAt != "2026-08-04")
        {
            return false;
        }

        return model switch
        {
            "gemini-3.6-flash" =>
                pricing.InputUsdPerMillion == 1.50m &&
                pricing.OutputUsdPerMillion == 7.50m,
            "claude-sonnet-5" =>
                pricing.InputUsdPerMillion == 3.00m &&
                pricing.OutputUsdPerMillion == 15.00m,
            _ => false,
        };
    }

    private bool TryLoadV5()
    {
        if (!File.Exists(_pricingFile)) return false;

        try
        {
            if (!AtomicFile.TryReadAllText(_pricingFile, out string json))
                throw new InvalidDataException("モデル単価ファイルを読み込めませんでした。");
            using JsonDocument parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("version", out JsonElement version) ||
                version.GetInt32() != CurrentVersion)
            {
                return false;
            }

            PricingDocument? document =
                JsonSerializer.Deserialize<PricingDocument>(json, JsonOptions);
            _history = ValidateHistoryDocument(document);
            bool added = false;
            foreach (ModelDescriptor descriptor in ProofreadingModelCatalog.All)
            {
                if (_history.ContainsKey(descriptor.Id)) continue;
                _history[descriptor.Id] = ToHistory(descriptor.Currency, []);
                added = true;
            }
            _models = SnapshotSafeModels();
            if (added) TrySaveHistory();
            return true;
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException
                or InvalidDataException or FormatException)
        {
            QuarantineBrokenFile();
            _models = CreateDefaults();
            _history = MigrateLegacy(_models);
            TrySaveHistory();
            return true;
        }
    }

    private static Dictionary<string, PricingModelHistory> ValidateHistoryDocument(
        PricingDocument? document)
    {
        if (document is null || document.Version != CurrentVersion ||
            document.Models.Count == 0)
            throw new InvalidDataException("モデル単価履歴の形式が不正です。");

        var validated = new Dictionary<string, PricingModelHistory>(StringComparer.Ordinal);
        foreach ((string rawModel, PricingModelHistory? history) in document.Models)
        {
            string model = NormalizeModel(rawModel);
            if (history is null || !PricingCurrency.IsSupported(history.Currency))
                throw new InvalidDataException("モデル単価履歴の通貨が不正です。");
            List<PricingEvent> events = ValidateEvents(
                history.Currency,
                ParseEvents(history));
            if (!validated.TryAdd(model, ToHistory(history.Currency, events)))
                throw new InvalidDataException("モデルIDが重複しています。");
        }
        return validated;
    }

    private Dictionary<string, PricingModelHistory> MigrateLegacy(
        IReadOnlyDictionary<string, ModelPricing> legacy)
    {
        var migrated = new Dictionary<string, PricingModelHistory>(StringComparer.Ordinal);
        foreach ((string model, ModelPricing pricing) in legacy)
        {
            bool catalogManaged = pricing.CatalogManaged ||
                IsCatalogEquivalent(model, pricing) ||
                (model == OpenAiModel && IsPreviousOpenAiDefaultPricing(pricing)) ||
                IsPreviousPromotionalModelDefault(model, pricing);
            List<PricingEvent> events = [];
            if (!catalogManaged)
            {
                events.Add(new PricingEvent(
                    ParseDate(pricing.UpdatedAt),
                    PricingEventType.Override,
                    pricing.InputUsdPerMillion,
                    pricing.OutputUsdPerMillion));
            }
            migrated[model] = ToHistory(pricing.Currency, events);
        }

        foreach (ModelDescriptor descriptor in ProofreadingModelCatalog.All)
        {
            if (!migrated.ContainsKey(descriptor.Id))
                migrated[descriptor.Id] = ToHistory(descriptor.Currency, []);
        }
        return migrated;
    }

    private static bool IsCatalogEquivalent(string model, ModelPricing pricing)
    {
        if (!ProofreadingModelCatalog.TryGet(model, out ModelDescriptor descriptor))
            return false;
        DateOnly date = ParseDate(pricing.UpdatedAt);
        EffectiveModelPricing effective = descriptor.PricingFor(date);
        return pricing.Currency == effective.Currency &&
               pricing.InputUsdPerMillion == effective.InputPricePerMillion &&
               pricing.OutputUsdPerMillion == effective.OutputPricePerMillion;
    }

    private static List<PricingEvent> ParseEvents(PricingModelHistory history)
    {
        var events = new List<PricingEvent>();
        foreach (PersistedPricingEvent persisted in history.Events)
        {
            PricingEventType type = persisted.Type switch
            {
                "override" => PricingEventType.Override,
                "use_catalog" => PricingEventType.UseCatalog,
                _ => throw new InvalidDataException(
                    $"料金イベント種別「{persisted.Type}」は利用できません。"),
            };
            events.Add(new PricingEvent(
                ParseDate(persisted.EffectiveFrom),
                type,
                persisted.InputPricePerMillion,
                persisted.OutputPricePerMillion));
        }
        return events.OrderBy(entry => entry.EffectiveFrom).ToList();
    }

    private IReadOnlyList<PricingEvent> UserEvents(string model)
    {
        if (!_history.TryGetValue(model, out PricingModelHistory? history))
            throw new KeyNotFoundException($"モデル「{model}」の単価がありません。");
        return ParseEvents(history);
    }

    private static List<PricingEvent> ValidateEvents(
        string currency,
        IEnumerable<PricingEvent> source)
    {
        if (!PricingCurrency.IsSupported(currency))
            throw new InvalidDataException("モデル単価履歴の通貨が不正です。");

        var validated = new List<PricingEvent>();
        var dates = new HashSet<DateOnly>();
        foreach (PricingEvent entry in source.OrderBy(entry => entry.EffectiveFrom))
        {
            if (!dates.Add(entry.EffectiveFrom))
                throw new InvalidDataException(
                    $"適用開始日「{entry.EffectiveFrom:yyyy-MM-dd}」が重複しています。");

            if (entry.Type == PricingEventType.Override)
            {
                if (entry.InputPricePerMillion is not decimal input ||
                    entry.OutputPricePerMillion is not decimal output ||
                    input < 0 || input > MaxUnitPriceUsdPerMillion ||
                    output < 0 || output > MaxUnitPriceUsdPerMillion)
                    throw new InvalidDataException("ユーザー単価の値が不正です。");
            }
            else if (entry.InputPricePerMillion is not null ||
                     entry.OutputPricePerMillion is not null)
            {
                throw new InvalidDataException(
                    "公式価格へ戻すイベントには単価を指定できません。");
            }
            validated.Add(entry);
        }
        return validated;
    }

    private static PricingModelHistory ToHistory(
        string currency,
        IEnumerable<PricingEvent> events)
        => new()
        {
            Currency = currency,
            Events = events
                .OrderBy(entry => entry.EffectiveFrom)
                .Select(entry => new PersistedPricingEvent
                {
                    Type = entry.Type == PricingEventType.Override
                        ? "override"
                        : "use_catalog",
                    EffectiveFrom = entry.EffectiveFrom.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    InputPricePerMillion = entry.Type == PricingEventType.Override
                        ? entry.InputPricePerMillion
                        : null,
                    OutputPricePerMillion = entry.Type == PricingEventType.Override
                        ? entry.OutputPricePerMillion
                        : null,
                })
                .ToList(),
        };

    private static ModelPricing EventPricing(
        string currency,
        PricingEvent entry,
        bool catalogManaged)
        => new()
        {
            Currency = currency,
            InputUsdPerMillion = entry.InputPricePerMillion!.Value,
            OutputUsdPerMillion = entry.OutputPricePerMillion!.Value,
            UpdatedAt = entry.EffectiveFrom.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            CatalogManaged = catalogManaged,
        };

    private static string NormalizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("モデルIDが空です。", nameof(model));
        return model.Trim();
    }

    private static DateOnly ParseDate(string value)
    {
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
            throw new InvalidDataException($"日付「{value}」の形式が不正です。");
        return date;
    }

    private Dictionary<string, ModelPricing> SnapshotSafeModels()
    {
        var models = new Dictionary<string, ModelPricing>(StringComparer.Ordinal);
        foreach (string model in _history.Keys)
        {
            try
            {
                models[model] = GetPricing(model);
            }
            catch (KeyNotFoundException)
            {
                PricingModelHistory history = _history[model];
                PricingEvent? first = ParseEvents(history)
                    .FirstOrDefault(entry => entry.Type == PricingEventType.Override);
                if (first is not null)
                    models[model] = EventPricing(history.Currency, first, false);
            }
        }
        return models;
    }

    private void SaveHistory(Dictionary<string, PricingModelHistory> candidate)
    {
        foreach (string model in ProofreadingModelCatalog.SupportedModels)
        {
            if (!candidate.ContainsKey(model))
                throw new InvalidDataException($"モデル「{model}」の単価は削除できません。");
        }

        var document = new PricingDocument { Models = candidate };
        AtomicFile.WriteAllText(
            _pricingFile,
            JsonSerializer.Serialize(document, JsonOptions));
        _history = candidate;
        _models = SnapshotSafeModels();
    }

    private void TrySaveHistory()
    {
        try
        {
            SaveHistory(new Dictionary<string, PricingModelHistory>(
                _history,
                StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくてもメモリ上の単価で継続する。
        }
    }

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
