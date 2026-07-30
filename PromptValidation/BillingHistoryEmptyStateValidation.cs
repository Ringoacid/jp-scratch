using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 課金履歴画面（<c>Views/BillingHistoryWindow.xaml.cs</c>）が起動直後の既定状態
/// （期間=当月、種別=4つとも選択）でロードする経路を、<c>api_calls</c> が0件の状態で再現する。
/// </summary>
/// <remarks>
/// 実機で報告されたクラッシュ（ステータスバーの利用状況クリック → 課金履歴画面がNullReferenceExceptionで
/// 開けない）の原因は、実際には <c>BillingHistoryWindow</c> のコンストラクタの初期化ガード
/// （<c>_initializing</c> を <c>InitializeComponent()</c> の"後"に true にしていたため、種別チェックボックスの
/// <c>IsChecked="True"</c> がBAML読み込み中に発火させる <c>Checked</c> イベントを止められず、
/// まだ配線されていない <c>ValidationErrorText</c> を触って落ちていた）ことが、ユーザー環境の
/// <c>%APPDATA%\JpScratch\crash.log</c> のスタックトレースから特定できた。この不具合自体は
/// <c>BillingHistoryWindow.xaml.cs</c> のコンストラクタで <c>_initializing = true;</c> を
/// <c>InitializeComponent()</c> より前に移すことで修正済み。
///
/// この不具合を実機同様に自動テストで再現するには、<c>BillingHistoryWindow</c> 自体を
/// 実際に生成する必要がある。試したところ、PromptValidation プロジェクトへ XAML を
/// 二重コンパイルできるようにするには <c>&lt;UseWPF&gt;true&lt;/UseWPF&gt;</c> が要るが、
/// これを付けると .NET SDK の既定の暗黙 using セット（特に <c>System.IO</c> / <c>System.Net.Http</c>）が
/// 変わり、このプロジェクトの既存ファイル9個ほどが軒並みコンパイルエラーになった
/// （<c>Path</c> / <c>Directory</c> / <c>File</c> / <c>IOException</c> / <c>HttpClient</c> が解決できなくなる）。
/// 1件の再現テストのために既存の検証群を広く触る不整合な変更になるため、この方式は採用しなかった。
///
/// 代わりに、初期化ガードで守られていない「その先」の経路 — <c>api_calls</c> が0件のときに
/// <see cref="ApiCallRepository.GetHistory"/> / <see cref="ApiCallRepository.GetUsageSummary"/> と
/// <see cref="UsageFormatting"/> がクラッシュせず正しい既定表示を作れること — をここで確認する。
/// これはレビューで挙げられた別の疑わしい候補（ログ0件特有の経路）を潰すものであり、
/// 実際のBAML読み込み順序バグそのものの回帰検知にはならない。そちらは
/// <c>Views/BillingHistoryWindow.xaml.cs</c> の <c>_initializing = true;</c> の位置を
/// 目視で確認する運用に頼る（コンストラクタ冒頭のコメント参照）。
/// </remarks>
internal static class BillingHistoryEmptyStateValidation
{
    internal static bool RunSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchBillingHistoryEmptyStateValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            var repository = new ApiCallRepository(database);

            // BillingHistoryWindow の既定状態: 期間 = 当月、種別 = 4つとも選択。
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset from = UsagePeriod.StartOfMonth(now);
            DateTimeOffset to = LocalStartOfNextMonth(now);
            ApiCallTrigger[] allTriggers =
            [
                ApiCallTrigger.Auto, ApiCallTrigger.Manual,
                ApiCallTrigger.Realternative, ApiCallTrigger.StyleGuide,
            ];

            // api_calls は1行も無い（ユーザー報告と同じ状態）。
            ApiCallHistoryPage page = repository.GetHistory(from, to, allTriggers);
            ApiCallUsageSummary summary = repository.GetUsageSummary(from, to, allTriggers);

            bool historyPassed =
                page.Rows.Count == 0 && page.TotalCount == 0 && !page.Truncated;
            bool summaryPassed = summary == ApiCallUsageSummary.Empty;

            // 画面のヘッダ文言が組み立てるのと同じ整形関数を、0件の集計に対して直接呼ぶ。
            // 例外が飛ばないこと、かつユーザーのスクリーンショットの表示（$0 (¥0.00)）と一致することを確認する。
            string usd = UsageFormatting.FormatUsd(summary.UsdCost);
            string jpy = UsageFormatting.FormatJpy(summary);
            string statusCounts = UsageFormatting.FormatStatusCounts(summary);
            string rateReference = UsageFormatting.FormatSummaryRateReference(summary);

            bool formattingPassed =
                usd == "0" && jpy == "¥0.00" &&
                statusCounts == "成功 0 / エラー 0 / タイムアウト 0" &&
                rateReference == "JPY 0円（レート不要）";

            bool passed = historyPassed && summaryPassed && formattingPassed;

            Console.WriteLine(
                "課金履歴の既定表示（api_calls 0件でのGetHistory/GetUsageSummary/ヘッダ整形）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static DateTimeOffset LocalStartOfNextMonth(DateTimeOffset value)
    {
        DateTime localStart = new(
            value.LocalDateTime.Year, value.LocalDateTime.Month, 1, 0, 0, 0, DateTimeKind.Local);
        return new DateTimeOffset(localStart.AddMonths(1));
    }
}
