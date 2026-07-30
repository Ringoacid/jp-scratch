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
                defaultQuote.UsdCost == 2.80m &&
                defaultQuote.Pricing.UpdatedAt == "2026-07-29";

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
                customQuote.UsdCost == 0.01600m &&
                customized.GetPricing(PricingService.DefaultModel)
                    .InputUsdPerMillion == 0.30m;

            File.WriteAllText(pricingFile, """{"broken":{"input_usd_per_1m":-1}}""");
            var recovered = new PricingService(pricingFile);
            bool recoveryPass =
                recovered.GetPricing(PricingService.DefaultModel)
                    .OutputUsdPerMillion == 2.50m &&
                Directory.EnumerateFiles(root, "pricing.json.bad*").Any();

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

            bool pass =
                defaultPass && customPass && recoveryPass && unknownPass;
            Console.WriteLine(
                "料金設定（作成・モデル別計算・破損復旧）: " +
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
}
