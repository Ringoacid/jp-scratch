using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Services;
using JpScratch.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace JpScratch;

/// <summary>
/// アプリ本体。常駐が前提なので、ウィンドウを閉じても終了せず、トレイの「終了」だけが出口になる。
/// </summary>
public partial class App : Application
{
    private readonly SingleInstance _singleInstance = new();
    private readonly SettingsService _settings = new();
    private readonly ThemeService _theme = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly TrayIconService _tray = new();
    private readonly CredentialService _credentials = new();

    private Database? _database;
    private PricingService? _pricing;
    private ApiCallRepository? _apiCalls;
    private FxRateService? _fxRates;
    private ReactionRepository? _reactions;
    private StyleGuideRepository? _styleGuides;
    private Proofreading.ProofreadingClientRouter? _proofreadingClient;
    private TabManager? _tabs;
    private MainWindow? _window;

    /// <summary>
    /// 既に常駐しているのを見つけて退場するだけのプロセスか。
    /// この場合、設定もタブも読み込んでいないので、終了処理で何も書いてはいけない。
    /// </summary>
    private bool _isDuplicateInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (AppPaths.IsolationFailure is { } isolationPath)
        {
            // 隔離用ディレクトリ（JPSCRATCH_DATA_DIR）を作成できなかった。黙って実データ
            // （%APPDATA%\JpScratch）へ落とすと、同じ app.db を既存の常駐インスタンスと並走して
            // 書く危険があるため、起動を中止してユーザーに直させる。
            MessageBox.Show(
                "JPSCRATCH_DATA_DIR で指定した隔離用ディレクトリを作成できませんでした。\n\n" +
                isolationPath + "\n\n" +
                "実データ（%APPDATA%\\JpScratch）へフォールバックすると別インスタンスとデータを" +
                "同時に書きかねないため、起動を中止します。\n" +
                "環境変数を修正してから起動し直してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (!_singleInstance.TryAcquire())
        {
            // 既に常駐しているので、そちらを前に出して自分は静かに退場する
            _isDuplicateInstance = true;
            SingleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _settings.Load();

        if (_settings.IsFirstRun)
        {
            // インストーラーがスタートアップ登録の初期状態を決めている（installer/Package.wxs）。
            // ここでそれを設定へ取り込むことで、MSI とアプリが登録を奪い合わなくなる。
            _settings.Current.StartWithWindows = StartupRegistration.IsRegistered();
            _settings.SaveNow();
        }

        _theme.Apply(_settings.Current.Theme);
        ConfirmEnvironmentCredentialSource();

        _database = new Database();
        _pricing = new PricingService();
        _apiCalls = new ApiCallRepository(_database);
        _fxRates = new FxRateService(_database);
        _reactions = new ReactionRepository(_database);
        _styleGuides = new StyleGuideRepository(_database);
        _proofreadingClient = new Proofreading.ProofreadingClientRouter(
            _settings,
            _credentials);
        var repository = new TabRepository(_database);
        _tabs = new TabManager(repository, _settings);
        _tabs.Initialize();

        if (_tabs.LoadFailures.Count > 0)
        {
            // 本文ファイルがあるのに読めなかったタブは開かずに残してある。ファイルは無傷なので
            // メモ帳等で直接開けるが、黙って消えると気づけないため起動時に必ず伝える。
            MessageBox.Show(
                $"{_tabs.LoadFailures.Count} 個のタブの本文を読み込めませんでした（ファイルは残っています）。\n\n" +
                TabManager.FormatTitles(_tabs.LoadFailures),
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (_settings.IsReadFailed)
        {
            // settings.json は無傷のまま残っており、この起動は既定値で動く。黙っていると
            // 「設定が初期化された」と誤解したうえで、次の保存で本当に初期化されたと思われる。
            //
            // 直し方は失敗の理由でまるで違う。共有違反なら待つ／再起動するだけで直るが、
            // 文字コードが不正なら再起動しても永久に直らない。同じ文面で済ませると、
            // 後者のユーザーは再起動を繰り返すだけで、掃除・圧縮が止まったまま気づけない。
            bool isEncoding = _settings.ReadFailure == FileReadFailure.InvalidEncoding;

            MessageBox.Show(
                (isEncoding
                    ? "設定ファイル（settings.json）が UTF-8 として読めませんでした。ファイルは" +
                      "無傷のまま残してあるため、既定の設定で動作し、設定の自動保存は行いません。\n\n"
                    : "設定ファイル（settings.json）を読み込めませんでした。ファイルは無傷のまま" +
                      "残してあるため、今回の起動だけ既定の設定で動作し、設定の自動保存は行いません。\n\n") +
                "設定値に依存する破壊的な処理（ゴミ箱の期限削除・課金明細の圧縮・" +
                "スタートアップ登録の同期）はこの起動では行いません。\n" +
                $"一方、校正の月間上限額は既定値（${AppSettings.DefaultMonthlyLimitUsd:0.00}）で" +
                "動作します。これより低い上限を設定していた場合は、下記の方法で直してください。\n\n" +
                (isEncoding
                    ? "外部のエディタで Shift_JIS（ANSI）等として保存された可能性があります。" +
                      "この状態は再起動しても直りません。settings.json を UTF-8 で保存し直すか、" +
                      "設定画面を開いて OK を押してください（OK を押すと、今表示されている" +
                      "既定の設定でファイルを上書きします）。"
                    : "他のアプリ（同期ソフト・ウイルス対策など）がファイルを掴んでいる可能性が" +
                      "あります。アプリを再起動すると元の設定に戻ります。"),
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _window = new MainWindow(
            _settings,
            _theme,
            _tabs,
            repository,
            _hotkeys,
            _credentials,
            _pricing,
            _apiCalls,
            _fxRates,
            _reactions,
            _styleGuides,
            _proofreadingClient,
            _tray);

        // ホットキーはウィンドウの HWND に紐づける。
        // EnsureHandle なら「表示せずに HWND だけ作る」ができるので、常駐開始が速い。
        var handle = new WindowInteropHelper(_window).EnsureHandle();
        _hotkeys.Attach(handle);
        _window.AttachHotkeyHandlers();

        var failures = _hotkeys.Reregister(_settings.Current);

        _tray.ToggleRequested += () => _window?.ToggleVisibility();
        _tray.SettingsRequested += () => _window?.OpenSettings();
        _tray.BillingHistoryRequested += () => _window?.OpenBillingHistory();
        _tray.ExitRequested += ExitApplication;
        _tray.Initialize();
        _tray.SetTooltip($"JP Scratch — {_settings.Current.ToggleHotkey} で表示");

        // MainWindowのコンストラクタはtray初期化より前に走るため、起動時点で月間上限に
        // 既に到達していても、その時点ではトレイ通知を発行できない（TrayIconService.ShowMessage
        // は未初期化なら黙ってfalseを返し、MainWindow側もそれを見て「通知済み」を記録していない）。
        // tray が使えるようになった直後にもう一度だけ評価し直し、取りこぼしを防ぐ。
        _window.RecheckUsageLimitNotificationAfterTrayReady();

        // 後から起動された自分自身に呼び戻してもらうための受け口。
        // Invoke（同期）は UI スレッド側の例外を ThreadPool のコールバックへ再スローするため、
        // BeginInvoke（非同期）にして呼び戻し元スレッドを巻き込まない。終了処理が始まっていると
        // Dispatcher へのキューイング自体が失敗するので、その場合は何もしない。
        _singleInstance.ListenForActivation(() =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.BeginInvoke(() => _window?.ShowAndFocus());
        });

        // 設定を読めなかった起動では、既定値（StartWithWindows=true）でレジストリを書き換えない。
        // テーマやホットキーが既定へ倒れるのはこの起動限りだが、スタートアップ登録は残ってしまう
        // ＝一時的な読み取り失敗が恒久的な設定変更になる（settings.json を上書きしないのと同じ理由）。
        if (!_settings.IsReadFailed)
            StartupRegistration.Sync(_settings.Current.StartWithWindows);

        var startedByWindows = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        if (startedByWindows)
        {
            // OS 起動時はウィンドウを出さない。ただし描画の初回コストは先に払っておく（要件 2.1）。
            _window.WarmUp();
        }
        else
        {
            _window.ShowAndFocus();
        }

        // 為替は補助情報なので、初期表示や校正操作をネットワーク待ちにしない。
        _ = _window.RefreshFxRateAsync();

        // 保持期限を過ぎた課金明細の圧縮（要件 3.6.2）。ウィンドウを出した後に別スレッドで走らせる。
        // 起動経路の同期処理に足すとコールドスタートの実測値（0.63秒）を落としかねない。
        // 設定変更時・日付が変わったときにも同じ処理が必要なので、実装は MainWindow に置いてある。
        _window.CompactApiLogsInBackground();

        if (failures.Count > 0)
        {
            // 黙って効かないのが一番困るので、必ず知らせる（R-7）
            _tray.ShowMessage(
                "ホットキーを登録できませんでした",
                string.Join(Environment.NewLine, failures) + Environment.NewLine + "設定画面から変更できます。",
                isWarning: true);
        }
    }

    private void ExitApplication()
    {
        // 終了確認ダイアログの間はタイマー発火による保存を止める。WPF のモーダルは入れ子の
        // メッセージループを回すため、止めないとダイアログ表示中に自動保存・再試行タイマーが
        // 発火して SaveDirty へ再入する（CLAUDE.md の不変条件）。
        bool exiting = false;
        _tabs?.SuspendAutoSave();
        try
        {
            exiting = TrySaveBeforeExit();
        }
        finally
        {
            // アプリへ戻す場合は再試行を必ず戻す。止めたままだと、終了を取りやめた後に
            // 未保存タブが誰にも保存されなくなる。
            if (!exiting) _tabs?.ResumeAutoSave();
        }

        if (!exiting)
        {
            // 終了はトレイから呼ばれる＝ウィンドウは隠れていることが多い。取りやめても
            // 画面が出てこなければ、ユーザーは原因を直しようがない。
            _window?.ShowAndFocus();
            return;
        }

        Shutdown();
    }

    /// <summary>
    /// 終了前の保存。失敗したら「再試行 / このまま終了 / 終了を取りやめる」を選ばせる。
    /// 戻り値は終了してよいか。
    ///
    /// 警告するだけで必ず終了していた頃は、ユーザーが原因（同期ソフトの一時ロック等）を
    /// 取り除いて保存し直す手段が無かった。「保存」を見せない設計では、ここが編集を守る
    /// 最後の分岐点になる。
    /// </summary>
    private bool TrySaveBeforeExit()
    {
        while (true)
        {
            // 保存失敗で Shutdown() に到達できず「終了できない」状態を避ける。保存そのものは
            // OnExit 側でも再試行される（例外を握って終了処理へ進む）。原因はログへ残す。
            IReadOnlyList<string> failures;
            try
            {
                failures = _tabs?.SaveDirty() ?? [];
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex, "トレイ終了時の保存");
                failures = DirtyTabTitles();
            }

            if (failures.Count == 0) return true;

            // 「保存」を見せない設計なので、ここで黙ると編集が静かに消える。本文ファイルには
            // 前回保存できた内容が残っている＝今回の編集ぶんだけが失われることを正直に伝える。
            // 「保存せず終了」とは書かない。OnExit がもう一度保存を試みるため、そこで成功すれば
            // 失われない（起きるかもしれないことを断定しない）。
            MessageBoxResult choice = MessageBox.Show(
                $"{failures.Count} 個のタブを保存できませんでした。" +
                "本文ファイルには前回保存できた内容が残っています。\n\n" +
                TabManager.FormatTitles(failures) + "\n\n" +
                "同期ソフトやウイルス対策がファイルを掴んでいる可能性があります。\n\n" +
                "［はい］　　　　　もう一度保存する\n" +
                "［いいえ］　　　　このまま終了する（保存できなかった編集は失われます）\n" +
                "［キャンセル］　　終了を取りやめてアプリへ戻る",
                "JP Scratch",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (choice == MessageBoxResult.Yes) continue;
            return choice == MessageBoxResult.No;
        }
    }

    /// <summary>未保存のまま残っているタブのタイトル（＝保存できなかったタブ）。</summary>
    private IReadOnlyList<string> DirtyTabTitles()
        => _tabs?.Tabs.Where(tab => tab.IsDirty).Select(tab => tab.Title).ToArray() ?? [];

    /// <summary>
    /// 環境変数によるAPIキー（GEMINI_API_KEY / OPENAI_API_KEY）が見つかったとき、使うかどうかを
    /// 初回のみ確認する（CLAUDE.md: 「使うかどうかを初回のみ確認する。選択は記憶する」）。
    /// プロバイダーごとに独立して確認し、選択はそれぞれ記憶する。
    /// </summary>
    private void ConfirmEnvironmentCredentialSource()
    {
        // 実際に使うプロバイダーだけを尋ねる。4プロバイダーを無条件に回すと、環境変数を複数
        // 設定しているユーザーが起動のたびに（選んでもいないモデルの分まで）モーダルを次々
        // 見ることになる。自動用と手動用が同じプロバイダーなら 1 回だけになる。
        foreach (Models.ApiProvider provider in InUseProviders())
        {
            ConfirmCredentialSourceIfNeeded(
                provider,
                source: ApiKeySourceOf(provider),
                apply: value => ApplyApiKeySource(provider, value));
        }
    }

    /// <summary>自動用・手動用として現在選ばれているモデルのプロバイダー（重複は除く）。</summary>
    private IEnumerable<Models.ApiProvider> InUseProviders()
        => new[]
            {
                Models.ProofreadingModelCatalog.ProviderOf(_settings.Current.AutoProofreadingModel),
                Models.ProofreadingModelCatalog.ProviderOf(_settings.Current.ManualProofreadingModel),
            }
            .Distinct();

    private Models.ApiKeySource ApiKeySourceOf(Models.ApiProvider provider)
        => provider switch
        {
            Models.ApiProvider.Google => _settings.Current.GeminiApiKeySource,
            Models.ApiProvider.OpenAi => _settings.Current.OpenAiApiKeySource,
            Models.ApiProvider.Anthropic => _settings.Current.AnthropicApiKeySource,
            Models.ApiProvider.PreferredNetworks => _settings.Current.PlamoApiKeySource,
            _ => Models.ApiKeySource.Unspecified,
        };

    private void ApplyApiKeySource(Models.ApiProvider provider, Models.ApiKeySource value)
    {
        switch (provider)
        {
            case Models.ApiProvider.Google: _settings.Current.GeminiApiKeySource = value; break;
            case Models.ApiProvider.OpenAi: _settings.Current.OpenAiApiKeySource = value; break;
            case Models.ApiProvider.Anthropic: _settings.Current.AnthropicApiKeySource = value; break;
            case Models.ApiProvider.PreferredNetworks: _settings.Current.PlamoApiKeySource = value; break;
        }
    }

    private void ConfirmCredentialSourceIfNeeded(
        Models.ApiProvider provider,
        Models.ApiKeySource source,
        Action<Models.ApiKeySource> apply)
    {
        if (source != Models.ApiKeySource.Unspecified ||
            !_credentials.EnvironmentKeyAvailable(provider))
        {
            return;
        }

        var dialog = new CredentialSourceDialog(
            _credentials.StoredKeyState(provider),
            Models.ProofreadingModelCatalog.ProviderDisplayName(provider),
            Models.ProofreadingModelCatalog.EnvironmentVariableName(provider));
        if (dialog.ShowDialog() != true)
        {
            // × や Esc で閉じられた場合も、選択を未指定のままにしない（「初回のみ確認する。
            // 選択は記憶する」の規約）。未指定のままだと毎回の起動で同じダイアログが出る。
            // 既定は「保存済みキーを使う」（ダイアログの既定と同じで、未指定時と同じ動作）
            // として記憶し、設定画面からいつでも変更できる。
            apply(Models.ApiKeySource.Stored);
            _settings.SaveNow();
            return;
        }

        apply(dialog.SelectedSource);
        _settings.SaveNow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 終了時保存（要件 3.2.4）。ここが最後の砦なので、例外で飛ばさない。
        if (!_isDuplicateInstance)
        {
            try
            {
                _tabs?.SaveDirty();
                _settings.SaveNow();
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex, "終了処理");
            }
        }

        _hotkeys.Dispose();
        _tray.Dispose();
        _proofreadingClient?.Dispose();
        _fxRates?.Dispose();
        _database?.Dispose();
        _singleInstance.Dispose();

        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception, "UI スレッド");

        // 本文を道連れにしない。保存してから、続行できるなら続行する。
        IReadOnlyList<string> failures;
        try
        {
            failures = _tabs?.SaveDirty() ?? [];
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex, "クラッシュ時の保存");
            failures = DirtyTabTitles();
        }

        // 保存できたかどうかを実際の結果から書く。ここを固定文言（「保存されています」）に
        // していると、保存失敗が原因でこのハンドラへ来た場合に事実と逆の案内になる。
        string saveState = failures.Count == 0
            ? "編集中の内容は保存されています。"
            : $"次の {failures.Count} 個のタブは保存できませんでした" +
              $"（本文ファイルには前回保存できた内容が残っています）。{Environment.NewLine}" +
              TabManager.FormatTitles(failures);

        // ダイアログの間はタイマー発火による保存を止める。WPF のモーダルは入れ子のメッセージ
        // ループを回すため、止めないと保存失敗後のバックオフ再試行がこのダイアログを読んでいる
        // 最中に消化され、ユーザーが操作できるようになった頃には再試行の上限に達している。
        // e.Handled = true でアプリは続くので、必ず再開する（止めたままだと自動保存が死ぬ）。
        _tabs?.SuspendAutoSave();
        try
        {
            MessageBox.Show(
                $"予期しないエラーが発生しました。{saveState}{Environment.NewLine}{Environment.NewLine}" +
                $"{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"詳細: {AppPaths.CrashLogFile}",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _tabs?.ResumeAutoSave();
        }

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) WriteCrashLog(ex, "バックグラウンドスレッド");
    }

    private static void WriteCrashLog(Exception ex, string context)
    {
        try
        {
            var entry = $"""

                ---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] ----
                {ex}
                """;
            File.AppendAllText(AppPaths.CrashLogFile, entry);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
