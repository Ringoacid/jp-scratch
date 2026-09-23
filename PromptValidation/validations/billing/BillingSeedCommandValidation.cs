using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="BillingSeedCommand"/>（課金履歴画面の目視確認用シード）の自己テスト。
/// 一時ディレクトリと <see cref="ApiCallRepository"/> だけを使い、実 API・実データには一切触れない。
/// </summary>
internal static class BillingSeedCommandValidation
{
    internal static bool RunSelfTests()
    {
        bool structuralPass = RunStructuralSelfTests();
        bool guardPass = RunGuardSelfTests();
        bool seedFlowPass = RunSeedFlowSelfTests();
        bool multiRateRangePass = RunMultiRateOnlyRangeSelfTests();
        bool retentionRangePass = RunRetentionCheckRangeSelfTests();
        bool passed = structuralPass && guardPass && seedFlowPass && multiRateRangePass &&
            retentionRangePass;
        Console.WriteLine("課金シード（構造・保護ガード・投入結果の一致・複数レート専用範囲・" +
            "圧縮確認用の古い明細）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunStructuralSelfTests()
    {
        DateTimeOffset fixedNow = new(2026, 7, 30, 15, 0, 0, TimeSpan.FromHours(9));
        IReadOnlyList<BillingSeedCommand.SeedRow> rows = BillingSeedCommand.BuildRows(fixedNow);

        bool countInRange = rows.Count is >= 20 and <= 40;
        bool hasAllTriggers =
            rows.Any(r => r.Trigger == ApiCallTrigger.Auto) &&
            rows.Any(r => r.Trigger == ApiCallTrigger.Manual) &&
            rows.Any(r => r.Trigger == ApiCallTrigger.Realternative) &&
            rows.Any(r => r.Trigger == ApiCallTrigger.StyleGuide);
        bool hasAllStatuses =
            rows.Any(r => r.Status == ApiCallStatus.Ok) &&
            rows.Any(r => r.Status == ApiCallStatus.Error) &&
            rows.Any(r => r.Status == ApiCallStatus.Timeout);
        bool hasJpyNullSuccessRow = rows.Any(r => r.Status == ApiCallStatus.Ok && r.FxRate is null);
        bool errorAndTimeoutHaveMessages = rows
            .Where(r => r.Status is ApiCallStatus.Error or ApiCallStatus.Timeout)
            .All(r => !string.IsNullOrWhiteSpace(r.ErrorMessage));
        int distinctRates = rows
            .Where(r => r.FxRate is not null)
            .Select(r => (r.FxRate!.UsdJpy, r.FxRate.RateDate))
            .Distinct()
            .Count();
        bool hasMultipleDistinctRates = distinctRates >= 2;
        bool suggestionAndDiscardedVary =
            rows.Select(r => r.SuggestionCount).Distinct().Count() > 1 &&
            rows.Select(r => r.DiscardedCount).Distinct().Count() > 1;
        // 決定的であること: 同じ now を渡せば常に同じ結果になる。
        bool deterministic = BillingSeedCommand.BuildRows(fixedNow).SequenceEqual(rows);

        bool passed = countInRange && hasAllTriggers && hasAllStatuses && hasJpyNullSuccessRow &&
            errorAndTimeoutHaveMessages && hasMultipleDistinctRates && suggestionAndDiscardedVary &&
            deterministic;

        Console.WriteLine("  構造チェック（件数・種別・成否・円欠損・複数レート・決定性）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunGuardSelfTests()
    {
        const string appDataRoot = @"C:\Users\test\AppData\Roaming\JpScratch";

        bool exactMatchIsProtected =
            BillingSeedCommand.IsProtectedDirectory(appDataRoot, appDataRoot);
        bool trailingSlashIsProtected =
            BillingSeedCommand.IsProtectedDirectory(appDataRoot + @"\", appDataRoot);
        bool caseInsensitiveIsProtected =
            BillingSeedCommand.IsProtectedDirectory(appDataRoot.ToLowerInvariant(), appDataRoot);
        bool differentDirIsNotProtected =
            !BillingSeedCommand.IsProtectedDirectory(appDataRoot + "Isolated", appDataRoot);

        bool refusesWhenExistsWithoutForce =
            BillingSeedCommand.ShouldRefuseExistingDatabase(databaseExists: true, forceSpecified: false);
        bool allowsWhenExistsWithForce =
            !BillingSeedCommand.ShouldRefuseExistingDatabase(databaseExists: true, forceSpecified: true);
        bool allowsWhenNotExists =
            !BillingSeedCommand.ShouldRefuseExistingDatabase(databaseExists: false, forceSpecified: false);

        // ディレクトリを省いて --seed-billing --force と打つと、--force という名前の
        // ディレクトリが作られてしまう（実際にリポジトリ直下へ作ってしまった）。
        bool rejectsForceAsDirectory =
            BillingSeedCommand.IsOptionLikeDirectoryArgument("--force");
        bool rejectsBulkAsDirectory =
            BillingSeedCommand.IsOptionLikeDirectoryArgument("--bulk");
        bool rejectsShortOptionAsDirectory =
            BillingSeedCommand.IsOptionLikeDirectoryArgument("-x");
        bool acceptsRelativePath =
            !BillingSeedCommand.IsOptionLikeDirectoryArgument(@"tmp\seed");
        bool acceptsAbsolutePath =
            !BillingSeedCommand.IsOptionLikeDirectoryArgument(@"C:\tmp\seed");

        bool passed = exactMatchIsProtected && trailingSlashIsProtected && caseInsensitiveIsProtected &&
            differentDirIsNotProtected && refusesWhenExistsWithoutForce && allowsWhenExistsWithForce &&
            allowsWhenNotExists && rejectsForceAsDirectory && rejectsBulkAsDirectory &&
            rejectsShortOptionAsDirectory && acceptsRelativePath && acceptsAbsolutePath;

        Console.WriteLine("  保護ガードチェック（%APPDATA%\\JpScratch拒否・既存app.dbの上書き拒否・" +
            "オプションをディレクトリとして扱わない）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunSeedFlowSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchBillingSeedValidation", Guid.NewGuid().ToString("N"));

        try
        {
            DateTimeOffset fixedNow = new(2026, 7, 30, 15, 0, 0, TimeSpan.FromHours(9));
            IReadOnlyList<BillingSeedCommand.SeedRow> expectedRows =
                BillingSeedCommand.BuildRows(fixedNow);

            Directory.CreateDirectory(directory);
            string databaseFile = Path.Combine(directory, "app.db");

            DateTimeOffset current = fixedNow;
            bool passed;
            using (var database = new Database(databaseFile))
            {
                var repository = new ApiCallRepository(database, () => current);
                foreach (BillingSeedCommand.SeedRow row in expectedRows)
                {
                    current = row.CalledAt;
                    repository.Add(new ApiCallLogEntry(
                        row.Trigger, PricingService.DefaultModel, row.PromptTokens, row.OutputTokens,
                        row.UsdCost, row.DurationMilliseconds, row.Status, row.ErrorMessage,
                        row.SuggestionCount, row.DiscardedCount, row.FxRate));
                }

                ApiCallHistoryPage all = repository.GetHistory();
                ApiCallUsageSummary summary = repository.GetUsageSummary();

                int expectedOk = expectedRows.Count(r => r.Status == ApiCallStatus.Ok);
                int expectedError = expectedRows.Count(r => r.Status == ApiCallStatus.Error);
                int expectedTimeout = expectedRows.Count(r => r.Status == ApiCallStatus.Timeout);
                int expectedDistinctRates = expectedRows
                    .Where(r => r.FxRate is not null)
                    .Select(r => (r.FxRate!.UsdJpy, r.FxRate.RateDate))
                    .Distinct()
                    .Count();
                bool expectedJpyComplete =
                    expectedRows.All(r => r.FxRate is not null || r.UsdCost == 0m);
                int expectedJpyNullRows = expectedRows.Count(r => r.FxRate is null);
                int actualJpyNullRows = all.Rows.Count(r => r.JpyCost is null);

                bool countMatches = all.TotalCount == expectedRows.Count && !all.Truncated;
                bool statusCountsMatch =
                    summary.OkCalls == expectedOk && summary.ErrorCalls == expectedError &&
                    summary.TimeoutCalls == expectedTimeout &&
                    summary.TotalCalls == expectedRows.Count;
                bool distinctRateMatches = summary.DistinctRateCount == expectedDistinctRates;
                bool jpyCompleteMatches = summary.IsJpyComplete == expectedJpyComplete;
                bool jpyNullCountMatches = actualJpyNullRows == expectedJpyNullRows;
                bool sortedDescending = all.Rows
                    .Zip(all.Rows.Skip(1), (a, b) => a.CalledAt >= b.CalledAt)
                    .All(ok => ok);
                bool errorRowsHaveMessages = all.Rows
                    .Where(r => r.Status is ApiCallStatus.Error or ApiCallStatus.Timeout)
                    .All(r => !string.IsNullOrWhiteSpace(r.ErrorMessage));

                passed = countMatches && statusCountsMatch && distinctRateMatches &&
                    jpyCompleteMatches && jpyNullCountMatches && sortedDescending &&
                    errorRowsHaveMessages;
            }

            Console.WriteLine(
                "  投入結果チェック（GetHistory/GetUsageSummaryがBuildRowsと一致・並び順・NULL円）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 課金指摘2の再発防止テスト。<see cref="BillingSeedCommand.MultiRateOnlyRange"/> が示す2日間を
    /// カスタム期間として <see cref="ApiCallRepository.GetUsageSummary"/> に渡すと、円欠損行を含まず
    /// （<c>IsJpyComplete == true</c>）、複数レート（<c>DistinctRateCount &gt;= 2</c>）になることを確認する。
    /// これが崩れると、ユーザーがこの範囲をカスタム指定しても
    /// <see cref="UsageFormatting.FormatSummaryRateReference"/> の複数レート分岐を画面上で踏めなくなる。
    /// </summary>
    private static bool RunMultiRateOnlyRangeSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchBillingSeedMultiRateValidation", Guid.NewGuid().ToString("N"));

        try
        {
            DateTimeOffset fixedNow = new(2026, 7, 30, 15, 0, 0, TimeSpan.FromHours(9));
            IReadOnlyList<BillingSeedCommand.SeedRow> rows = BillingSeedCommand.BuildRows(fixedNow);
            (DateOnly day1, DateOnly day2) = BillingSeedCommand.MultiRateOnlyRange(fixedNow);

            Directory.CreateDirectory(directory);
            string databaseFile = Path.Combine(directory, "app.db");

            bool passed;
            using (var database = new Database(databaseFile))
            {
                DateTimeOffset current = fixedNow;
                var repository = new ApiCallRepository(database, () => current);
                foreach (BillingSeedCommand.SeedRow row in rows)
                {
                    current = row.CalledAt;
                    repository.Add(new ApiCallLogEntry(
                        row.Trigger, PricingService.DefaultModel, row.PromptTokens, row.OutputTokens,
                        row.UsdCost, row.DurationMilliseconds, row.Status, row.ErrorMessage,
                        row.SuggestionCount, row.DiscardedCount, row.FxRate));
                }

                // day1の0時0分 〜 day2の翌日0時0分（半開区間）＝カスタム期間画面が
                // 「day1〜day2」と指定したときに渡す範囲と同じ計算。
                DateTimeOffset from = new(new DateTime(day1.Year, day1.Month, day1.Day, 0, 0, 0, DateTimeKind.Local));
                DateOnly dayAfterDay2 = day2.AddDays(1);
                DateTimeOffset to = new(new DateTime(
                    dayAfterDay2.Year, dayAfterDay2.Month, dayAfterDay2.Day, 0, 0, 0, DateTimeKind.Local));

                ApiCallUsageSummary summary = repository.GetUsageSummary(from, to);

                bool hasCalls = summary.TotalCalls > 0;
                bool jpyComplete = summary.IsJpyComplete;
                bool multipleRates = summary.DistinctRateCount >= 2;
                bool datesAreConsecutive = day2 == day1.AddDays(1);

                passed = hasCalls && jpyComplete && multipleRates && datesAreConsecutive;
            }

            Console.WriteLine(
                $"  複数レート専用範囲（{day1:yyyy-MM-dd}〜{day2:yyyy-MM-dd}がIsJpyComplete=true・" +
                "DistinctRateCount>=2）: " + (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 実機確認で「保持期間を1か月にしても何も圧縮されない」と分かった件の再発防止テスト。
    ///
    /// 原因はシード側にあった。<see cref="ApiLogRetention.ComputeCutoff"/> の境界は
    /// 「当月から N か月前の月初」なので、保持期間 1 か月でも前月の明細は残る。ところが
    /// シードの最古は前月11日だったため、**どの保持期間を指定しても圧縮対象が0件**で、
    /// 画面で確認しようがなかった。
    ///
    /// ここでは (a) 既定の12か月では1件も圧縮されない、(b) 3か月にすると
    /// <see cref="BillingSeedCommand.RetentionCheckRange"/> の OldDay だけが圧縮される、
    /// (c) 1か月にすると OldDay と MidDay の両方が圧縮される、(d) いずれの場合も
    /// 全期間の集計は <c>CompactedCalls</c> を除いて完全に一致する、を確認する。
    /// </summary>
    private static bool RunRetentionCheckRangeSelfTests()
    {
        DateTimeOffset fixedNow = new(2026, 7, 30, 15, 0, 0, TimeSpan.FromHours(9));
        IReadOnlyList<BillingSeedCommand.SeedRow> rows = BillingSeedCommand.BuildRows(fixedNow);
        (DateOnly oldDay, DateOnly midDay) = BillingSeedCommand.RetentionCheckRange(fixedNow);

        int oldDayRows = rows.Count(r => DateOnly.FromDateTime(r.CalledAt.LocalDateTime) == oldDay);
        int midDayRows = rows.Count(r => DateOnly.FromDateTime(r.CalledAt.LocalDateTime) == midDay);
        bool bothDaysPopulated = oldDayRows > 0 && midDayRows > 0;

        bool defaultKeepsEverything = CompactedCountAt(rows, fixedNow, retentionMonths: 12) == 0;
        bool threeMonthsCompactsOldOnly =
            CompactedCountAt(rows, fixedNow, retentionMonths: 3) == oldDayRows;
        bool oneMonthCompactsBoth =
            CompactedCountAt(rows, fixedNow, retentionMonths: 1) == oldDayRows + midDayRows;

        bool totalsPreserved = TotalsSurviveCompaction(rows, fixedNow, retentionMonths: 1);

        bool passed = bothDaysPopulated && defaultKeepsEverything && threeMonthsCompactsOldOnly &&
            oneMonthCompactsBoth && totalsPreserved;

        Console.WriteLine(
            $"  圧縮確認用の古い明細（{oldDay:yyyy-MM-dd}に{oldDayRows}件・{midDay:yyyy-MM-dd}に{midDayRows}件。" +
            "12か月では0件・3か月では前者のみ・1か月では両方が対象で合計不変）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>指定した保持期間で圧縮対象になるシード行の件数を、境界計算だけで数える。</summary>
    private static int CompactedCountAt(
        IReadOnlyList<BillingSeedCommand.SeedRow> rows, DateTimeOffset now, int retentionMonths)
    {
        DateTimeOffset? cutoff = ApiLogRetention.ComputeCutoff(now, retentionMonths);
        return cutoff is null ? 0 : rows.Count(r => r.CalledAt < cutoff.Value);
    }

    /// <summary>
    /// シードを実際に投入して圧縮し、全期間の集計が <c>CompactedCalls</c> を除いて
    /// 完全一致することを確かめる。<see cref="ApiLogCompactionValidation"/> は合成データで
    /// 同じ不変条件を見ているが、こちらは画面確認に使う実物のシードで確かめる。
    /// </summary>
    private static bool TotalsSurviveCompaction(
        IReadOnlyList<BillingSeedCommand.SeedRow> rows, DateTimeOffset now, int retentionMonths)
    {
        DateTimeOffset? cutoff = ApiLogRetention.ComputeCutoff(now, retentionMonths);
        if (cutoff is null) return false;

        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchBillingSeedRetentionValidation", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            using var database = new Database(Path.Combine(directory, "app.db"));

            DateTimeOffset current = now;
            var repository = new ApiCallRepository(database, () => current);
            foreach (BillingSeedCommand.SeedRow row in rows)
            {
                current = row.CalledAt;
                repository.Add(new ApiCallLogEntry(
                    row.Trigger, PricingService.DefaultModel, row.PromptTokens, row.OutputTokens,
                    row.UsdCost, row.DurationMilliseconds, row.Status, row.ErrorMessage,
                    row.SuggestionCount, row.DiscardedCount, row.FxRate));
            }

            ApiCallUsageSummary before = repository.GetUsageSummary();
            long detailsBefore = repository.GetHistory().TotalCount;

            ApiCallCompactionResult result = repository.Compact(cutoff.Value);

            ApiCallUsageSummary after = repository.GetUsageSummary();
            long detailsAfter = repository.GetHistory().TotalCount;

            return result.DidCompact &&
                (after with { CompactedCalls = 0 }) == before &&
                after.CompactedCalls == result.CompactedCalls &&
                detailsAfter == detailsBefore - result.CompactedCalls;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
