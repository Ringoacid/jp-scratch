using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class PricingServiceValidation
{
    internal static bool RunSelfTests()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "JpScratch-PricingValidation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string pricingFile = Path.Combine(root, "pricing.json");
            var created = new PricingService(pricingFile);
            PricingQuote defaultQuote =
                created.Calculate(PricingService.DefaultModel, 1_000_000, 1_000_000);
            bool defaultPass =
                File.Exists(pricingFile) &&
                defaultQuote.Cost == 2.80m &&
                defaultQuote.Pricing.UpdatedAt == "2026-07-29";
            PricingQuote openAiQuote =
                created.Calculate(PricingService.OpenAiModel, 1_000_000, 1_000_000);
            bool openAiDefaultPass =
                openAiQuote.Cost == 1.40m &&
                openAiQuote.Pricing.InputUsdPerMillion == 0.20m &&
                openAiQuote.Pricing.OutputUsdPerMillion == 1.20m &&
                openAiQuote.Pricing.UpdatedAt == "2026-07-31";

            File.WriteAllText(
                pricingFile,
                """
                {
                  "custom-model": {
                    "input_usd_per_1m": 1.25,
                    "output_usd_per_1m": 4.50,
                    "updated_at": "2026-07-30"
                  }
                }
                """);
            var customized = new PricingService(pricingFile);
            PricingQuote customQuote =
                customized.Calculate("custom-model", 2_000, 3_000);
            bool customPass =
                customQuote.Cost == 0.01600m &&
                customized.GetPricing(PricingService.DefaultModel)
                    .InputUsdPerMillion == 0.30m;

            File.WriteAllText(pricingFile, """{"broken":{"input_usd_per_1m":-1}}""");
            var recovered = new PricingService(pricingFile);
            bool recoveryPass =
                recovered.GetPricing(PricingService.DefaultModel)
                    .OutputUsdPerMillion == 2.50m &&
                Directory.EnumerateFiles(root, "pricing.json.bad*").Any();

            // 上限超過の単価（decimalオーバーフロー源）は不正として隔離され、既定単価へ戻る。
            File.WriteAllText(
                pricingFile,
                """
                {
                  "custom-model": {
                    "input_usd_per_1m": 99999999999999999999999999,
                    "output_usd_per_1m": 0.20,
                    "updated_at": "2026-07-30"
                  }
                }
                """);
            var capped = new PricingService(pricingFile);
            bool capModelRejected;
            try
            {
                capped.GetPricing("custom-model");
                capModelRejected = false; // 読み込まれてしまった＝上限チェックが効いていない
            }
            catch (KeyNotFoundException)
            {
                capModelRejected = true; // 隔離されて既定値へ戻った
            }

            // 設定画面の入力側も同じ上限を共有する（TryParseUnitPrice）。
            bool parseCapPass =
                !SettingsFieldFormatting.TryParseUnitPrice("1000000001", out _) &&
                SettingsFieldFormatting.TryParseUnitPrice("1000000000", out _) &&
                SettingsFieldFormatting.TryParseUnitPrice("0", out _);

            bool unknownPass;
            try
            {
                recovered.Calculate("unknown-model", 1, 1);
                unknownPass = false;
            }
            catch (KeyNotFoundException)
            {
                unknownPass = true;
            }

            bool replacePass = RunReplaceTests(pricingFile);

            bool migrationPass = RunOpenAiPricingMigrationTest(root);
            bool scheduledPricingPass = RunScheduledPricingTests(root);
            bool historyPass = RunPricingHistoryTests(root);
            bool pass =
                defaultPass && openAiDefaultPass && customPass && recoveryPass &&
                capModelRejected && parseCapPass && unknownPass && replacePass && migrationPass &&
                scheduledPricingPass && historyPass;
            Console.WriteLine(
                "料金設定（作成・モデル別計算・破損復旧・設定画面からの編集API）: " +
                (pass ? "PASS" : "FAIL"));
            return pass;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"料金設定: FAIL / {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool RunScheduledPricingTests(string root)
    {
        string pricingFile = Path.Combine(root, "scheduled-pricing.json");
        File.WriteAllText(pricingFile, "{\"gemini-3.6-flash\":{\"input_usd_per_1m\":1.50,\"output_usd_per_1m\":7.50,\"updated_at\":\"2026-08-04\"},\"claude-sonnet-5\":{\"input_usd_per_1m\":3.00,\"output_usd_per_1m\":15.00,\"updated_at\":\"2026-08-04\"},\"custom-gemini\":{\"input_usd_per_1m\":9.00,\"output_usd_per_1m\":19.00,\"updated_at\":\"2026-08-04\"}}");
        DateOnly geminiToday = new DateOnly(2026, 12, 31);
        var beforeGemini = new PricingService(
            pricingFile, utcTodayProvider: () => geminiToday);
        ModelPricing geminiBefore = beforeGemini.GetPricing("gemini-3.6-flash");
        ModelPricing sonnetBefore = beforeGemini.GetPricing("claude-sonnet-5");
        ModelPricing customBefore = beforeGemini.GetPricing("custom-gemini");
        ModelPricing newModelBefore = beforeGemini.GetPricing("gemini-3.7-flash");
        bool endDatePass =
            geminiBefore.CatalogManaged &&
            geminiBefore.InputUsdPerMillion == 0.75m &&
            geminiBefore.OutputUsdPerMillion == 3.75m &&
            sonnetBefore.CatalogManaged &&
            sonnetBefore.InputUsdPerMillion == 3.00m &&
            sonnetBefore.OutputUsdPerMillion == 15.00m &&
            newModelBefore.CatalogManaged &&
            newModelBefore.InputUsdPerMillion == 0.75m &&
            newModelBefore.OutputUsdPerMillion == 3.75m;

        geminiToday = new DateOnly(2027, 1, 1);
        ModelPricing geminiAfter = beforeGemini.GetPricing("gemini-3.6-flash");
        ModelPricing sonnetAfter = beforeGemini.GetPricing("claude-sonnet-5");
        ModelPricing customAfter = beforeGemini.GetPricing("custom-gemini");
        ModelPricing newModelAfter = beforeGemini.GetPricing("gemini-3.7-flash");
        bool nextDayPass =
            geminiAfter.CatalogManaged &&
            geminiAfter.InputUsdPerMillion == 1.50m &&
            geminiAfter.OutputUsdPerMillion == 7.50m &&
            newModelAfter.CatalogManaged &&
            newModelAfter.InputUsdPerMillion == 1.50m &&
            newModelAfter.OutputUsdPerMillion == 7.50m &&
            sonnetAfter.InputUsdPerMillion == 3.00m &&
            sonnetAfter.OutputUsdPerMillion == 15.00m;
        bool customHeldPass =
            !customBefore.CatalogManaged &&
            !customAfter.CatalogManaged &&
            customBefore.InputUsdPerMillion == 9.00m &&
            customAfter.InputUsdPerMillion == 9.00m &&
            customBefore.OutputUsdPerMillion == 19.00m &&
            customAfter.OutputUsdPerMillion == 19.00m;

        var sonnetPromo = new PricingService(
            pricingFile, utcTodayProvider: () => new DateOnly(2026, 8, 31));
        var sonnetStandard = new PricingService(
            pricingFile, utcTodayProvider: () => new DateOnly(2026, 9, 1));
        bool sonnetBoundaryPass =
            sonnetPromo.GetPricing("claude-sonnet-5").InputUsdPerMillion == 2.00m &&
            sonnetPromo.GetPricing("claude-sonnet-5").OutputUsdPerMillion == 10.00m &&
            sonnetStandard.GetPricing("claude-sonnet-5").InputUsdPerMillion == 3.00m &&
            sonnetStandard.GetPricing("claude-sonnet-5").OutputUsdPerMillion == 15.00m;

        bool pass = endDatePass && nextDayPass && customHeldPass && sonnetBoundaryPass;
        return pass;
    }

    private static bool RunPricingHistoryTests(string root)
    {
        string pricingFile = Path.Combine(root, "pricing-history.json");
        DateOnly today = new(2026, 8, 19);
        var service = new PricingService(pricingFile, () => today);
        const string model = "claude-sonnet-5";
        PricingEvent[] events =
        [
            new(new DateOnly(2026, 8, 20), PricingEventType.Override, 7m, 17m),
            new(new DateOnly(2026, 9, 15), PricingEventType.UseCatalog),
        ];
        service.ReplaceUserEvents(model, events);

        bool futureUnaffected =
            service.GetPricing(model).InputUsdPerMillion == 2m;
        bool overrideStartsOnDate =
            service.GetPricing(model, new DateOnly(2026, 8, 20)).InputUsdPerMillion == 7m &&
            service.GetPricing(model, new DateOnly(2026, 9, 1)).OutputUsdPerMillion == 17m;
        bool resetStartsOnDate =
            service.GetPricing(model, new DateOnly(2026, 9, 15)).CatalogManaged &&
            service.GetPricing(model, new DateOnly(2026, 9, 15)).InputUsdPerMillion == 3m;

        var reloaded = new PricingService(
            pricingFile,
            () => new DateOnly(2026, 9, 20));
        bool persisted =
            reloaded.GetUserEvents(model).SequenceEqual(events) &&
            reloaded.GetPricing(model).InputUsdPerMillion == 3m &&
            File.ReadAllText(pricingFile).Contains("version", StringComparison.Ordinal);
        IReadOnlyList<PricingHistoryEntry> history = reloaded.GetHistory(model);
        bool officialAndUserVisible =
            history.Any(row => row.IsCatalog && row.IsPromotional &&
                               row.EffectiveFrom == new DateOnly(2026, 8, 14)) &&
            history.Any(row => row.IsCatalog && !row.IsPromotional &&
                               row.EffectiveFrom == new DateOnly(2026, 9, 1)) &&
            history.Count(row => !row.IsCatalog) == 2;

        bool duplicateRejected;
        try
        {
            reloaded.ReplaceUserEvents(
                model,
                [
                    events[0],
                    new(events[0].EffectiveFrom, PricingEventType.UseCatalog),
                ]);
            duplicateRejected = false;
        }
        catch (InvalidDataException)
        {
            duplicateRejected =
                reloaded.GetUserEvents(model).SequenceEqual(events);
        }

        bool pass = futureUnaffected && overrideStartsOnDate && resetStartsOnDate &&
                    persisted && officialAndUserVisible && duplicateRejected;
        Console.WriteLine(
            "  料金履歴（将来予約・ユーザー優先・公式復帰・永続化・重複拒否）: " +
            (pass ? "PASS" : "FAIL"));
        return pass;
    }

    private static bool RunOpenAiPricingMigrationTest(string root)
    {
        string pricingFile = Path.Combine(root, "legacy-openai-pricing.json");
        File.WriteAllText(
            pricingFile,
            """
            {
              "gemini-3.5-flash-lite": {
                "input_usd_per_1m": 0.30,
                "output_usd_per_1m": 2.50,
                "updated_at": "2026-07-29"
              },
              "gpt-5.6-luna": {
                "input_usd_per_1m": 1.00,
                "output_usd_per_1m": 6.00,
                "updated_at": "2026-07-31"
              }
            }
            """);

        var migrated = new PricingService(pricingFile);
        ModelPricing pricing = migrated.GetPricing(PricingService.OpenAiModel);
        return pricing.InputUsdPerMillion == 0.20m &&
               pricing.OutputUsdPerMillion == 1.20m &&
               pricing.UpdatedAt == "2026-07-31";
    }

    /// <summary>
    /// 設定画面から使う <see cref="PricingService.Snapshot"/> / <see cref="PricingService.Replace"/> の検証。
    /// 他モデルの保持、永続化（別インスタンスでの読み直し）、拒否ケースでのメモリ非破壊を確認する。
    /// </summary>
    private static bool RunReplaceTests(string pricingFile)
    {
        File.WriteAllText(
            pricingFile,
            """
            {
              "gemini-3.5-flash-lite": {
                "input_usd_per_1m": 0.30,
                "output_usd_per_1m": 2.50,
                "updated_at": "2026-07-29"
              },
              "other-model": {
                "input_usd_per_1m": 1.00,
                "output_usd_per_1m": 3.00,
                "updated_at": "2026-07-01"
              }
            }
            """);
        var service = new PricingService(pricingFile);

        // 既定モデルの単価だけを変更し、other-model はSnapshotの値をそのまま渡す。
        var snapshot = new Dictionary<string, ModelPricing>(service.Snapshot(), StringComparer.Ordinal);
        snapshot[PricingService.DefaultModel] = new ModelPricing
        {
            InputUsdPerMillion = 0.40m,
            OutputUsdPerMillion = 2.60m,
            UpdatedAt = "2026-08-01",
        };
        service.Replace(snapshot);

        bool otherModelKept =
            service.GetPricing("other-model").InputUsdPerMillion == 1.00m &&
            service.GetPricing("other-model").OutputUsdPerMillion == 3.00m;
        bool defaultModelUpdated =
            service.GetPricing(PricingService.DefaultModel).InputUsdPerMillion == 0.40m;

        // 永続化の確認: 同じファイルを読み直した新しいインスタンスが新しい値を返す。
        var reloaded = new PricingService(pricingFile);
        bool persisted =
            reloaded.GetPricing(PricingService.DefaultModel).InputUsdPerMillion == 0.40m &&
            reloaded.GetPricing("other-model").InputUsdPerMillion == 1.00m;

        // 拒否ケース: 負値・日付不正・既定モデル欠落は例外で拒否され、メモリ上の単価は変わらない。
        bool negativeRejected = RejectsAndKeepsMemory(service, m =>
        {
            m[PricingService.DefaultModel] = new ModelPricing
            {
                InputUsdPerMillion = -1m,
                OutputUsdPerMillion = 2.60m,
                UpdatedAt = "2026-08-01",
            };
        });
        bool badDateRejected = RejectsAndKeepsMemory(service, m =>
        {
            m[PricingService.DefaultModel] = new ModelPricing
            {
                InputUsdPerMillion = 0.40m,
                OutputUsdPerMillion = 2.60m,
                UpdatedAt = "2026/08/01",
            };
        });
        bool missingDefaultRejected = RejectsAndKeepsMemory(service, m =>
        {
            m.Remove(PricingService.DefaultModel);
        });

        bool pass = otherModelKept && defaultModelUpdated && persisted &&
                    negativeRejected && badDateRejected && missingDefaultRejected;
        Console.WriteLine(
            "  Replace（他モデル保持・永続化・負値/日付不正/既定モデル欠落の拒否）: " +
            (pass ? "PASS" : "FAIL"));
        return pass;
    }

    private static bool RejectsAndKeepsMemory(
        PricingService service,
        Action<Dictionary<string, ModelPricing>> mutate)
    {
        ModelPricing before = service.GetPricing(PricingService.DefaultModel);

        var candidate = new Dictionary<string, ModelPricing>(service.Snapshot(), StringComparer.Ordinal);
        mutate(candidate);

        bool threw;
        try
        {
            service.Replace(candidate);
            threw = false;
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        ModelPricing after = service.GetPricing(PricingService.DefaultModel);
        bool unchanged =
            after.InputUsdPerMillion == before.InputUsdPerMillion &&
            after.OutputUsdPerMillion == before.OutputUsdPerMillion &&
            after.UpdatedAt == before.UpdatedAt;

        return threw && unchanged;
    }
}
