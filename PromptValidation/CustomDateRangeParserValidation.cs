using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="CustomDateRangeParser"/> の自己テスト。課金履歴画面のカスタム期間入力の解析、
/// 終了日が極端な値（<c>9999-12-31</c>）のときに例外で落ちずエラーを返すこと、
/// 開始日が極端な値（<c>0001-01-01</c>）のときも例外で落ちず、かつ終了日側の文言を
/// 誤って名指ししないこと（いずれもレビュー指摘の再発防止）を確かめる。
/// </summary>
internal static class CustomDateRangeParserValidation
{
    internal static bool RunSelfTests()
    {
        CustomDateRangeParser.Result valid = CustomDateRangeParser.Parse("2026-07-01", "2026-07-31");
        DateTimeOffset expectedFrom = new(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Local));
        DateTimeOffset expectedTo = new(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Local));
        bool validPassed =
            !valid.IsError && valid.From == expectedFrom && valid.To == expectedTo;

        CustomDateRangeParser.Result sameDay = CustomDateRangeParser.Parse("2026-07-15", "2026-07-15");
        DateTimeOffset sameDayFrom = new(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Local));
        DateTimeOffset sameDayTo = new(new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Local));
        bool sameDayPassed =
            !sameDay.IsError && sameDay.From == sameDayFrom && sameDay.To == sameDayTo;

        CustomDateRangeParser.Result badFormat = CustomDateRangeParser.Parse("2026/07/01", "2026-07-31");
        bool badFormatPassed = badFormat.IsError && badFormat.From is null && badFormat.To is null;

        CustomDateRangeParser.Result blank = CustomDateRangeParser.Parse("", "  ");
        bool blankPassed = blank.IsError;

        CustomDateRangeParser.Result reversed = CustomDateRangeParser.Parse("2026-07-31", "2026-07-01");
        bool reversedPassed = reversed.IsError &&
            reversed.Error == "開始日は終了日より前の日付にしてください";

        // レビュー指摘の再現ケース: 終了日を DateOnly の上限（9999-12-31）にすると、
        // 翌日への繰り上げが DateTime/DateTimeOffset の表現範囲を超える。例外を投げず、
        // エラーメッセージとして返すことを確認する（修正前は ArgumentOutOfRangeException で落ちていた）。
        CustomDateRangeParser.Result extremeEnd = CustomDateRangeParser.Parse("9999-12-01", "9999-12-31");
        bool extremeEndPassed = extremeEnd.IsError && extremeEnd.From is null && extremeEnd.To is null;

        // 上限のすぐ手前なら正常に計算できる。
        CustomDateRangeParser.Result nearExtremeEnd = CustomDateRangeParser.Parse("9999-12-01", "9999-12-29");
        bool nearExtremeEndPassed = !nearExtremeEnd.IsError && nearExtremeEnd.To is not null;

        // レビュー指摘の再現ケース（実機 UTC+9 で確認）: 開始日を DateOnly の下限（0001-01-01）にすると、
        // ローカル→UTC変換で下限を下回りうる。from/to を同じ try で計算していた旧実装は、
        // このケースでも「終了日が扱える範囲を超えています」という誤った文言を返していた。
        // アンダーフローするかどうかはローカルタイムゾーンのオフセットの符号に依存する
        // （負のオフセットの環境ではそもそも下限を下回らない）ため、例外を投げずに返ってくること
        // 自体と、返ってきたエラーが「開始日」を正しく名指しすることの両方を確認する。
        CustomDateRangeParser.Result minStart = CustomDateRangeParser.Parse("0001-01-01", "2026-07-31");
        bool minStartPassed = minStart.IsError
            ? minStart.From is null && minStart.To is null &&
              minStart.Error == "開始日が扱える範囲を超えています。もう少し後の日付を指定してください"
            : minStart.From is not null && minStart.To is not null;

        bool passed =
            validPassed && sameDayPassed && badFormatPassed && blankPassed &&
            reversedPassed && extremeEndPassed && nearExtremeEndPassed && minStartPassed;

        Console.WriteLine(
            "課金履歴カスタム期間の解析（正常系・書式/前後エラー・開始日/終了日いずれの上限でも例外化しない）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
