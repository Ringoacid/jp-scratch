using System.Text.Encodings.Web;
using System.Text.Json;

namespace JpScratch.PromptValidation;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            // --seed-billing はケース選択やAPI呼び出しの土台となる Options.Parse と形が異なるため、
            // ここで先に分岐する（既存フローの解析ロジックには一切触れない）。
            if (args.Length > 0 && args[0] == "--seed-billing")
                return BillingSeedCommand.Run(args.Skip(1).ToArray());
            // --probe-openai-cache も同様に別系統。--self-test には絶対に混ぜない
            // （実APIを呼ぶ診断のため、self-testが無課金・無キーで動く前提を壊す）。
            if (args.Length > 0 && args[0] == "--probe-openai-cache")
                return await OpenAiCacheProbeCommand.RunAsync();
            // --model-benchmark も同じ理由で別系統・--self-test 対象外（全プロバイダーへ実課金する）。
            // 計算部分だけは ModelBenchmarkValidation として self-test に入れてある。
            if (args.Length > 0 && args[0] == "--model-benchmark")
                return await ModelBenchmarkCommand.RunAsync(args.Skip(1).ToArray());

            Options options = Options.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            IReadOnlyList<ValidationCase> cases = LoadCases();

            if (options.SelfTest)
                return await RunSelfTestAsync();
            if (options.AnalyzeResultsPath is not null)
                return DocumentDiffValidation.AnalyzeSavedResults(
                    options.AnalyzeResultsPath,
                    cases);

            IReadOnlyList<ValidationCase> selected = SelectCases(cases, options);

            if (options.DryRun)
            {
                PrintPrompt(selected[0], options.Variant);
                return 0;
            }

            string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine(
                    "GEMINI_API_KEY が設定されていません。APIキーはコマンドライン引数ではなく環境変数で渡してください。");
                return 2;
            }

            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            using GeminiClient client = new(apiKey, options.Model);
            List<CaseResult> results = [];
            decimal spentUsd = 0;

            for (int iteration = 1; iteration <= options.Repeat; iteration++)
            {
                foreach (ValidationCase testCase in selected)
                {
                    if (spentUsd >= options.MaxCostUsd)
                    {
                        Console.WriteLine(
                            $"費用上限 ${options.MaxCostUsd:F2} に達したため中断します。");
                        goto Finished;
                    }

                    Console.Write($"[{options.Variant}/{iteration}/{testCase.Id}] ");
                    ProbeResult probe = await client.GenerateAsync(
                        testCase,
                        options.Variant,
                        cancellation.Token);
                    CaseResult result = Evaluator.Evaluate(
                        testCase,
                        probe,
                        options.Variant,
                        iteration);
                    results.Add(result);
                    spentUsd += result.CostUsd;
                    PrintCaseResult(result);
                }
            }

        Finished:
            PrintSummary(results);
            if (options.OutputPath is not null)
            {
                string fullPath = Path.GetFullPath(options.OutputPath);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(
                    fullPath,
                    JsonSerializer.Serialize(results, JsonOptions),
                    cancellation.Token);
                Console.WriteLine($"レポート: {fullPath}");
            }

            return results.All(result => result.Passed) ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("中断しました。");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"エラー: {exception.Message}");
            return 2;
        }
    }

    private static IReadOnlyList<ValidationCase> LoadCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "cases.json");
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ValidationCase>>(json, JsonOptions)
            ?? throw new InvalidOperationException("cases.json を読み取れませんでした。");
    }

    private static IReadOnlyList<ValidationCase> SelectCases(
        IReadOnlyList<ValidationCase> cases,
        Options options)
    {
        if (options.Input is not null)
        {
            return
            [
                new ValidationCase(
                    "adhoc",
                    "adhoc",
                    options.Input,
                    options.BeforeContext,
                    options.AfterContext,
                    [])
            ];
        }

        if (options.CaseId is null)
        {
            return options.Suite == "all"
                ? cases
                : cases.Where(item => item.Kind == options.Suite).ToArray();
        }

        ValidationCase? selected = cases.FirstOrDefault(
            item => string.Equals(item.Id, options.CaseId, StringComparison.OrdinalIgnoreCase));
        return selected is null
            ? throw new ArgumentException($"ケース「{options.CaseId}」が見つかりません。")
            : [selected];
    }

    private static void PrintCaseResult(CaseResult result)
    {
        string status = result.Passed ? "PASS" : "FAIL";
        Console.WriteLine(
            $"{status} / 提案 {result.Corrections.Count} / 破棄 {result.DiscardedCount} / " +
            $"{result.ElapsedMilliseconds:F0} ms / tokens {result.PromptTokens}+{result.CandidateTokens} / " +
            $"${result.CostUsd:F6}");

        foreach (Correction correction in result.Corrections)
        {
            Console.WriteLine(
                $"  {correction.Category}: 「{correction.Original}」→「{correction.Suggestion}」" +
                $" confidence={correction.Confidence:F2}");
            Console.WriteLine($"    {correction.Reason}");
        }

        foreach (string failure in result.Failures)
            Console.WriteLine($"  ! {failure}");
    }

    private static void PrintSummary(IReadOnlyList<CaseResult> results)
    {
        int passed = results.Count(result => result.Passed);
        int typoPassed = results.Count(result => result.Kind == "error" && result.Passed);
        int typoTotal = results.Count(result => result.Kind == "error");
        int stylePassed = results.Count(result => result.Kind == "style" && result.Passed);
        int styleTotal = results.Count(result => result.Kind == "style");

        Console.WriteLine();
        Console.WriteLine($"総合: {passed}/{results.Count} PASS");
        if (typoTotal > 0)
            Console.WriteLine($"誤り検出: {typoPassed}/{typoTotal}");
        if (styleTotal > 0)
            Console.WriteLine($"文体保護: {stylePassed}/{styleTotal}");
        Console.WriteLine(
            $"合計 tokens: {results.Sum(result => result.PromptTokens)}+" +
            $"{results.Sum(result => result.CandidateTokens)}");
        Console.WriteLine($"推定料金: ${results.Sum(result => result.CostUsd):F6}");
    }

    private static void PrintPrompt(ValidationCase testCase, string variant)
    {
        Console.WriteLine("=== SYSTEM ===");
        Console.WriteLine(PromptFactory.BuildSystemInstruction(variant));
        Console.WriteLine("=== USER ===");
        bool fullRewrite = variant.StartsWith("full-rewrite", StringComparison.Ordinal);
        Console.WriteLine(
            fullRewrite
                ? PromptFactory.BuildRewriteRequest(testCase, variant)
                : PromptFactory.BuildUserPrompt(testCase));
        if (!fullRewrite)
        {
            Console.WriteLine("=== RESPONSE SCHEMA ===");
            Console.WriteLine(PromptFactory.SchemaAsJson());
        }
    }

    private static async Task<int> RunSelfTestAsync()
    {
        Correction exact = new("typo", "文章ア", "文章が", "この", "、誤り", "", 0.9);
        Correction fallback = new("typo", "同じ", "違う", "後ろの", "", "", 0.9);
        bool exactPass = Evaluator.ResolvePosition("この文章ア、誤り", exact) == 2;
        bool fallbackPass = Evaluator.ResolvePosition("同じ、後ろの同じ", fallback) == 6;

        Console.WriteLine($"位置解決（完全一致）: {(exactPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"位置解決（複数候補）: {(fallbackPass ? "PASS" : "FAIL")}");
        bool diffPass = DocumentDiffValidation.RunSelfTests();
        bool paragraphPass = ParagraphProofreadingPlannerValidation.RunSelfTests();
        bool dispatchPlannerPass = ProofreadingDispatchPlannerValidation.RunSelfTests();
        bool credentialPass = CredentialServiceValidation.RunSelfTests();
        bool pricingPass = PricingServiceValidation.RunSelfTests();
        bool apiCallPass = ApiCallRepositoryValidation.RunSelfTests();
        bool apiCallHistoryPass = ApiCallRepositoryValidation.RunHistorySelfTests();
        bool apiCallUsageTriggerPass = ApiCallRepositoryValidation.RunUsageSummaryTriggerFilterSelfTests();
        bool apiCallUnconfirmedPass = ApiCallRepositoryValidation.RunUnconfirmedCostSelfTests();
        bool hideSuppressionPass = HideSuppressionCounterValidation.RunSelfTests();
        bool customDateRangePass = CustomDateRangeParserValidation.RunSelfTests();
        bool billingHistoryEmptyStatePass = BillingHistoryEmptyStateValidation.RunSelfTests();
        bool usagePeriodPass = UsagePeriodValidation.RunSelfTests();
        bool usageLimitPass = UsageLimitServiceValidation.RunSelfTests();
        bool migrationPass = DatabaseMigrationValidation.RunSelfTests();
        bool fxRatePass = await FxRateServiceValidation.RunSelfTestsAsync();
        bool reactionPass = ReactionRepositoryValidation.RunSelfTests();
        bool schedulePass = ProofreadingScheduleValidation.RunSelfTests();
        bool geminiClientPass = await GeminiProofreadingClientValidation.RunSelfTestsAsync();
        bool openAiClientPass = await OpenAiProofreadingClientValidation.RunSelfTestsAsync();
        bool completionGuardPass = await ProviderCompletionGuardValidation.RunSelfTestsAsync();
        bool modelCatalogPass = ProofreadingModelCatalogValidation.RunSelfTests();
        bool appPathsPass = AppPathsValidation.RunSelfTests();
        bool singleInstancePass = SingleInstanceValidation.RunSelfTests();
        bool billingSeedPass = BillingSeedCommandValidation.RunSelfTests();
        bool settingsFieldFormattingPass = SettingsFieldFormattingValidation.RunSelfTests();
        bool billingCsvPass = BillingCsvExporterValidation.RunSelfTests();
        bool apiLogCompactionPass = ApiLogCompactionValidation.RunSelfTests();
        bool trayIconStatePass = TrayIconStateValidation.RunSelfTests();
        bool fewShotPass = FewShotSelectorValidation.RunSelfTests();
        bool styleGuidePass = StyleGuideRepositoryValidation.RunSelfTests();
        bool promptV3Pass = ProofreadingPromptV3Validation.RunSelfTests();
        bool inlineDiffPass = ProofreadingInlineDiffLayoutValidation.RunSelfTests();
        bool statusBarFormatterPass = StatusBarUsageFormatterValidation.RunSelfTests();
        bool apiUsageDisplayPass = ApiUsageDisplayFormatterValidation.RunSelfTests();
        bool missedCorrectionPass = MissedCorrectionActionValidation.RunSelfTests();
        bool crossTabPreviewPass = CrossTabSearchPreviewValidation.RunSelfTests();
        bool trashRepositoryPass = TrashRepositoryValidation.RunSelfTests();
        bool atomicFilePass = AtomicFileValidation.RunSelfTests();
        bool modelBenchmarkPass = ModelBenchmarkValidation.RunSelfTests();
        return exactPass && fallbackPass && diffPass && paragraphPass &&
               credentialPass && pricingPass && apiCallPass && apiCallHistoryPass &&
               apiCallUsageTriggerPass && apiCallUnconfirmedPass && hideSuppressionPass && customDateRangePass &&
               billingHistoryEmptyStatePass && usagePeriodPass && usageLimitPass &&
               migrationPass && fxRatePass && reactionPass && schedulePass && dispatchPlannerPass &&
               geminiClientPass && openAiClientPass && completionGuardPass && modelCatalogPass &&
               appPathsPass && singleInstancePass &&
               billingSeedPass && settingsFieldFormattingPass && billingCsvPass &&
               apiLogCompactionPass && trayIconStatePass &&
               fewShotPass && styleGuidePass && promptV3Pass && inlineDiffPass &&
               statusBarFormatterPass && apiUsageDisplayPass && missedCorrectionPass &&
               crossTabPreviewPass && trashRepositoryPass && atomicFilePass && modelBenchmarkPass ? 0 : 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            JP Scratch プロンプト検証

            使用法:
              dotnet run --project PromptValidation
              dotnet run --project PromptValidation -- --case typo-01
              dotnet run --project PromptValidation -- --input "校正する文章"
              dotnet run --project PromptValidation -- --dry-run [--case ID]
              dotnet run --project PromptValidation -- --self-test
              dotnet run --project PromptValidation -- --analyze-results PromptValidation/results

            オプション:
              --case ID          cases.json の1ケースだけ実行
              --input TEXT       任意の文章を実行（合否判定なし）
              --before TEXT      任意文章の前文脈
              --after TEXT       任意文章の後文脈
              --model ID         モデルID（既定: gemini-3.5-flash-lite）
              --variant NAME     current / minimal-diff / phrase-span / full-rewrite / full-rewrite-safe
              --suite NAME       all / error / style（既定: all）
              --repeat N         各ケースの反復回数（既定: 1）
              --max-cost USD     1回の実行で許容する推定料金（既定: 2.00）
              --output PATH      JSONレポートを保存
              --dry-run          APIを呼ばずプロンプトとスキーマを表示
              --self-test        APIを呼ばず位置解決ロジックを検査
              --analyze-results PATH
                                 保存済み全文応答の差分抽出を検査（APIは呼ばない）
              --seed-billing DIR [--bulk] [--force]
                                 隔離ディレクトリへ検証用app.dbを作り、課金履歴画面の
                                 目視確認用データを投入する（APIは呼ばない）。
                                 --bulk は2000件を超える大量データ（Truncated確認用）。
                                 --force は既存app.dbの上書きを明示的に許可する。
              --model-benchmark [--trials N] [--max-cost USD] [--models a,b] [--texts a,b]
                                 [--timeout S] [--output PATH] [--yes]
                                 カタログ収録の全モデルへ同じ文章を送り、所要時間・料金・
                                 全提案を承諾した結果をJSONへ保存する比較ベンチマーク。
                                 課金が発生する。api_calls とpricing.json へは書き込まない。
                                 APIキーは GEMINI_API_KEY / OPENAI_API_KEY /
                                 ANTHROPIC_API_KEY / PLAMO_API_KEY から読む。
                                 詳細は --model-benchmark --help を参照。
              --probe-openai-cache
                                 v3相当の長いシステム指示を同一内容で2回連続送信し、OpenAIの
                                 自動プロンプトキャッシュ（cached_tokens）が働くかを実APIで確認する
                                 診断コマンド。課金が発生する。api_calls へは書き込まない。
                                 APIキーは環境変数 OPENAI_API_KEY から読む。
              --help             このヘルプを表示

            APIキーは環境変数 GEMINI_API_KEY からのみ読み取ります
            （--probe-openai-cache だけは OPENAI_API_KEY を使います）。
            """);
    }

    private sealed record Options(
        string? CaseId,
        string? Input,
        string? BeforeContext,
        string? AfterContext,
        string Model,
        string Variant,
        string Suite,
        int Repeat,
        decimal MaxCostUsd,
        string? OutputPath,
        bool DryRun,
        bool SelfTest,
        string? AnalyzeResultsPath,
        bool ShowHelp)
    {
        internal static Options Parse(string[] args)
        {
            string? caseId = null;
            string? input = null;
            string? before = null;
            string? after = null;
            string model = "gemini-3.5-flash-lite";
            string variant = "full-rewrite-safe";
            string suite = "all";
            int repeat = 1;
            decimal maxCostUsd = 2.00m;
            string? output = null;
            bool dryRun = false;
            bool selfTest = false;
            string? analyzeResults = null;
            bool help = false;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument)
                {
                    case "--case":
                        caseId = NextValue(args, ref index, argument);
                        break;
                    case "--input":
                        input = NextValue(args, ref index, argument);
                        break;
                    case "--before":
                        before = NextValue(args, ref index, argument);
                        break;
                    case "--after":
                        after = NextValue(args, ref index, argument);
                        break;
                    case "--model":
                        model = NextValue(args, ref index, argument);
                        break;
                    case "--variant":
                        variant = NextValue(args, ref index, argument);
                        break;
                    case "--suite":
                        suite = NextValue(args, ref index, argument);
                        break;
                    case "--repeat":
                        if (!int.TryParse(NextValue(args, ref index, argument), out repeat) ||
                            repeat < 1)
                            throw new ArgumentException("--repeat は1以上の整数で指定してください。");
                        break;
                    case "--max-cost":
                        if (!decimal.TryParse(
                                NextValue(args, ref index, argument),
                                System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out maxCostUsd) ||
                            maxCostUsd <= 0)
                            throw new ArgumentException("--max-cost は0より大きいUSD額で指定してください。");
                        break;
                    case "--output":
                        output = NextValue(args, ref index, argument);
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--self-test":
                        selfTest = true;
                        break;
                    case "--analyze-results":
                        analyzeResults = NextValue(args, ref index, argument);
                        break;
                    case "--help":
                    case "-h":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException($"不明な引数です: {argument}");
                }
            }

            if (caseId is not null && input is not null)
                throw new ArgumentException("--case と --input は同時に指定できません。");
            if (!PromptFactory.Variants.Contains(variant, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"--variant は {string.Join(" / ", PromptFactory.Variants)} から選んでください。");
            if (suite is not ("all" or "error" or "style"))
                throw new ArgumentException("--suite は all / error / style から選んでください。");

            return new Options(
                caseId, input, before, after, model, variant, suite, repeat, maxCostUsd, output,
                dryRun, selfTest, analyzeResults, help);
        }

        private static string NextValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length)
                throw new ArgumentException($"{option} の値が必要です。");
            return args[index];
        }
    }
}
