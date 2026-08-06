using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Proofreading;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// カタログ収録の全モデルへ同じ日本語文章を送り、所要時間・料金・全提案を承諾した結果を
/// 1 つの JSON へ残す比較ベンチマーク。
///
/// 実APIを呼んで課金が発生するため <c>--self-test</c> には絶対に組み込まない
/// （<see cref="OpenAiCacheProbeCommand"/> と同じ理由。self-test が無課金・無キーで動く前提を壊す）。
/// 計測であってアプリの利用ではないので <c>api_calls</c> へも書き込まない（課金履歴と月間上限を汚さない）。
/// 単価と為替も隔離ディレクトリのファイルで扱い、%APPDATA%\JpScratch には一切触れない。
/// </summary>
internal static class ModelBenchmarkCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 全モデル共通のタイムアウト。モデルごとの推奨値（15〜90秒）をそのまま使うと、
    /// 遅いモデルほど打ち切られやすくなり、比較したいはずの所要時間の軸が歪む。
    /// </summary>
    private const int DefaultTimeoutSeconds = 120;

    internal static async Task<int> RunAsync(string[] args)
    {
        BenchmarkOptions options;
        try
        {
            options = BenchmarkOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"エラー: {exception.Message}");
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        IReadOnlyList<BenchmarkText> allTexts = LoadTexts();
        IReadOnlyList<BenchmarkText> texts = options.TextIds is { Count: > 0 }
            ? [.. allTexts.Where(text => options.TextIds.Contains(text.Id, StringComparer.Ordinal))]
            : allTexts;
        if (texts.Count == 0)
        {
            Console.Error.WriteLine("対象の文章がありません。--texts の指定を確認してください。");
            return 2;
        }

        IReadOnlyList<ModelDescriptor> requested = options.ModelIds is { Count: > 0 }
            ? [.. ProofreadingModelCatalog.All.Where(
                d => options.ModelIds.Contains(d.Id, StringComparer.Ordinal))]
            : ProofreadingModelCatalog.All;
        if (requested.Count == 0)
        {
            Console.Error.WriteLine("対象のモデルがありません。--models の指定を確認してください。");
            return 2;
        }

        string systemInstruction = ProofreadingPrompt.BuildSystemInstruction(
            styleGuide: null,
            customInstruction: null,
            fewShotExamples: []);
        string systemInstructionHash = Sha256Hex(systemInstruction);

        Console.WriteLine("=== JP Scratch 校正モデル比較ベンチマーク ===");
        Console.WriteLine($"データディレクトリ（本体と一致している必要がある）: {DataDirectory()}");
        Console.WriteLine($"システム指示: {systemInstruction.Length} 文字 / sha256 {systemInstructionHash[..16]}…");
        Console.WriteLine($"用途: 手動（ManualEffort） / 試行 {options.Trials} 回 / " +
                          $"タイムアウト {options.TimeoutSeconds} 秒（全モデル共通）");
        Console.WriteLine();

        // ---- APIキーの解決（1円も使う前に、取れないプロバイダーを確定させる）----
        CredentialService credentials = new();
        Dictionary<ApiProvider, string> keys = [];
        List<BenchmarkSkippedProvider> skipped = [];
        foreach (ApiProvider provider in requested.Select(d => d.Provider).Distinct())
        {
            string environmentName = ProofreadingModelCatalog.EnvironmentVariableName(provider);
            string providerName = ProofreadingModelCatalog.ProviderDisplayName(provider);
            string? key = Environment.GetEnvironmentVariable(environmentName);
            string source = environmentName;
            if (string.IsNullOrWhiteSpace(key))
            {
                // 環境変数が無い場合だけ、本体が DPAPI で保存したキーへ落とす。
                key = credentials.GetApiKey(provider, ApiKeySource.Stored);
                source = "credentials.dat";
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                string[] excluded = [.. requested.Where(d => d.Provider == provider).Select(d => d.Id)];
                string reason =
                    $"{environmentName} が未設定で、credentials.dat も " +
                    $"{credentials.StoredKeyState(provider)} のため取得できませんでした。";
                skipped.Add(new BenchmarkSkippedProvider(providerName, reason, excluded));
                Console.WriteLine($"  × {providerName}: {reason}");
                Console.WriteLine($"     → 除外するモデル: {string.Join(", ", excluded)}");
                continue;
            }

            keys[provider] = key.Trim();
            Console.WriteLine($"  ○ {providerName}: {source} から取得");
        }

        IReadOnlyList<ModelDescriptor> models =
            [.. requested.Where(d => keys.ContainsKey(d.Provider))];
        if (models.Count == 0)
        {
            Console.Error.WriteLine("APIキーを取得できたプロバイダーがありません。");
            return 2;
        }

        // ---- 隔離ディレクトリ（実データの pricing.json / app.db を書き換えない）----
        string scratchDirectory = Path.Combine(
            Path.GetTempPath(),
            "jpscratch-model-benchmark",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        int exitCode;
        try
        {
            exitCode = await RunBenchmarkAsync(
                options, texts, models, keys, skipped,
                systemInstruction, systemInstructionHash, scratchDirectory);
        }
        finally
        {
            TryDeleteDirectory(scratchDirectory);
        }

        return exitCode;
    }

    private static async Task<int> RunBenchmarkAsync(
        BenchmarkOptions options,
        IReadOnlyList<BenchmarkText> texts,
        IReadOnlyList<ModelDescriptor> models,
        IReadOnlyDictionary<ApiProvider, string> keys,
        IReadOnlyList<BenchmarkSkippedProvider> skipped,
        string systemInstruction,
        string systemInstructionHash,
        string scratchDirectory)
    {
        PricingService pricing = new(Path.Combine(scratchDirectory, "pricing.json"));

        // 為替も隔離した app.db に取りに行く。実データの fx_rates / app_metadata を触ると、
        // 本体側の「当日1回だけ試行」の判定を消費してしまう。
        FxRate? fxRate = null;
        using (Database scratchDatabase = new(Path.Combine(scratchDirectory, "app.db")))
        using (HttpClient fxHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan })
        // 本体の 5 秒は入力中の校正を待たせないための値。ここは1回限りの計測なので、
        // 初回接続が遅くて取り逃し、円建てモデルの料金が丸ごと「未確認」になる方が損。
        using (FxRateService fx = new(
                   scratchDatabase, fxHttpClient, requestTimeout: TimeSpan.FromSeconds(15)))
        {
            fxRate = await fx.EnsureTodayAsync();
        }

        if (fxRate is null)
        {
            string[] unpriced =
            [
                .. models
                    .Where(d => pricing.GetPricing(d.Id).Currency == PricingCurrency.Jpy)
                    .Select(d => d.Id)
            ];
            Console.WriteLine(
                "  ! USD/JPY レートを取得できませんでした。円建てモデルの料金は「未確認」として" +
                "記録し、推測レートでは埋めません。");
            if (unpriced.Length > 0)
            {
                // 未確認の料金は --max-cost の累計に足せない。ループ自体はモデル×文章×試行で
                // 上限があるので青天井にはならず、超過しうるのは対象モデルの計画分だけ。
                Console.WriteLine(
                    $"     → {string.Join(", ", unpriced)} の実費は --max-cost の累計に含まれません" +
                    "（超過しうるのはこのモデルの計画分のみ）。");
            }
        }
        else
        {
            Console.WriteLine(
                $"  USD/JPY = {fxRate.UsdJpy} （基準日 {fxRate.RateDate:yyyy-MM-dd}）");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"計測対象: {models.Count} モデル × {texts.Count} 文章 × {options.Trials} 試行 = " +
            $"{models.Count * texts.Count * options.Trials} リクエスト");

        decimal estimate = EstimateTotalCostUsd(
            models, texts, options.Trials, systemInstruction.Length, pricing, fxRate?.UsdJpy);
        Console.WriteLine(
            $"概算費用: 約 ${estimate:F2}（実測ではなく文字数からの粗い見積もり）/ " +
            $"上限 ${options.MaxCostUsd:F2}");

        if (!options.AssumeYes)
        {
            Console.Write("実行しますか？ 課金が発生します [y/N]: ");
            string? answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("中止しました。");
                return 0;
            }
        }

        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        TimeSpan timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        List<HttpClient> httpClients = [];
        Dictionary<ApiProvider, HttpClient> httpByProvider = [];
        Dictionary<string, IProofreadingClient> clients = [];
        string runStartedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        List<BenchmarkTrialResult> results = [];
        decimal spentUsd = 0;
        bool stoppedByBudget = false;

        try
        {
            foreach (ModelDescriptor descriptor in models)
            {
                if (!httpByProvider.TryGetValue(descriptor.Provider, out HttpClient? http))
                {
                    // タイムアウトは ProofreadingClientBase が自前の CTS で掛ける。HttpClient 側の
                    // 既定 100 秒が先に効くと、120 秒設定でも 100 秒で切れる。
                    http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                    httpClients.Add(http);
                    httpByProvider[descriptor.Provider] = http;
                }

                clients[descriptor.Id] = CreateClient(
                    descriptor, keys[descriptor.Provider], http, timeout);
            }

            // 文章でインターリーブする（試行 → 文章 → モデル）。モデル単位でまとめると、
            // 通信が遅い時間帯にたまたま当たったモデルだけが systematically 不利になる。
            for (int trial = 1; trial <= options.Trials && !stoppedByBudget; trial++)
            {
                foreach (BenchmarkText text in texts)
                {
                    // 上限の判定は文章の切れ目でだけ行い、モデルの列は必ず最後まで回す。
                    // 途中で抜けると、カタログ順で後ろにいるモデルだけ試行数が減り、
                    // 「モデルごとに母数の違う中央値」が図に出てしまう。
                    if (spentUsd >= options.MaxCostUsd)
                    {
                        Console.WriteLine(
                            $"費用上限 ${options.MaxCostUsd:F2} に達したため中断します。");
                        stoppedByBudget = true;
                        break;
                    }

                    foreach (ModelDescriptor descriptor in models)
                    {
                        BenchmarkTrialResult result = await RunOneAsync(
                            clients[descriptor.Id],
                            descriptor,
                            text,
                            trial,
                            systemInstruction,
                            pricing,
                            fxRate?.UsdJpy,
                            cancellation.Token);
                        results.Add(result);
                        if (result.CostUsdKnown && result.CostUsd is { } cost)
                            spentUsd += cost;
                        PrintTrial(descriptor, text, trial, result, spentUsd);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("中断しました。ここまでの結果を保存します。");
        }
        finally
        {
            foreach (IProofreadingClient client in clients.Values) client.Dispose();
            foreach (HttpClient http in httpClients) http.Dispose();
        }

        BenchmarkModelInfo[] modelInfos =
        [
            .. models.Select(descriptor =>
            {
                ModelPricing modelPricing = pricing.GetPricing(descriptor.Id);
                return new BenchmarkModelInfo(
                    descriptor.Id,
                    descriptor.DisplayName,
                    ProofreadingModelCatalog.ProviderDisplayName(descriptor.Provider),
                    descriptor.EffortFor(ProofreadingPurpose.Manual),
                    modelPricing.InputUsdPerMillion,
                    modelPricing.OutputUsdPerMillion,
                    modelPricing.Currency,
                    modelPricing.UpdatedAt);
            })
        ];
        BenchmarkTextInfo[] textInfos =
        [
            .. texts.Select(text => new BenchmarkTextInfo(
                text.Id, text.Kind, text.Text.Length, text.MustNotChange.Count))
        ];

        BenchmarkReport report = new(
            runStartedAt,
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            "Manual",
            options.Trials,
            options.TimeoutSeconds,
            systemInstructionHash,
            fxRate is null
                ? null
                : new BenchmarkFxRate(
                    fxRate.UsdJpy,
                    fxRate.RateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    fxRate.FetchedAt.ToString("O", CultureInfo.InvariantCulture)),
            modelInfos,
            skipped,
            textInfos,
            spentUsd,
            stoppedByBudget,
            ModelBenchmark.Summarize(modelInfos, textInfos, results),
            results);

        string outputPath = Path.GetFullPath(options.OutputPath ?? DefaultOutputPath(options.Trials));
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, JsonOptions));

        PrintSummary(report);
        Console.WriteLine($"レポート: {outputPath}");
        return results.Any(result => result.Succeeded) ? 0 : 1;
    }

    private static async Task<BenchmarkTrialResult> RunOneAsync(
        IProofreadingClient client,
        ModelDescriptor descriptor,
        BenchmarkText text,
        int trial,
        string systemInstruction,
        PricingService pricing,
        decimal? usdJpyRate,
        CancellationToken cancellationToken)
    {
        ProofreadingRequest request = new(
            SourceStart: 0,
            SourceLength: text.Text.Length,
            SourceText: text.Text,
            BeforeContext: null,
            AfterContext: null,
            ContentHash: "benchmark",
            ParagraphIndex: 0,
            PartIndex: 0,
            PartCount: 1,
            SystemInstructionOverride: systemInstruction);

        try
        {
            GeminiProofreadingResult result = await client.ProofreadAsync(request, cancellationToken);
            GeminiUsage usage = result.Usage;
            PricingQuote quote = pricing.Calculate(
                descriptor.Id, usage.PromptTokens, usage.BillableOutputTokens);
            (decimal? costUsd, bool costKnown) = ModelBenchmark.ToUsdCost(quote, usdJpyRate);

            string? acceptedText = null;
            bool? matchesCorrected = null;
            string? rejectionReason = result.Diff.RejectionReason;
            if (result.Diff.Accepted)
            {
                try
                {
                    // 「全ての提案を承諾した結果」の正典。CorrectedText をそのまま使わないのは、
                    // 提案として提示される差分の集合がユーザーの見るものだから。
                    acceptedText = DocumentDiff.Apply(text.Text, result.Diff.Changes);
                    matchesCorrected = string.Equals(
                        acceptedText, result.CorrectedText, StringComparison.Ordinal);
                }
                catch (InvalidOperationException exception)
                {
                    rejectionReason = $"提案の適用に失敗: {exception.Message}";
                }
            }

            return new BenchmarkTrialResult(
                descriptor.Id, text.Id, trial,
                Succeeded: true,
                result.Elapsed.TotalMilliseconds,
                result.Attempts,
                usage.PromptTokens,
                usage.CandidateTokens,
                usage.ThoughtsTokens,
                usage.CachedContentTokens,
                usage.BillableOutputTokens,
                usage.TotalTokens,
                costUsd,
                costKnown,
                quote.Cost,
                quote.Currency,
                result.Diff.Accepted,
                rejectionReason,
                result.Diff.Changes.Count,
                result.Diff.ChangedRatio,
                acceptedText,
                matchesCorrected,
                ModelBenchmark.FindProtectionViolations(text.MustNotChange, acceptedText),
                ErrorKind: null,
                ErrorStatusCode: null,
                ErrorMessage: null);
        }
        catch (GeminiClientException exception)
        {
            // HTTP 200 の後で応答を採用できなかった場合は使用量が分かる＝課金されている。
            decimal? costUsd = null;
            bool costKnown = false;
            decimal? costNative = null;
            string? currency = null;
            if (exception.Usage is { } usage)
            {
                PricingQuote quote = pricing.Calculate(
                    descriptor.Id, usage.PromptTokens, usage.BillableOutputTokens);
                (costUsd, costKnown) = ModelBenchmark.ToUsdCost(quote, usdJpyRate);
                costNative = quote.Cost;
                currency = quote.Currency;
            }

            return new BenchmarkTrialResult(
                descriptor.Id, text.Id, trial,
                Succeeded: false,
                exception.Elapsed?.TotalMilliseconds,
                Attempts: null,
                exception.Usage?.PromptTokens,
                exception.Usage?.CandidateTokens,
                exception.Usage?.ThoughtsTokens,
                exception.Usage?.CachedContentTokens,
                exception.Usage?.BillableOutputTokens,
                exception.Usage?.TotalTokens,
                costUsd, costKnown, costNative, currency,
                Accepted: null,
                RejectionReason: null,
                ChangeCount: null,
                ChangedRatio: null,
                AcceptedText: null,
                AcceptedTextMatchesCorrected: null,
                ProtectionViolations: [],
                exception.Error.ToString(),
                exception.StatusCode is { } status ? (int)status : null,
                exception.Message);
        }
    }

    private static IProofreadingClient CreateClient(
        ModelDescriptor descriptor,
        string apiKey,
        HttpClient httpClient,
        TimeSpan timeout)
        => descriptor.Provider switch
        {
            ApiProvider.Google => new GeminiProofreadingClient(
                () => apiKey, httpClient, descriptor.Id, null, timeout,
                () => ProofreadingPurpose.Manual),
            ApiProvider.OpenAi => new OpenAiProofreadingClient(
                () => apiKey, httpClient, descriptor.Id, null, timeout,
                () => ProofreadingPurpose.Manual),
            ApiProvider.Anthropic => new AnthropicProofreadingClient(
                () => apiKey, httpClient, descriptor.Id, null, timeout,
                () => ProofreadingPurpose.Manual),
            ApiProvider.PreferredNetworks => new PlamoProofreadingClient(
                () => apiKey, httpClient, descriptor.Id, null, timeout,
                () => ProofreadingPurpose.Manual),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
        };

    private static decimal EstimateTotalCostUsd(
        IReadOnlyList<ModelDescriptor> models,
        IReadOnlyList<BenchmarkText> texts,
        int trials,
        int systemInstructionLength,
        PricingService pricing,
        decimal? usdJpyRate)
    {
        decimal total = 0;
        foreach (BenchmarkText text in texts)
        {
            int inputTokens = (int)((systemInstructionLength + text.Text.Length) *
                                    ModelBenchmark.EstimatedTokensPerChar);
            int outputTokens = (int)(text.Text.Length * ModelBenchmark.EstimatedTokensPerChar) +
                               ModelBenchmark.EstimatedThinkingTokens;
            foreach (ModelDescriptor descriptor in models)
            {
                PricingQuote quote = pricing.Calculate(descriptor.Id, inputTokens, outputTokens);
                // レートが無ければ円建てぶんは見積もりから落とす（推測レートで水増ししない）。
                total += quote.ToUsd(usdJpyRate) * trials ?? 0m;
            }
        }

        return total;
    }

    private static void PrintTrial(
        ModelDescriptor descriptor,
        BenchmarkText text,
        int trial,
        BenchmarkTrialResult result,
        decimal spentUsd)
    {
        string head = $"[{trial}/{text.Id}/{descriptor.Id}]";
        if (!result.Succeeded)
        {
            Console.WriteLine($"{head} 失敗 {result.ErrorKind}: {result.ErrorMessage}");
            return;
        }

        string cost = result.CostUsdKnown && result.CostUsd is { } value
            ? $"${value:F6}"
            : $"{result.CostNative:F2} {result.CostCurrency}（USD 換算不可）";
        string verdict = result.Accepted == true
            ? $"提案 {result.ChangeCount}"
            : $"破棄（{result.RejectionReason}）";
        string protection = result.ProtectionViolations.Count > 0
            ? $" ! 保護違反 {result.ProtectionViolations.Count}: " +
              string.Join(" / ", result.ProtectionViolations)
            : "";
        Console.WriteLine(
            $"{head} {result.ElapsedMs:F0} ms / {verdict} / " +
            $"tokens {result.PromptTokens}+{result.BillableOutputTokens} / {cost} / " +
            $"累計 ${spentUsd:F4}{protection}");
    }

    private static void PrintSummary(BenchmarkReport report)
    {
        Console.WriteLine();
        Console.WriteLine("=== モデル別サマリ（手動用 effort、中央値）===");
        foreach (BenchmarkModelSummary summary in report.Summary.OrderBy(s => s.MedianElapsedMs ?? double.MaxValue))
        {
            string elapsed = summary.MedianElapsedMs is { } ms ? $"{ms / 1000:F1} s" : "—";
            string cost = summary.MedianCostUsd is { } value && summary.CostUsdKnown
                ? $"${value:F6}"
                : "—";
            int violations = summary.Protection.Sum(p => p.ViolationCount);
            Console.WriteLine(
                $"  {summary.DisplayName,-24} {elapsed,8} / {cost,12} / " +
                $"提案 {summary.MeanChangeCount:F1} / 破棄 {summary.RejectedCount} / " +
                $"失敗 {summary.FailureCount} / 保護違反 {violations}");
        }

        Console.WriteLine();
        Console.WriteLine($"実測合計: ${report.TotalCostUsd:F4}");
        if (report.StoppedByBudget)
            Console.WriteLine("※ 費用上限に達したため、一部の組み合わせは未計測です。");
    }

    private static IReadOnlyList<BenchmarkText> LoadTexts()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "benchmark-texts.json");
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<BenchmarkText>>(json, JsonOptions)
            ?? throw new InvalidOperationException("benchmark-texts.json を読み取れませんでした。");
    }

    private static string DefaultOutputPath(int trials)
        => Path.Combine(
            "PromptValidation",
            "results",
            $"model-benchmark-{DateTime.Now:yyyy-MM-dd}-r{trials}.json");

    private static string DataDirectory()
        => Path.GetDirectoryName(AppPaths.CredentialsFile) ?? "(不明)";

    private static string Sha256Hex(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 一時ディレクトリを消せなくても計測結果には影響しない。
        }
    }

    private static void PrintHelp()
        => Console.WriteLine(
            """
            校正モデル比較ベンチマーク（実APIを呼びます。課金が発生します）

            使用法:
              dotnet run --project PromptValidation -- --model-benchmark [オプション]

            オプション:
              --trials N        1 モデル × 1 文章あたりの試行回数（既定: 3）
              --max-cost USD    実測の累計費用の上限。超えたら以後を打ち切る（既定: 10.00）
              --models a,b      対象モデルIDを絞る（既定: カタログの全モデル）
              --texts a,b       対象文章IDを絞る（既定: benchmark-texts.json の全件）
              --timeout S       全モデル共通のタイムアウト秒（既定: 120）
              --output PATH     保存先（既定: PromptValidation/results/model-benchmark-{日付}-r{試行}.json）
              --yes             実行前の確認を省略する
              --help            このヘルプを表示

            APIキーは環境変数 GEMINI_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY / PLAMO_API_KEY
            から読みます。見つからないプロバイダーだけ credentials.dat（DPAPI）へフォールバックし、
            それでも取れなければそのプロバイダーのモデルを理由つきで除外します。
            """);

    private sealed record BenchmarkOptions(
        int Trials,
        decimal MaxCostUsd,
        IReadOnlyList<string>? ModelIds,
        IReadOnlyList<string>? TextIds,
        int TimeoutSeconds,
        string? OutputPath,
        bool AssumeYes,
        bool ShowHelp)
    {
        internal static BenchmarkOptions Parse(string[] args)
        {
            int trials = 3;
            decimal maxCostUsd = 10.00m;
            IReadOnlyList<string>? modelIds = null;
            IReadOnlyList<string>? textIds = null;
            int timeoutSeconds = DefaultTimeoutSeconds;
            string? output = null;
            bool assumeYes = false;
            bool help = false;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument)
                {
                    case "--trials":
                        if (!int.TryParse(NextValue(args, ref index, argument), out trials) ||
                            trials < 1)
                            throw new ArgumentException("--trials は1以上の整数で指定してください。");
                        break;
                    case "--max-cost":
                        if (!decimal.TryParse(
                                NextValue(args, ref index, argument),
                                NumberStyles.Number,
                                CultureInfo.InvariantCulture,
                                out maxCostUsd) ||
                            maxCostUsd <= 0)
                            throw new ArgumentException("--max-cost は0より大きいUSD額で指定してください。");
                        break;
                    case "--models":
                        modelIds = SplitList(NextValue(args, ref index, argument));
                        break;
                    case "--texts":
                        textIds = SplitList(NextValue(args, ref index, argument));
                        break;
                    case "--timeout":
                        // 上限は本体の ProofreadingModelCatalog.ClampTimeout（300秒）より広い。
                        // 設定画面の上限は「UIを待たせない」ための値で、遅いモデルが完走するかを
                        // 測るこのコマンドには当てはまらないため、あえて縛らない。
                        if (!int.TryParse(NextValue(args, ref index, argument), out timeoutSeconds) ||
                            timeoutSeconds < 5 || timeoutSeconds > 600)
                            throw new ArgumentException("--timeout は5〜600秒で指定してください。");
                        break;
                    case "--output":
                        output = NextValue(args, ref index, argument);
                        break;
                    case "--yes":
                        assumeYes = true;
                        break;
                    case "--help":
                    case "-h":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException($"不明な引数です: {argument}");
                }
            }

            return new BenchmarkOptions(
                trials, maxCostUsd, modelIds, textIds, timeoutSeconds, output, assumeYes, help);
        }

        private static IReadOnlyList<string> SplitList(string value)
            => [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                    StringSplitOptions.TrimEntries)];

        private static string NextValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length)
                throw new ArgumentException($"{option} の値が必要です。");
            return args[index];
        }
    }
}
