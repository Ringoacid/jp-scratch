namespace JpScratch.Services;

/// <summary>
/// 校正漏れ報告（proofreading-ux-fixes-plan.md §9）の操作種別。
/// </summary>
internal enum MissedCorrectionKind
{
    /// <summary>選択あり・修正後あり: 選択範囲を置換する。</summary>
    Replace,

    /// <summary>選択なし・修正後あり: カーソル位置へ挿入する。</summary>
    Insert,

    /// <summary>選択あり・修正後が空: 選択範囲を削除する。</summary>
    Delete,
}

/// <summary>
/// 校正漏れ報告の操作種別判定（proofreading-ux-fixes-plan.md §9.3）。WPF 非依存の純粋関数。
/// 置換・挿入・削除の判定と、実行ボタンの文言を固定する。
/// </summary>
internal static class MissedCorrectionAction
{
    /// <summary>
    /// 操作種別を決める。実行不可の場合は <paramref name="allowed"/> が false になる。
    /// 判定規則:
    /// - 選択あり・修正後あり → 置換
    /// - 選択なし・修正後あり → 挿入
    /// - 選択あり・修正後が空 → 削除
    /// - 選択なし・修正後が空 → 実行不可
    /// - 修正前と修正後が同一 → 実行不可
    /// </summary>
    internal static MissedCorrectionKind Determine(
        string original,
        string corrected,
        out bool allowed)
    {
        bool hasOriginal = original.Length > 0;
        bool hasCorrected = corrected.Length > 0;

        if (string.Equals(original, corrected, StringComparison.Ordinal))
        {
            allowed = false;
            return default;
        }

        if (!hasOriginal && !hasCorrected)
        {
            allowed = false;
            return default;
        }

        allowed = true;
        if (!hasOriginal)
            return MissedCorrectionKind.Insert;
        if (!hasCorrected)
            return MissedCorrectionKind.Delete;
        return MissedCorrectionKind.Replace;
    }

    /// <summary>実行ボタンの文言（§9.2）。</summary>
    internal static string ButtonText(MissedCorrectionKind kind) => kind switch
    {
        MissedCorrectionKind.Replace => "修正して校正漏れを記録",
        MissedCorrectionKind.Insert => "挿入して校正漏れを記録",
        MissedCorrectionKind.Delete => "削除して校正漏れを記録",
        _ => "記録",
    };
}

/// <summary>
/// 校正漏れ報告ダイアログの「前後の文脈＋修正箇所」プレビュー（§9）で使う文字列整形の純粋関数。
/// WPF の <c>TextBlock</c> の <c>Inlines</c> へ流し込む前の、切り詰め・改行の平坦化だけを担う。
/// ダイアログ側は <see cref="JpScratch.Views.MissedCorrectionDialog"/> を参照。
/// </summary>
internal static class MissedCorrectionPreview
{
    /// <summary>プレビューで修正箇所の両側に表示する文脈の最大文字数。</summary>
    internal const int DefaultMaxContextChars = 24;

    /// <summary>プレビューで原文（選択範囲）を省略するときの最大文字数。</summary>
    internal const int DefaultMaxOriginalChars = 30;

    /// <summary>プレビュー用に改行を空白へ潰す（単一行の流れで前後文脈を示すため）。</summary>
    internal static string FlattenForPreview(string text)
        => (text ?? "").Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    /// <summary>左文脈を末尾基準で切り詰め、切った側に「…」を付ける。</summary>
    internal static string TruncateLeft(string text, int maxChars)
        => text.Length <= maxChars ? text : "…" + text[^maxChars..];

    /// <summary>右文脈を先頭基準で切り詰め、切った側に「…」を付ける。</summary>
    internal static string TruncateRight(string text, int maxChars)
        => text.Length <= maxChars ? text : text[..maxChars] + "…";

    /// <summary>原文（選択範囲）をプレビュー用に省略する。長すぎる場合は前後を残して中を省略する。</summary>
    internal static string TruncateOriginal(string original, int maxChars = DefaultMaxOriginalChars)
    {
        if (original.Length <= maxChars)
            return original;
        return original[..(maxChars / 2)] + "…" + original[^10..];
    }
}
