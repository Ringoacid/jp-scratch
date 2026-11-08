using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="HideSuppressionCounter"/> の自己テスト。全タブ検索と課金履歴の両方を開いた状態で
/// 片方だけを閉じても、メインウィンドウの自動非表示が誤って解除されない（レビュー指摘の再発防止）ことを確かめる。
/// </summary>
internal static class HideSuppressionCounterValidation
{
    internal static bool RunSelfTests()
    {
        var counter = new HideSuppressionCounter();
        bool initiallyNotSuppressed = !counter.IsSuppressed;

        counter.Suppress(); // 全タブ検索を開いた
        bool suppressedAfterFirst = counter.IsSuppressed;

        counter.Suppress(); // 課金履歴も開いた（2つ目の子ウィンドウ）
        bool suppressedWithTwoOpen = counter.IsSuppressed;

        counter.Release();  // 課金履歴だけ閉じた
        bool stillSuppressedWhileOtherOpen = counter.IsSuppressed; // 全タブ検索がまだ開いている

        counter.Release();  // 全タブ検索も閉じた
        bool notSuppressedAfterAllClosed = !counter.IsSuppressed;

        counter.Release();  // 対応する Suppress がないのに呼ばれても負にしない
        bool neverGoesNegative = !counter.IsSuppressed;

        counter.Suppress();
        bool suppressedAgainAfterUnderflow = counter.IsSuppressed; // 直前の余分な Release に影響されない

        bool passed =
            initiallyNotSuppressed && suppressedAfterFirst && suppressedWithTwoOpen &&
            stillSuppressedWhileOtherOpen && notSuppressedAfterAllClosed &&
            neverGoesNegative && suppressedAgainAfterUnderflow;

        Console.WriteLine(
            "自動非表示の抑止カウント（複数ウィンドウの片方だけを閉じても抑止継続・負に落ちない）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
