using System.Globalization;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>正の円額がUSDの0へ丸められないことを、呼び出し時と後追い補完の両経路で検証する。</summary>
internal static class CurrencyConversionValidation
{
    private const decimal TinyJpyCost = 0.000000000000000000000000001m;
    private const decimal UsdJpyRate = 159.01m;

    internal static bool RunSelfTests()
    {
        bool immediate = TestImmediateConversionRoundsUp();
        bool completion = TestCompletionConversionRoundsUp();
        bool passed = immediate && completion;
        Console.WriteLine(
            "極小の正の円額のUSD換算（呼び出し時・後追い補完）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool TestImmediateConversionRoundsUp()
    {
        var quote = new PricingQuote(
            "plamo-3.0-prime",
            1,
            1,
            TinyJpyCost,
            PricingCurrency.Jpy,
            new ModelPricing { Currency = PricingCurrency.Jpy });
        decimal? usdCost = quote.ToUsd(UsdJpyRate);

        return usdCost == UsdCostConversion.MinimumPositiveUsd && usdCost > 0m;
    }

    private static bool TestCompletionConversionRoundsUp()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchCurrencyConversionValidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var database = new Database(Path.Combine(directory, "test.db"));
            DateTimeOffset clock = new(2026, 8, 21, 10, 0, 0, TimeSpan.FromHours(9));
            var repository = new ApiCallRepository(database, () => clock);
            long id = repository.Add(new ApiCallLogEntry(
                ApiCallTrigger.Manual,
                "plamo-3.0-prime",
                1,
                1,
                0m,
                1,
                ApiCallStatus.Ok,
                null,
                0,
                0,
                OriginalCurrency: PricingCurrency.Jpy,
                OriginalCost: TinyJpyCost,
                IsUsdCostConfirmed: false));

            FxRateCompletionResult result = repository.ApplyFxRates(
                new Dictionary<DateOnly, FxRate>
                {
                    [new DateOnly(2026, 8, 21)] =
                        new FxRate(new DateOnly(2026, 8, 21), UsdJpyRate, clock),
                });
            (string Jpy, string Usd, int Confirmed) stored = database.Read(
                "SELECT jpy_cost, usd_cost, usd_cost_confirmed FROM api_calls WHERE id = $id;",
                reader => reader.Read()
                    ? (reader.GetString(0), reader.GetString(1), reader.GetInt32(2))
                    : ("", "", -1),
                ("$id", id));

            return result == new FxRateCompletionResult(1, 0) &&
                   stored.Jpy == TinyJpyCost.ToString(CultureInfo.InvariantCulture) &&
                   stored.Usd == UsdCostConversion.MinimumPositiveUsd.ToString(CultureInfo.InvariantCulture) &&
                   stored.Usd != "0" &&
                   stored.Confirmed == 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  極小料金の後追い補完: FAIL / {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
