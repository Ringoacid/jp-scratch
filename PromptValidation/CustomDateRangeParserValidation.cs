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
            reversedPassed && extremeEndPassed && nearExtremeEndPassed && minStartPassed &&
            RunRoundTripTests() && RunAllTimeToCustomDefaultTests();

        Console.WriteLine(
            "課金履歴カスタム期間の解析（正常系・書式/前後エラー・開始日/終了日いずれの上限でも例外化しない）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 課金履歴画面の項目7の自己テスト。プリセット（当日/当週/当月）が実際にクエリへ渡す
    /// 半開区間を <see cref="CustomDateRangeParser.FormatInclusive"/> で書き戻し、その文字列を
    /// 再び <see cref="CustomDateRangeParser.Parse"/> へ通しても同じ区間になることを確認する。
    /// ここがずれると「当月からカスタムに切り替えただけで期間が1日ずれる」バグになる
    /// （レビュー指摘の再現防止）。
    /// </summary>
    private static bool RunRoundTripTests()
    {
        bool RoundTrips(DateTimeOffset from, DateTimeOffset toExclusive)
        {
            (string fromText, string toText) = CustomDateRangeParser.FormatInclusive(from, toExclusive);
            CustomDateRangeParser.Result reparsed = CustomDateRangeParser.Parse(fromText, toText);
            return !reparsed.IsError && reparsed.From == from && reparsed.To == toExclusive;
        }

        // 当月相当（月初〜翌月1日00:00、排他）。
        bool monthPreset = RoundTrips(
            new DateTimeOffset(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Local)),
            new DateTimeOffset(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Local)));

        // 当週相当（月曜0時〜翌週月曜0時、排他）。
        bool weekPreset = RoundTrips(
            new DateTimeOffset(new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Local)),
            new DateTimeOffset(new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Local)));

        // 当日相当（1日だけ、排他的な終了は翌日0時）。
        bool dayPreset = RoundTrips(
            new DateTimeOffset(new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Local)),
            new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Local)));

        // 年またぎ（12月→翌年1月）でも同じく往復する。
        bool yearBoundary = RoundTrips(
            new DateTimeOffset(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Local)),
            new DateTimeOffset(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local)));

        bool passed = monthPreset && weekPreset && dayPreset && yearBoundary;
        Console.WriteLine(
            "  プリセット→カスタム欄書き戻しの往復一致（当月/当週/当日/年またぎ）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 課金履歴画面のレビュー指摘の再発防止テスト。「全期間」から欄を空にした状態で
    /// 「カスタム」へ切り替えると、<c>BillingHistoryWindow.FillCustomRangeDefaultIfBlank</c> が
    /// 当月（<see cref="UsagePeriod.StartOfMonth"/> 〜翌月1日00:00）を既定値として書き戻す。
    /// WPFのウィンドウはこの検証ハーネスから直接インスタンス化できない（App.xamlのリソース初期化・
    /// STAスレッドを要するため）ため、その既定値ぶんの日付計算とフォーマット・再解析だけを
    /// ロジックレベルで再現し、「空欄のままカスタムへ切り替えても、いきなり入力エラーにならず、
    /// 有効な範囲として解釈できる」ことを確認する。
    /// </summary>
    private static bool RunAllTimeToCustomDefaultTests()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset monthStart = UsagePeriod.StartOfMonth(now);
        DateTimeOffset nextMonthStart = LocalStartOfNextMonth(now);

        (string fromText, string toText) = CustomDateRangeParser.FormatInclusive(monthStart, nextMonthStart);
        CustomDateRangeParser.Result parsed = CustomDateRangeParser.Parse(fromText, toText);

        bool notAnError = !parsed.IsError;
        bool matchesComputedRange = parsed.From == monthStart && parsed.To == nextMonthStart;

        bool passed = notAnError && matchesComputedRange;
        Console.WriteLine(
            "  全期間→カスタムの既定値（当月書き戻しがエラーにならず有効な範囲として解釈できる）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    // BillingHistoryWindow.LocalStartOfNextMonth と同じ計算（この検証ハーネスはWindowを参照しない）。
    private static DateTimeOffset LocalStartOfNextMonth(DateTimeOffset value)
    {
        DateTime localStart = new(
            value.LocalDateTime.Year, value.LocalDateTime.Month, 1, 0, 0, 0, DateTimeKind.Local);
        return new DateTimeOffset(localStart.AddMonths(1));
    }
}
