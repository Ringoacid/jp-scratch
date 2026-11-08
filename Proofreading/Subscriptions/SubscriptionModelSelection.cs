using System.Text.RegularExpressions;
using JpScratch.Models;

namespace JpScratch.Proofreading;

internal static class SubscriptionModelSelection
{
    // Names are a conservative hint when the service does not publish comparable prices.
    // These tiers are not a subscription invoice or an API price estimate.
    internal static bool IsHighCost(SubscriptionModel model) => model.Multiplier > 1 ||
        Regex.IsMatch(model.Id + " " + model.Name, @"\b(astra|fable|opus)\b", RegexOptions.IgnoreCase) ||
        (ProofreadingModelCatalog.IsSupported(model.Id) && ProofreadingModelCatalog.IsHighCostForProofreading(model.Id));

    internal static bool IsEconomy(SubscriptionModel model) => !IsHighCost(model) &&
        Regex.IsMatch(model.Id + " " + model.Name, @"\b(luna|nano|mini|haiku|lite|spark)\b", RegexOptions.IgnoreCase);

    internal static IReadOnlyList<SubscriptionModel> Order(IEnumerable<SubscriptionModel> source)
    {
        var models = source.ToArray();
        // Compare service prices only when the same fields exist for every candidate.
        bool prices = models.Length > 0 && models.All(m => Valid(m.InputPrice) && Valid(m.OutputPrice));
        bool multipliers = models.Length > 0 && models.All(m => Valid(m.Multiplier));
        return models.OrderBy(m => m.Id.Equals("auto", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(m => prices ? m.InputPrice!.Value + m.OutputPrice!.Value :
                multipliers ? m.Multiplier!.Value : IsEconomy(m) ? 0 : IsHighCost(m) ? 2 : 1)
            .ThenBy(m => IsHighCost(m) ? 1 : 0)
            .ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool Valid(double? value) => value is >= 0 && double.IsFinite(value.Value);
    internal static string Label(SubscriptionModel model) => model.Name;
}
