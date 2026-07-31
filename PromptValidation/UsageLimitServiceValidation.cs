using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="UsageLimitService"/>（判定・進捗率）と <see cref="UsageLimitNotificationTracker"/>
/// （通知抑止）の自己テスト。要件 3.6.3（課金ガード・発火条件5）の境界値、月境界での繰り越し、
/// 月替り／上限額変更での通知再解禁を確かめる。APIは呼ばない。
/// </summary>
internal static class UsageLimitServiceValidation
{
    internal static bool RunSelfTests()
    {
        bool boundaryPassed = RunBoundaryTests();
        bool progressPassed = RunProgressTests();
        bool notificationPassed = RunNotificationTests();
        bool monthRolloverPassed = RunMonthRolloverIntegrationTest();
        bool deliveryGatedPassed = RunDeliveryGatedNotificationTests();

        bool passed = boundaryPassed && progressPassed && notificationPassed &&
            monthRolloverPassed && deliveryGatedPassed;
        Console.WriteLine(
            "月間上限ガード（境界値・進捗率・通知抑止/月替り再解禁）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 月境界の自己テスト。<c>MainWindow</c> が実際に行う経路
    /// （<see cref="UsagePeriod.StartOfMonth"/> で区切った範囲を <c>ApiCallRepository.GetUsageSummary</c>
    /// へ渡し、その結果を <see cref="UsageLimitService"/> で判定する）をそのまま再現し、
    /// 前月の使用量が当月累計へ繰り越されないこと、月が変わると到達状態が自然に解除されることを確認する。
    /// </summary>
    private static bool RunMonthRolloverIntegrationTest()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchUsageLimitMonthRolloverValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            DateTimeOffset timestamp = At(2026, 6, 30, 23, 0);
            var repository = new ApiCallRepository(database, () => timestamp);

            long AddAt(DateTimeOffset at, ApiCallLogEntry entry)
            {
                timestamp = at;
                return repository.Add(entry);
            }

            const decimal limit = 2.00m;

            // 前月（6月）にすでに上限を超える額を使っていても、7月の判定には影響しない。
            AddAt(At(2026, 6, 30, 23, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 1000, 1000,
                5.00m, 100, ApiCallStatus.Ok, null, 1, 0));
            // 7月分は警告閾値未満。
            AddAt(At(2026, 7, 5, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 100, 100,
                1.00m, 50, ApiCallStatus.Ok, null, 1, 0));

            DateTimeOffset julyNow = At(2026, 7, 5, 10, 0);
            ApiCallUsageSummary julyMonth = repository.GetUsageSummary(
                UsagePeriod.StartOfMonth(julyNow), LocalStartOfNextMonth(julyNow));
            bool julyNotCarriedOver =
                julyMonth.UsdCost == 1.00m &&
                UsageLimitService.Evaluate(julyMonth.UsdCost, limit, 0.80m) == UsageLimitState.Normal;

            // 7月分をさらに追加して上限へ到達させる。
            AddAt(At(2026, 7, 20, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 100, 100,
                1.50m, 50, ApiCallStatus.Ok, null, 1, 0));
            ApiCallUsageSummary julyMonthReached = repository.GetUsageSummary(
                UsagePeriod.StartOfMonth(julyNow), LocalStartOfNextMonth(julyNow));
            bool julyReached =
                UsageLimitService.IsReached(julyMonthReached.UsdCost, limit);

            // 8月になった瞬間、当月境界が翌月へ移り、7月分の到達状態は繰り越されない。
            DateTimeOffset augustNow = At(2026, 8, 1, 0, 30);
            ApiCallUsageSummary augustMonth = repository.GetUsageSummary(
                UsagePeriod.StartOfMonth(augustNow), LocalStartOfNextMonth(augustNow));
            bool augustReset =
                augustMonth.UsdCost == 0m &&
                !UsageLimitService.IsReached(augustMonth.UsdCost, limit);

            bool passed = julyNotCarriedOver && julyReached && augustReset;
            Console.WriteLine(
                "  月境界（前月繰り越しなし・当月到達・翌月ロールオーバーで解除）: " +
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

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

    // MainWindow.LocalStartOfNextMonth と同じ計算（PromptValidationはWPFの型を参照できないため複製）。
    private static DateTimeOffset LocalStartOfNextMonth(DateTimeOffset value)
    {
        DateTime localStart = new(
            value.LocalDateTime.Year, value.LocalDateTime.Month, 1, 0, 0, 0, DateTimeKind.Local);
        return new DateTimeOffset(localStart.AddMonths(1));
    }

    private static bool RunBoundaryTests()
    {
        const decimal limit = 2.00m;
        const decimal warningRatio = 0.80m;

        // 上限未満（警告閾値未満）: 通常
        bool belowWarning =
            UsageLimitService.Evaluate(1.00m, limit, warningRatio) == UsageLimitState.Normal &&
            !UsageLimitService.IsReached(1.00m, limit);

        // 警告閾値ちょうど（80% = $1.60）: 警告
        bool atWarningExact =
            UsageLimitService.Evaluate(1.60m, limit, warningRatio) == UsageLimitState.Warning &&
            !UsageLimitService.IsReached(1.60m, limit);

        // 警告閾値のすぐ手前: 通常のまま
        bool justBelowWarning =
            UsageLimitService.Evaluate(1.599999m, limit, warningRatio) == UsageLimitState.Normal;

        // 上限ちょうど: 到達（「以上」で判定するため、ちょうどでも到達扱い）
        bool atLimitExact =
            UsageLimitService.Evaluate(2.00m, limit, warningRatio) == UsageLimitState.Reached &&
            UsageLimitService.IsReached(2.00m, limit);

        // 上限超過: 到達
        bool overLimit =
            UsageLimitService.Evaluate(2.50m, limit, warningRatio) == UsageLimitState.Reached &&
            UsageLimitService.IsReached(2.50m, limit);

        // 上限0（無制限）: 当月累計がどれだけ大きくても常に通常・未到達
        bool unlimitedZero =
            UsageLimitService.Evaluate(1_000_000m, 0m, warningRatio) == UsageLimitState.Normal &&
            !UsageLimitService.IsReached(1_000_000m, 0m);

        // 負の上限も「無制限」と同じ扱い（SettingsServiceの正規化で本来0へ倒すが、
        // 万一そのまま渡ってもガードが誤発火しないことを確認する）。
        bool unlimitedNegative =
            UsageLimitService.Evaluate(5.00m, -1m, warningRatio) == UsageLimitState.Normal &&
            !UsageLimitService.IsReached(5.00m, -1m);

        bool passed = belowWarning && atWarningExact && justBelowWarning &&
            atLimitExact && overLimit && unlimitedZero && unlimitedNegative;

        Console.WriteLine(
            "  境界値（未満/警告ちょうど/上限ちょうど/超過/無制限0/無制限負値）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunProgressTests()
    {
        // 進捗率は0〜100にクランプし、上限0（無制限）はnull（バー非表示の合図）。
        bool zero = UsageLimitService.ProgressPercent(0m, 2.00m) == 0d;
        bool half = UsageLimitService.ProgressPercent(1.00m, 2.00m) == 50d;
        bool full = UsageLimitService.ProgressPercent(2.00m, 2.00m) == 100d;
        bool overClamped = UsageLimitService.ProgressPercent(5.00m, 2.00m) == 100d;
        bool unlimited = UsageLimitService.ProgressPercent(1.00m, 0m) is null;

        bool passed = zero && half && full && overClamped && unlimited;
        Console.WriteLine(
            "  進捗率（0/50/100/超過クランプ/無制限null）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunNotificationTests()
    {
        var tracker = new UsageLimitNotificationTracker();
        DateTimeOffset julyLate = new(2026, 7, 30, 23, 0, 0, TimeSpan.FromHours(9));
        DateTimeOffset augustEarly = new(2026, 8, 1, 0, 30, 0, TimeSpan.FromHours(9));
        const decimal limit = 2.00m;

        // 初回は通知してよい
        bool firstShouldNotify = tracker.ShouldNotify(julyLate, limit);
        tracker.MarkNotified(julyLate, limit);

        // 同月・同上限額のまま、入力停止のたびに再判定されても再通知しない
        // （デバウンス後の自動チェックが繰り返しガードへ引っかかるケースを模す）。
        bool sameMonthSuppressed =
            !tracker.ShouldNotify(julyLate.AddMinutes(1), limit) &&
            !tracker.ShouldNotify(julyLate.AddHours(2), limit);

        // 月が変わったら再度通知できる（翌月1日00:00ロールオーバーでの解除）。
        bool nextMonthReenabled = tracker.ShouldNotify(augustEarly, limit);

        // 月をまたいでも、上限額が同じまま「通知済み」を更新していなければ
        // 同月内での抑止状態は保たれる（マークし直すまでは変わらない）ことも確認する。
        var trackerForLimitChange = new UsageLimitNotificationTracker();
        trackerForLimitChange.MarkNotified(julyLate, limit);
        bool sameMonthDifferentLimitReenabled =
            trackerForLimitChange.ShouldNotify(julyLate.AddMinutes(1), 3.00m);

        // Resetで明示的に解除できる。
        var trackerForReset = new UsageLimitNotificationTracker();
        trackerForReset.MarkNotified(julyLate, limit);
        trackerForReset.Reset();
        bool resetReenabled = trackerForReset.ShouldNotify(julyLate.AddMinutes(1), limit);

        bool passed = firstShouldNotify && sameMonthSuppressed && nextMonthReenabled &&
            sameMonthDifferentLimitReenabled && resetReenabled;

        Console.WriteLine(
            "  通知抑止（初回可・同月同上限は抑止・月替り再解禁・上限額変更で再解禁・Reset）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 「先にマークしてから撃つ」順序で実機を踏んだ不具合の再発防止テスト。
    /// <c>MainWindow.NotifyMonthlyLimitReachedIfNeeded</c> は
    /// <c>TrayIconService.ShowMessage</c>（tray未初期化なら <c>false</c> を返す）の戻り値を見てから
    /// <see cref="UsageLimitNotificationTracker.MarkNotified"/> を呼ぶ契約になっている。
    /// ここではWPFの<c>MainWindow</c>を介さず、その契約（配信できなかった回はMarkしない・
    /// 配信できた回だけMarkする）を <see cref="UsageLimitNotificationTracker"/> に対して
    /// 直接シミュレートし、「起動時点で上限到達済みでも、trayが使えるようになった後の
    /// 再評価で必ず1回通知される」ことを確認する。
    /// </summary>
    private static bool RunDeliveryGatedNotificationTests()
    {
        var tracker = new UsageLimitNotificationTracker();
        DateTimeOffset startupTime = new(2026, 7, 30, 9, 0, 0, TimeSpan.FromHours(9));
        DateTimeOffset trayReadyTime = startupTime.AddMilliseconds(50);
        const decimal limit = 2.00m;

        // TrayIconService.ShowMessage の戻り値（tray未初期化ならfalse）を模した、
        // 呼び出し側が制御する配信結果。MainWindow.NotifyMonthlyLimitReachedIfNeeded の契約
        // 「戻り値を見てからMarkNotifiedを呼ぶ」を、定数条件による到達不能コード警告を避けつつ再現する。
        bool NotifyIfNeeded(DateTimeOffset now, bool trayAvailable)
        {
            if (!tracker.ShouldNotify(now, limit)) return false;
            bool delivered = trayAvailable; // ShowMessageの戻り値に相当
            if (delivered) tracker.MarkNotified(now, limit);
            return delivered;
        }

        // MainWindowのコンストラクタ内の初回評価（tray未初期化 = 配信失敗を模す）。
        bool shouldNotifyAtStartup = tracker.ShouldNotify(startupTime, limit);
        bool deliveredAtStartup = NotifyIfNeeded(startupTime, trayAvailable: false);

        // 配信できなかったので、直後に再評価してもまだ「通知してよい」ままのはず
        // （ここでtrueのままなことが、起動直後に通知の機会を失っていないことの核心）。
        bool stillPendingAfterFailedDelivery = tracker.ShouldNotify(trayReadyTime, limit);

        // App.OnStartupがtray初期化直後にRecheckUsageLimitNotificationAfterTrayReadyを呼ぶ
        // ケース（今度は配信できる）を模す。ここで初めてMarkNotifiedが呼ばれる。
        bool deliveredAfterTrayReady = NotifyIfNeeded(trayReadyTime, trayAvailable: true);

        // 以降、同月・同上限額のままの再評価では通知を繰り返さない。
        bool suppressedAfterSuccessfulDelivery =
            !tracker.ShouldNotify(trayReadyTime.AddMinutes(5), limit);

        bool passed = shouldNotifyAtStartup && !deliveredAtStartup &&
            stillPendingAfterFailedDelivery && deliveredAfterTrayReady &&
            suppressedAfterSuccessfulDelivery;

        Console.WriteLine(
            "  配信ゲート（tray未準備で配信失敗時はMarkせず、後続の配信成功時に初めて抑止・機会を失わない）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
