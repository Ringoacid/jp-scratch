using System.Globalization;
using System.Text;

namespace JpScratch.Services;

/// <summary>
/// 課金履歴（要件 3.6.2）のCSVエクスポート。WPF・SQLiteに依存しない純粋関数だけを置き、
/// <see cref="JpScratch.Views.BillingHistoryWindow"/> はファイル選択と書き込みだけを担当する。
///
/// 画面の表示書式（<see cref="UsageFormatting"/>）とは意図的に分けてある。画面は読みやすさ優先で
/// `$0.000107` `¥0.02` のように丸めるが、CSVは表計算ソフトで再集計する前提なので、
/// USD/JPY/レートは保存済みの <see cref="decimal"/> をそのまま不変カルチャで書く（通貨記号なし）。
/// ここで画面の書式を使い回すと、合計が丸め誤差で合わなくなる。
/// </summary>
internal static class BillingCsvExporter
{
    /// <summary>RFC 4180 の改行。OS依存の <see cref="Environment.NewLine"/> は使わない。</summary>
    private const string LineBreak = "\r\n";

    internal static readonly string[] Headers =
    [
        "日時", "種別", "モデル", "入力トークン", "出力トークン",
        "USD", "USD/JPYレート", "レート基準日", "JPY", "所要ms",
        "成否", "提案件数", "破棄件数", "エラー",
    ];

    /// <summary>
    /// 明細行からCSV本文を組み立てる。1行目は必ずヘッダで、行がゼロ件でもヘッダだけは出す
    /// （空ファイルだと「書き出しに失敗した」のか「該当0件だった」のか区別できない）。
    /// </summary>
    internal static string BuildCsv(IReadOnlyList<ApiCallHistoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.Append(string.Join(",", Headers.Select(h => EscapeField(h)))).Append(LineBreak);

        foreach (ApiCallHistoryRow row in rows)
        {
            builder.Append(string.Join(",", BuildFields(row))).Append(LineBreak);
        }

        return builder.ToString();
    }

    private static IEnumerable<string> BuildFields(ApiCallHistoryRow row)
    {
        // 日時は画面の一覧と同じローカル時刻表記にする。表計算ソフトが日付として解釈できる形を
        // 優先し、オフセットは付けない（元のオフセットは api_calls に残っている）。
        yield return EscapeField(
            row.CalledAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        yield return EscapeField(UsageFormatting.FormatTrigger(row.Trigger));
        // モデル名とエラー文は外部（API応答・例外メッセージ）由来の自由文字列なので数式ガードをかける。
        yield return EscapeField(row.Model, guardFormula: true);
        yield return EscapeField(row.PromptTokens.ToString(CultureInfo.InvariantCulture));
        yield return EscapeField(row.OutputTokens.ToString(CultureInfo.InvariantCulture));
        yield return EscapeField(row.UsdCost.ToString(CultureInfo.InvariantCulture));
        yield return EscapeField(FormatNullableDecimal(row.UsdJpyRate));
        yield return EscapeField(
            row.RateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
        yield return EscapeField(FormatNullableDecimal(row.JpyCost));
        yield return EscapeField(row.DurationMilliseconds.ToString(CultureInfo.InvariantCulture));
        yield return EscapeField(UsageFormatting.FormatStatus(row.Status));
        yield return EscapeField(row.SuggestionCount.ToString(CultureInfo.InvariantCulture));
        yield return EscapeField(row.DiscardedCount.ToString(CultureInfo.InvariantCulture));
        yield return EscapeField(row.ErrorMessage ?? "", guardFormula: true);
    }

    /// <summary>
    /// レート・JPYが未記録（NULL）の行は空欄にする。0 と書くと「レート0円で換算した」ように読めるため。
    /// </summary>
    private static string FormatNullableDecimal(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// RFC 4180 のエスケープ。カンマ・二重引用符・改行を含む値は全体を <c>"</c> で囲み、
    /// 内側の <c>"</c> は二重にする。
    ///
    /// <paramref name="guardFormula"/> が true の値が <c>= + - @</c> やタブ・CR で始まる場合は、
    /// 先頭に <c>'</c> を付けて数式として実行されないようにする（CSVインジェクション対策）。
    /// エラー文はGemini APIの応答や例外メッセージ由来で内容を制御できないため、
    /// 表計算ソフトで開いた瞬間に数式として評価されうる。数値列には適用しない。
    /// </summary>
    internal static string EscapeField(string? value, bool guardFormula = false)
    {
        string text = value ?? "";
        if (guardFormula && text.Length > 0)
        {
            // 先頭の " は RFC 4180 の引用符として Excel 側で剥がされるため、剥がした後の
            // 先頭文字で危険文字を判定する（例: "=cmd...）。通常の = + - @ タブ CR で
            // 始まる値は従来どおり前置する。
            int firstNonQuote = 0;
            while (firstNonQuote < text.Length && text[firstNonQuote] == '"') firstNonQuote++;
            if (firstNonQuote < text.Length && "=+-@\t\r".Contains(text[firstNonQuote]))
                text = "'" + text;
        }

        if (text.AsSpan().IndexOfAny(",\"\r\n") < 0)
            return text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// 保存ダイアログの既定ファイル名。期間ラベルは日本語のうえ「〜」などを含みうるので使わず、
    /// 出力時刻だけで一意にする（同じ条件で連続して出しても上書き確認にならない）。
    /// </summary>
    internal static string BuildDefaultFileName(DateTimeOffset now)
        => "jpscratch-billing-" +
           now.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
           ".csv";

    /// <summary>
    /// 書き出しに使うエンコーディング。**BOM付きUTF-8固定**。BOMが無いと Excel が CP932 として
    /// 読み、日本語のヘッダ・種別ラベルが文字化けする（PowerShellスクリプトのBOM問題と同じ理由）。
    /// </summary>
    internal static Encoding CsvEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
}
