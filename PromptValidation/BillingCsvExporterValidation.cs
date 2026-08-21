using System.Globalization;
using System.Text;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 課金履歴のCSVエクスポート（<see cref="BillingCsvExporter"/>、要件 3.6.2）の自己テスト。
///
/// 実際のファイル書き込みとダイアログは <c>Views/BillingHistoryWindow.xaml.cs</c> 側にあり、
/// ここでは組み立てたCSV本文だけを検査する。中心にあるのは
/// **「行 → CSV → パース → 同じ値」の往復不変性**で、`SettingsFieldFormatting` や
/// `CustomDateRangeParser.FormatInclusive` と同じ考え方。
/// </summary>
internal static class BillingCsvExporterValidation
{
    internal static bool RunSelfTests()
    {
        bool headerPassed = RunHeaderSelfTest();
        bool escapePassed = RunEscapeSelfTests();
        bool formulaPassed = RunFormulaGuardSelfTests();
        bool roundTripPassed = RunRoundTripSelfTest();
        bool precisionPassed = RunPrecisionSelfTest();
        bool encodingPassed = RunEncodingSelfTest();
        bool fileNamePassed = RunFileNameSelfTest();

        bool passed = headerPassed && escapePassed && formulaPassed && roundTripPassed &&
            precisionPassed && encodingPassed && fileNamePassed;

        Console.WriteLine(
            "課金履歴CSV（ヘッダ・エスケープ・数式ガード・往復・精度保持・BOM・既定ファイル名）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunHeaderSelfTest()
    {
        // 0件でもヘッダだけは出す（空ファイルだと失敗と区別できない）。
        string csv = BillingCsvExporter.BuildCsv([]);
        const string expected =
            "日時,種別,モデル,入力トークン,出力トークン,USD,料金状態,元通貨,元通貨額," +
            "USD/JPYレート,レート基準日,JPY,所要ms,成否,提案件数,破棄件数,エラー\r\n";

        bool passed = csv == expected && BillingCsvExporter.Headers.Length == 17;
        Console.WriteLine("  ヘッダのみ（0件・17列・CRLF）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunEscapeSelfTests()
    {
        bool plain = BillingCsvExporter.EscapeField("abc") == "abc";
        bool comma = BillingCsvExporter.EscapeField("a,b") == "\"a,b\"";
        bool quote = BillingCsvExporter.EscapeField("a\"b") == "\"a\"\"b\"";
        bool newline = BillingCsvExporter.EscapeField("a\nb") == "\"a\nb\"";
        bool carriageReturn = BillingCsvExporter.EscapeField("a\r\nb") == "\"a\r\nb\"";
        bool empty = BillingCsvExporter.EscapeField("") == "";
        bool nullValue = BillingCsvExporter.EscapeField(null) == "";
        // 日本語は囲まない（不要な引用符でファイルが読みにくくならないこと）。
        bool japanese = BillingCsvExporter.EscapeField("別案生成") == "別案生成";

        bool passed = plain && comma && quote && newline && carriageReturn &&
            empty && nullValue && japanese;
        Console.WriteLine("  RFC 4180 エスケープ（カンマ・引用符・改行・空・null）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunFormulaGuardSelfTests()
    {
        // エラー文はGemini API応答や例外メッセージ由来で内容を制御できない。
        bool equals = BillingCsvExporter.EscapeField("=1+1", guardFormula: true) == "'=1+1";
        bool plus = BillingCsvExporter.EscapeField("+1", guardFormula: true) == "'+1";
        bool minus = BillingCsvExporter.EscapeField("-1", guardFormula: true) == "'-1";
        bool at = BillingCsvExporter.EscapeField("@SUM(A1)", guardFormula: true) == "'@SUM(A1)";
        // タブはRFC 4180の囲み対象ではないので、ガードの ' が付くだけで引用符は増えない。
        bool tab = BillingCsvExporter.EscapeField("\tx", guardFormula: true) == "'\tx";
        // CRで始まる値はガードと囲みの両方が効く。
        bool carriageReturn = BillingCsvExporter.EscapeField("\rx", guardFormula: true) == "\"'\rx\"";
        // カンマを含む数式は、ガードを付けたうえで囲む（両方が効く）。
        bool both = BillingCsvExporter.EscapeField("=A1,B1", guardFormula: true) == "\"'=A1,B1\"";
        // ガードを指定しない列は素通し。数値列に '- を付けると数値として読めなくなる。
        bool notGuarded = BillingCsvExporter.EscapeField("-1") == "-1";
        bool safePrefix = BillingCsvExporter.EscapeField("gemini-3.5", guardFormula: true) == "gemini-3.5";

        bool passed = equals && plus && minus && at && tab && carriageReturn && both &&
            notGuarded && safePrefix;
        Console.WriteLine("  数式ガード（= + - @ タブで始まる自由文字列・数値列は対象外）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunRoundTripSelfTest()
    {
        ApiCallHistoryRow[] rows =
        [
            new(1,
                new DateTimeOffset(2026, 7, 30, 19, 50, 51, TimeSpan.FromHours(9)),
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 307, 6,
                0.0001071m, 155.1234m, new DateOnly(2026, 7, 29), 0.01661m,
                907, ApiCallStatus.Ok, null, 1, 0),
            // 円建て・レート未取得で料金未確認。元通貨額はCSVへ残す。
            new(2,
                new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(9)),
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 100, 20,
                0m, null, null, null,
                1200, ApiCallStatus.Timeout, "15秒でタイムアウトしました", 0, 0,
                "JPY", 12.5m, false),
            // エラー文にカンマ・引用符・改行・数式の先頭文字をすべて含む最悪ケース。
            new(3,
                new DateTimeOffset(2026, 7, 30, 21, 0, 0, TimeSpan.FromHours(9)),
                ApiCallTrigger.Realternative, "gemini-3.5-flash-lite", 50, 0,
                0m, 155m, new DateOnly(2026, 7, 30), 0m,
                30, ApiCallStatus.Error, "=HYPERLINK(\"x\"),\r\n改行あり", 0, 2),
        ];

        string csv = BillingCsvExporter.BuildCsv(rows);
        List<string[]> parsed = ParseCsv(csv);

        bool shapePassed =
            parsed.Count == rows.Length + 1 &&
            parsed.All(fields => fields.Length == BillingCsvExporter.Headers.Length);

        bool headerPassed = shapePassed && parsed[0].SequenceEqual(BillingCsvExporter.Headers);

        bool valuesPassed = shapePassed;
        for (int i = 0; i < rows.Length && valuesPassed; i++)
        {
            ApiCallHistoryRow row = rows[i];
            string[] fields = parsed[i + 1];

            valuesPassed =
                fields[0] == row.CalledAt.LocalDateTime.ToString(
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) &&
                fields[1] == UsageFormatting.FormatTrigger(row.Trigger) &&
                fields[2] == row.Model &&
                fields[3] == row.PromptTokens.ToString(CultureInfo.InvariantCulture) &&
                fields[4] == row.OutputTokens.ToString(CultureInfo.InvariantCulture) &&
                decimal.Parse(fields[5], CultureInfo.InvariantCulture) == row.UsdCost &&
                fields[6] == (row.IsUsdCostConfirmed ? "確定" : "未確認") &&
                fields[7] == (row.OriginalCurrency ?? "") &&
                ParseNullableDecimal(fields[8]) == row.OriginalCost &&
                ParseNullableDecimal(fields[9]) == row.UsdJpyRate &&
                fields[10] == (row.RateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "") &&
                ParseNullableDecimal(fields[11]) == row.JpyCost &&
                fields[12] == row.DurationMilliseconds.ToString(CultureInfo.InvariantCulture) &&
                fields[13] == UsageFormatting.FormatStatus(row.Status) &&
                fields[14] == row.SuggestionCount.ToString(CultureInfo.InvariantCulture) &&
                fields[15] == row.DiscardedCount.ToString(CultureInfo.InvariantCulture) &&
                // エラー文だけは数式ガードの ' が付くため、それを剥がしてから比較する。
                StripFormulaGuard(fields[16]) == (row.ErrorMessage ?? "");
        }

        bool passed = shapePassed && headerPassed && valuesPassed;
        Console.WriteLine("  往復（行 → CSV → パース → 同じ値。円欠損・改行・引用符・数式を含む）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunPrecisionSelfTest()
    {
        // 画面表示は小数点以下最大8桁で丸めるが、CSVは再集計に使うので保存値をそのまま書く。
        // UsageFormatting.FormatUsd を通してしまうと、この値は "0.00000012" へ丸められる。
        const decimal highPrecisionUsd = 0.000000123456789m;
        ApiCallHistoryRow row = new(
            1, new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(9)),
            ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 1, 1,
            highPrecisionUsd, null, null, null,
            10, ApiCallStatus.Ok, null, 0, 0);

        string[] fields = ParseCsv(BillingCsvExporter.BuildCsv([row]))[1];
        bool exact = fields[5] == "0.000000123456789";
        bool noCurrencySymbol = !fields[5].Contains('$') && !fields[11].Contains('¥');
        bool differsFromDisplay = fields[5] != UsageFormatting.FormatUsd(highPrecisionUsd);

        bool passed = exact && noCurrencySymbol && differsFromDisplay;
        Console.WriteLine("  精度保持（表示書式で丸めず保存値をそのまま書く・通貨記号なし）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunEncodingSelfTest()
    {
        byte[] bytes = BillingCsvExporter.CsvEncoding.GetPreamble();
        bool hasBom = bytes is [0xEF, 0xBB, 0xBF];

        // BOM無しUTF-8だとExcelがCP932として読み、日本語ヘッダが化ける。
        byte[] encoded = BillingCsvExporter.CsvEncoding.GetBytes("日時");
        bool roundTrips = BillingCsvExporter.CsvEncoding.GetString(encoded) == "日時";

        bool passed = hasBom && roundTrips;
        Console.WriteLine("  エンコーディング（BOM付きUTF-8）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunFileNameSelfTest()
    {
        string name = BillingCsvExporter.BuildDefaultFileName(
            new DateTimeOffset(2026, 7, 31, 15, 4, 5, TimeSpan.FromHours(9)));

        bool expected = name == "jpscratch-billing-20260731-150405.csv";
        // Windowsのファイル名に使えない文字を含まないこと。
        bool valid = name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        bool passed = expected && valid;
        Console.WriteLine("  既定ファイル名（時刻で一意・使用不可文字なし）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static decimal? ParseNullableDecimal(string text)
        => text.Length == 0 ? null : decimal.Parse(text, CultureInfo.InvariantCulture);

    private static string StripFormulaGuard(string text)
        => text.StartsWith('\'') ? text[1..] : text;

    /// <summary>
    /// 検証専用の最小限の RFC 4180 パーサ。<see cref="BillingCsvExporter"/> の実装を流用すると
    /// 同じ誤解を両側で共有してしまうため、独立に書いてある。
    /// </summary>
    private static List<string[]> ParseCsv(string csv)
    {
        List<string[]> records = [];
        List<string> fields = [];
        var field = new StringBuilder();
        bool quoted = false;
        int i = 0;

        while (i < csv.Length)
        {
            char c = csv[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    quoted = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    i++;
                    continue;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    i++;
                    continue;
                case '\r' when i + 1 < csv.Length && csv[i + 1] == '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add([.. fields]);
                    fields.Clear();
                    i += 2;
                    continue;
                default:
                    field.Append(c);
                    i++;
                    continue;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add([.. fields]);
        }

        return records;
    }
}
