using JpScratch.Models;

namespace JpScratch.PromptValidation;

/// <summary>
/// モデル記述子の表と、v3 → v4 の設定移行（要件 3.5.1）の自己テスト。
/// 移行は既存ユーザー全員が初回起動で必ず通る唯一の経路なので、静かに別モデルへ倒れないことを守る。
/// </summary>
internal static class ProofreadingModelCatalogValidation
{
    internal static bool RunSelfTests()
    {
        // 旧設定があれば自動用・手動用の両方へ引き継ぐ（移行前と同じ挙動で起動する）。
        (string auto, string manual) = ProofreadingModelCatalog.MigrateLegacyModel(
            ProofreadingModelCatalog.GeminiModel,
            ProofreadingModelCatalog.DefaultAutomaticModel,
            ProofreadingModelCatalog.DefaultManualModel);
        bool legacyPass =
            auto == ProofreadingModelCatalog.GeminiModel &&
            manual == ProofreadingModelCatalog.GeminiModel;

        // 旧設定が空（新規インストール）なら既定のまま。
        (string freshAuto, string freshManual) = ProofreadingModelCatalog.MigrateLegacyModel(
            "",
            ProofreadingModelCatalog.DefaultAutomaticModel,
            ProofreadingModelCatalog.DefaultManualModel);
        bool freshPass =
            freshAuto == ProofreadingModelCatalog.DefaultAutomaticModel &&
            freshManual == ProofreadingModelCatalog.DefaultManualModel;

        // 未知のモデルID（手で壊した settings.json）は引き継がず既定のまま。
        (string unknownAuto, string unknownManual) = ProofreadingModelCatalog.MigrateLegacyModel(
            "gemini-9.9-nonexistent",
            ProofreadingModelCatalog.DefaultAutomaticModel,
            ProofreadingModelCatalog.DefaultManualModel);
        bool unknownPass =
            unknownAuto == ProofreadingModelCatalog.DefaultAutomaticModel &&
            unknownManual == ProofreadingModelCatalog.DefaultManualModel;

        // 既定モデルは必ずカタログに載っていること（載っていないと起動直後に校正が止まる）。
        bool defaultsPass =
            ProofreadingModelCatalog.IsSupported(ProofreadingModelCatalog.DefaultAutomaticModel) &&
            ProofreadingModelCatalog.IsSupported(ProofreadingModelCatalog.DefaultManualModel);

        // 用途別の思考量は、Haiku 4.5（effort 非対応）以外のすべてで定義されていること。
        bool effortPass = ProofreadingModelCatalog.All.All(descriptor =>
            descriptor.Id == "claude-haiku-4-5-20251001"
                ? descriptor.EffortFor(ProofreadingPurpose.Automatic) is null
                : descriptor.EffortFor(ProofreadingPurpose.Automatic) is not null &&
                  descriptor.EffortFor(ProofreadingPurpose.Manual) is not null);

        // 円建てはPLaMoだけ。ここが崩れると料金表示が桁違いに狂う。
        bool currencyPass = ProofreadingModelCatalog.All.All(descriptor =>
            descriptor.Currency == (descriptor.Id == "plamo-3.0-prime" ? "JPY" : "USD"));

        // タイムアウトの丸めが設定の範囲（5〜300秒）と一致すること。
        bool clampPass =
            ProofreadingModelCatalog.ClampTimeout(TimeSpan.FromSeconds(1)) ==
                ProofreadingModelCatalog.MinimumRequestTimeout &&
            ProofreadingModelCatalog.ClampTimeout(TimeSpan.FromSeconds(9999)) ==
                ProofreadingModelCatalog.MaximumRequestTimeout &&
            ProofreadingModelCatalog.ClampTimeout(TimeSpan.FromSeconds(45)) ==
                TimeSpan.FromSeconds(45);

        Console.WriteLine($"モデルカタログ（旧設定の移行）: {(legacyPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"モデルカタログ（新規は既定のまま）: {(freshPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"モデルカタログ（未知IDは引き継がない）: {(unknownPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"モデルカタログ（既定モデルが収録済み）: {(defaultsPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"モデルカタログ（用途別の思考量）: {(effortPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"モデルカタログ（通貨はPLaMoのみJPY）: {(currencyPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"モデルカタログ（タイムアウトの丸め）: {(clampPass ? "PASS" : "FAIL")}");

        return legacyPass && freshPass && unknownPass && defaultsPass &&
               effortPass && currencyPass && clampPass;
    }
}
