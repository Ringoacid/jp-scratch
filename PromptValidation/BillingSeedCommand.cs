using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 課金履歴画面とステータスバー下段を、実 API を呼ばずに実データへ触れずに目視確認するための
/// シードデータ投入コマンド。<c>--seed-billing &lt;dir&gt;</c> で呼び出す。
///
/// 投入先は必ず呼び出し元が指定した隔離ディレクトリで、%APPDATA%\JpScratch そのもの（実パス含む）
/// への投入は事故防止のため拒否する。credentials.dat は生成しない
/// （APIキーが無い状態にしておくことで、隔離環境から誤って課金APIを呼べないようにする）。
/// </summary>
internal static class BillingSeedCommand
{
    /// <summary>投入する1行分の課金ログ。<see cref="BuildRows"/> が唯一の情報源。</summary>
    internal sealed record SeedRow(
        DateTimeOffset CalledAt,
        ApiCallTrigger Trigger,
        ApiCallStatus Status,
        int PromptTokens,
        int OutputTokens,
        decimal UsdCost,
        long DurationMilliseconds,
        int SuggestionCount,
        int DiscardedCount,
        FxRate? FxRate,
        string? ErrorMessage,
        string Label);

    /// <summary>
    /// <paramref name="now"/> を基準に、期間フィルタ（当日/当週/当月/全期間）・種別フィルタ・
    /// 円欠損（jpy_cost NULL）・複数レート・error/timeoutのツールチップを一通り確認できる
    /// 30行の決定的なデータを組み立てる。日付は実行時点の「今日」からの相対で決めるため、
    /// いつ実行しても当日・当週・当月・前月のいずれかに必ず分布する。
    ///
    /// <paramref name="multiRateOnlyDay1"/>・<paramref name="multiRateOnlyDay2"/>
    /// （<see cref="Run"/> が投入結果に明記する2日間、連続日）は円欠損行を含まず、
    /// レートC・レートDの2種類だけで構成する。カスタム期間としてこの2日間を指定すると、
    /// 「円あり行のみ・複数レート」の <see cref="UsageFormatting.FormatSummaryRateReference"/> の
    /// 分岐（レートの日付範囲・件数を表示する分岐）を画面上で確認できる
    /// （既存の当日/当週/当月/全期間には必ず円欠損行が混ざるため、この分岐を踏めなかった）。
    /// </summary>
    internal static IReadOnlyList<SeedRow> BuildRows(DateTimeOffset now)
    {
        DateOnly today = DateOnly.FromDateTime(now.LocalDateTime);
        DateOnly monday = DateOnly.FromDateTime(UsagePeriod.StartOfWeek(now).LocalDateTime);
        DateOnly firstOfMonth = DateOnly.FromDateTime(UsagePeriod.StartOfMonth(now).LocalDateTime);

        // 「当週だが当日ではない」日。週が始まったばかり（今日が月曜）だと該当日が無いので、
        // その場合は当日と同じ日にフォールバックする（Run() が投入結果に明記する）。
        DateOnly weekOtherDay = monday < today ? monday : today;

        // 「当月だが当週ではない」日。月初が今週に入っている場合は該当日が無いので、
        // weekOtherDay にフォールバックする。
        DateOnly monthOtherDay =
            firstOfMonth < monday
                ? (firstOfMonth.AddDays(1) < monday ? firstOfMonth.AddDays(1) : firstOfMonth)
                : weekOtherDay;

        DateOnly lastMonthDayA = firstOfMonth.AddDays(-5);
        DateOnly lastMonthDayB = firstOfMonth.AddDays(-12);

        // 前月のさらに前（既存の lastMonthDayA/B と重ならない）に確保した、円欠損行を一切
        // 含まない連続2日間。カスタム期間の複数レート表示だけを踏むための専用の日。
        (DateOnly multiRateOnlyDay1, DateOnly multiRateOnlyDay2) = MultiRateOnlyRange(now);

        FxRate rateA = new(today.AddDays(-3), 155.32m, AtLocal(today.AddDays(-3), 8, 0));
        FxRate rateB = new(firstOfMonth.AddDays(-12), 149.87m, AtLocal(firstOfMonth.AddDays(-12), 8, 0));
        FxRate rateC = new(multiRateOnlyDay1, 152.10m, AtLocal(multiRateOnlyDay1, 8, 0));
        FxRate rateD = new(multiRateOnlyDay2, 148.05m, AtLocal(multiRateOnlyDay2, 8, 0));

        const string http429 = "Gemini APIがHTTP 429を返しました。";
        const string http500 = "Gemini APIがHTTP 500を返しました。";
        const string http503 = "Gemini APIがHTTP 503を返しました。";
        const string connectFailed = "Gemini APIへ接続できませんでした。";
        const string timeout = "Gemini APIへの接続が15秒以内に完了しませんでした。";

        return
        [
            // ---- 複数レート専用範囲（円欠損行を含まない連続2日間。カスタム期間で
            //      multiRateOnlyDay1〜multiRateOnlyDay2 を指定すると、複数レートの
            //      日付範囲・件数表示だけを踏める） ----
            Row(multiRateOnlyDay1, 9, 30, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                26, 13, 0.00003000m, 1000, 1, 0, rateC, null, "複数レート範囲/成功/レートC"),
            Row(multiRateOnlyDay1, 17, 45, ApiCallTrigger.Auto, ApiCallStatus.Ok,
                20, 10, 0.00002400m, 950, 1, 0, rateC, null, "複数レート範囲/成功/レートC"),
            Row(multiRateOnlyDay2, 10, 15, ApiCallTrigger.Realternative, ApiCallStatus.Ok,
                58, 40, 0.00007800m, 1600, 1, 0, rateD, null, "複数レート範囲/成功/レートD/別案生成"),
            Row(multiRateOnlyDay2, 19, 0, ApiCallTrigger.StyleGuide, ApiCallStatus.Ok,
                430, 280, 0.00081000m, 3600, 0, 0, rateD, null, "複数レート範囲/成功/レートD/スタイルガイド"),

            // ---- 前月（全期間のみでヒットし、当月合計には含まれないことを確認する） ----
            Row(lastMonthDayB, 9, 0, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                40, 20, 0.00004500m, 1200, 3, 0, null, null, "前月/成功/円欠損"),
            Row(lastMonthDayB, 14, 30, ApiCallTrigger.Auto, ApiCallStatus.Ok,
                15, 8, 0.00001800m, 900, 1, 0, rateB, null, "前月/成功/レートB"),
            Row(lastMonthDayB, 20, 0, ApiCallTrigger.Realternative, ApiCallStatus.Ok,
                90, 66, 0.00014000m, 2200, 2, 0, null, null, "前月/成功/円欠損/別案生成"),
            Row(lastMonthDayA, 10, 0, ApiCallTrigger.StyleGuide, ApiCallStatus.Ok,
                500, 320, 0.00095000m, 4200, 0, 0, rateB, null, "前月/成功/レートB/スタイルガイド"),
            Row(lastMonthDayA, 16, 45, ApiCallTrigger.Auto, ApiCallStatus.Error,
                6, 0, 0.00000700m, 650, 0, 0, null, http500, "前月/エラー500"),

            // ---- 当月・当週より前（当月合計には入るが、当週合計には入らないことを確認する） ----
            Row(monthOtherDay, 8, 15, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                22, 11, 0.00002600m, 1000, 2, 0, rateA, null, "当月(週外)/成功/レートA"),
            Row(monthOtherDay, 9, 40, ApiCallTrigger.Auto, ApiCallStatus.Timeout,
                3, 0, 0.00000350m, 15200, 0, 1, null, timeout, "当月(週外)/タイムアウト"),
            Row(monthOtherDay, 13, 5, ApiCallTrigger.Realternative, ApiCallStatus.Ok,
                60, 45, 0.00008200m, 1800, 1, 0, rateA, null, "当月(週外)/成功/レートA/別案生成"),
            Row(monthOtherDay, 18, 20, ApiCallTrigger.Auto, ApiCallStatus.Ok,
                18, 9, 0.00002000m, 950, 1, 1, null, null, "当月(週外)/成功/円欠損"),

            // ---- 当週・当日より前（当週合計には入るが、当日合計には入らないことを確認する） ----
            Row(weekOtherDay, 7, 50, ApiCallTrigger.Manual, ApiCallStatus.Error,
                5, 0, 0.00000600m, 500, 0, 0, null, http429, "当週(当日外)/エラー429"),
            Row(weekOtherDay, 11, 30, ApiCallTrigger.Auto, ApiCallStatus.Ok,
                28, 14, 0.00003300m, 1100, 2, 0, rateA, null, "当週(当日外)/成功/レートA"),
            Row(weekOtherDay, 15, 10, ApiCallTrigger.StyleGuide, ApiCallStatus.Ok,
                480, 300, 0.00088000m, 3900, 0, 0, rateA, null, "当週(当日外)/成功/レートA/スタイルガイド"),
            Row(weekOtherDay, 20, 5, ApiCallTrigger.Realternative, ApiCallStatus.Timeout,
                4, 0, 0.00000450m, 15100, 0, 1, null, timeout, "当週(当日外)/タイムアウト/別案生成"),

            // ---- 当日（種別4種・成功/エラー/タイムアウト・円あり/円欠損を一通り含む） ----
            Row(today, 8, 5, ApiCallTrigger.Auto, ApiCallStatus.Ok,
                16, 8, 0.00001800m, 850, 1, 0, rateA, null, "当日/成功/レートA"),
            Row(today, 9, 12, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                24, 12, 0.00002800m, 980, 2, 0, rateA, null, "当日/成功/レートA(実データと同型)"),
            Row(today, 10, 47, ApiCallTrigger.Auto, ApiCallStatus.Error,
                7, 0, 0.00000800m, 600, 0, 0, null, connectFailed, "当日/エラー(接続不可)"),
            Row(today, 11, 3, ApiCallTrigger.Realternative, ApiCallStatus.Ok,
                70, 55, 0.00010500m, 2100, 1, 0, null, null, "当日/成功/円欠損/別案生成"),
            Row(today, 12, 30, ApiCallTrigger.StyleGuide, ApiCallStatus.Timeout,
                2, 0, 0.00000250m, 15300, 0, 0, null, timeout, "当日/タイムアウト/スタイルガイド"),
            Row(today, 13, 58, ApiCallTrigger.Auto, ApiCallStatus.Ok,
                19, 10, 0.00002200m, 900, 1, 0, rateA, null, "当日/成功/レートA"),
            Row(today, 15, 22, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                33, 21, 0.00004100m, 1300, 3, 0, rateA, null, "当日/成功/レートA"),
            Row(today, 16, 41, ApiCallTrigger.Auto, ApiCallStatus.Error,
                9, 0, 0.00001000m, 700, 0, 0, null, http503, "当日/エラー503"),
            Row(today, 17, 15, ApiCallTrigger.Realternative, ApiCallStatus.Ok,
                55, 38, 0.00007300m, 1700, 1, 0, rateA, null, "当日/成功/レートA/別案生成"),
            Row(today, 18, 2, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                12, 6, 0.00001400m, 800, 1, 1, rateA, null, "当日/成功/レートA/破棄あり"),
            Row(today, 19, 30, ApiCallTrigger.StyleGuide, ApiCallStatus.Ok,
                620, 410, 0.00120000m, 5200, 0, 0, rateA, null, "当日/成功/レートA/スタイルガイド"),
            Row(today, 20, 10, ApiCallTrigger.Auto, ApiCallStatus.Timeout,
                3, 0, 0.00000350m, 15400, 0, 0, null, timeout, "当日/タイムアウト"),
            Row(today, 21, 45, ApiCallTrigger.Manual, ApiCallStatus.Ok,
                14, 7, 0.00001600m, 850, 1, 0, null, null, "当日/成功/円欠損"),
        ];
    }

    /// <summary>
    /// 円欠損行を含まない、複数レート専用の連続2日間（<see cref="BuildRows"/> 参照）。
    /// <see cref="Run"/> の投入結果表示と自己テストの両方から同じ計算を使うために独立させてある。
    /// </summary>
    internal static (DateOnly Day1, DateOnly Day2) MultiRateOnlyRange(DateTimeOffset now)
    {
        DateOnly firstOfMonth = DateOnly.FromDateTime(UsagePeriod.StartOfMonth(now).LocalDateTime);
        return (firstOfMonth.AddDays(-20), firstOfMonth.AddDays(-19));
    }

    /// <summary>
    /// <c>--bulk</c> 用。<see cref="ApiCallRepository.GetHistory"/> の既定 limit（2000件）を超える
    /// データを当日1日に集中させ、どの期間フィルタでも Truncated バナーへ到達できるようにする。
    /// 通常のシードには含めない。
    /// </summary>
    internal static IReadOnlyList<SeedRow> BuildBulkRows(DateTimeOffset now, int count = 2050)
    {
        DateOnly today = DateOnly.FromDateTime(now.LocalDateTime);
        DateTimeOffset start = AtLocal(today, 0, 0, 1);
        List<SeedRow> rows = new(count);

        for (int i = 0; i < count; i++)
        {
            ApiCallTrigger trigger = (ApiCallTrigger)(i % 4);
            rows.Add(new SeedRow(
                start.AddSeconds(i),
                trigger,
                ApiCallStatus.Ok,
                10,
                5,
                0.000001m,
                100,
                1,
                0,
                null,
                null,
                $"bulk/{i}"));
        }

        return rows;
    }

    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine(
                """
                使用法: dotnet run --project PromptValidation -- --seed-billing <dir> [--bulk] [--force]

                  <dir>     シード先ディレクトリ。%APPDATA%\JpScratch そのもの（実パス含む）は拒否する。
                  --bulk    2000件を超えるデータを投入し、課金履歴画面のTruncatedバナーを確認する。
                            既定のシードには含めないため、必要なときだけ明示的に指定する。
                  --force   既に app.db がある場合でも上書きする（既定では拒否する）。

                credentials.dat は作らない。APIキーが無い状態のため、隔離環境から課金APIを呼ぶことはできない。
                """);
            return args.Length == 0 ? 2 : 0;
        }

        string directoryArgument = args[0];
        bool bulk = args.Skip(1).Any(a => a == "--bulk");
        bool force = args.Skip(1).Any(a => a == "--force");
        bool unknownArgs = args.Skip(1).Any(a => a is not ("--bulk" or "--force"));
        if (unknownArgs)
        {
            Console.Error.WriteLine("不明な引数です。--seed-billing <dir> [--bulk] [--force] の形式で指定してください。");
            return 2;
        }

        string targetFull;
        try
        {
            targetFull = Path.GetFullPath(directoryArgument);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"指定されたディレクトリのパスが不正です: {ex.Message}");
            return 2;
        }

        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JpScratch");
        string appDataRootFull;
        try
        {
            appDataRootFull = Path.GetFullPath(appDataRoot);
        }
        catch (Exception)
        {
            appDataRootFull = appDataRoot;
        }

        if (IsProtectedDirectory(targetFull, appDataRootFull))
        {
            Console.Error.WriteLine(
                $"拒否: {targetFull} は実データのディレクトリ（{appDataRootFull}）と同じです。" +
                "隔離用の別ディレクトリを指定してください。");
            return 2;
        }

        Directory.CreateDirectory(targetFull);
        string dbFile = Path.Combine(targetFull, "app.db");
        bool dbExists = File.Exists(dbFile);

        if (ShouldRefuseExistingDatabase(dbExists, force))
        {
            Console.Error.WriteLine(
                $"拒否: {dbFile} は既に存在します。上書きするには --force を明示的に指定してください。");
            return 2;
        }

        if (dbExists && force)
        {
            DeleteIfExists(dbFile);
            DeleteIfExists(dbFile + "-wal");
            DeleteIfExists(dbFile + "-shm");
        }

        DateTimeOffset now = DateTimeOffset.Now;
        IReadOnlyList<SeedRow> rows = bulk ? BuildBulkRows(now) : BuildRows(now);

        List<(long Id, SeedRow Row)> inserted = new(rows.Count);
        using (var database = new Database(dbFile))
        {
            DateTimeOffset currentTimestamp = now;
            var repository = new ApiCallRepository(database, () => currentTimestamp);

            database.InTransaction(_ =>
            {
                foreach (SeedRow row in rows)
                {
                    currentTimestamp = row.CalledAt;
                    long id = repository.Add(new ApiCallLogEntry(
                        row.Trigger,
                        PricingService.DefaultModel,
                        row.PromptTokens,
                        row.OutputTokens,
                        row.UsdCost,
                        row.DurationMilliseconds,
                        row.Status,
                        row.ErrorMessage,
                        row.SuggestionCount,
                        row.DiscardedCount,
                        row.FxRate));
                    inserted.Add((id, row));
                }
            });
        }

        Console.WriteLine($"シード投入先: {dbFile}");
        Console.WriteLine($"件数: {inserted.Count}{(bulk ? "（--bulk）" : "")}");
        Console.WriteLine(
            $"基準日: today={DateOnly.FromDateTime(now.LocalDateTime):yyyy-MM-dd} " +
            $"week-start={UsagePeriod.StartOfWeek(now).LocalDateTime:yyyy-MM-dd} " +
            $"month-start={UsagePeriod.StartOfMonth(now).LocalDateTime:yyyy-MM-dd}");
        if (!bulk)
        {
            (DateOnly multiRateDay1, DateOnly multiRateDay2) = MultiRateOnlyRange(now);
            Console.WriteLine(
                $"複数レート表示の確認用: 課金履歴画面でカスタム期間を " +
                $"{multiRateDay1:yyyy-MM-dd} 〜 {multiRateDay2:yyyy-MM-dd} に指定すると、" +
                "円欠損行を含まない複数レート（2種類）の期間合計表示を確認できます。");
        }
        Console.WriteLine("credentials.dat は作成していません（APIキー未設定のまま）。");

        if (!bulk)
        {
            foreach ((long id, SeedRow row) in inserted)
            {
                string jpy = row.FxRate is null ? "jpy=NULL" : $"jpy=あり(rate={row.FxRate.UsdJpy})";
                string err = row.ErrorMessage is null ? "" : $" err=\"{row.ErrorMessage}\"";
                Console.WriteLine(
                    $"  id={id,4}  {row.CalledAt:yyyy-MM-dd HH:mm zzz}  {row.Trigger,-13} {row.Status,-7} " +
                    $"{jpy,-22} {row.Label}{err}");
            }
        }

        return 0;
    }

    /// <summary>
    /// 投入先が実データのディレクトリ（%APPDATA%\JpScratch）と同一かどうかを判定する純粋関数。
    /// 末尾区切り文字の有無・大文字小文字を無視して比較する。
    /// </summary>
    internal static bool IsProtectedDirectory(string targetFullPath, string appDataRootFullPath)
        => string.Equals(
            Normalize(targetFullPath),
            Normalize(appDataRootFullPath),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>既存app.dbの上書きを拒否すべきかどうかを判定する純粋関数。</summary>
    internal static bool ShouldRefuseExistingDatabase(bool databaseExists, bool forceSpecified)
        => databaseExists && !forceSpecified;

    private static string Normalize(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static SeedRow Row(
        DateOnly date, int hour, int minute,
        ApiCallTrigger trigger, ApiCallStatus status,
        int promptTokens, int outputTokens, decimal usdCost, long durationMs,
        int suggestionCount, int discardedCount,
        FxRate? fxRate, string? errorMessage, string label)
        => new(
            AtLocal(date, hour, minute), trigger, status, promptTokens, outputTokens,
            usdCost, durationMs, suggestionCount, discardedCount, fxRate, errorMessage, label);

    private static DateTimeOffset AtLocal(DateOnly date, int hour, int minute, int second = 0)
        => new(new DateTime(date.Year, date.Month, date.Day, hour, minute, second, DateTimeKind.Local));
}
