using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using JpScratch.Infrastructure;
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

        // 後から起動された自分自身に呼び戻してもらうための受け口
        _singleInstance.ListenForActivation(
            () => Dispatcher.Invoke(() => _window?.ShowAndFocus()));

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
        _tabs?.SaveDirty();
        Shutdown();
    }

    private void ConfirmEnvironmentCredentialSource()
    {
        if (_settings.Current.GeminiApiKeySource != Models.GeminiApiKeySource.Unspecified ||
            !_credentials.EnvironmentKeyAvailable)
        {
            return;
        }

        var dialog = new CredentialSourceDialog(_credentials.StoredKeyState);
        dialog.ShowDialog();

        _settings.Current.GeminiApiKeySource = dialog.SelectedSource;
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
        try
        {
            _tabs?.SaveDirty();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex, "クラッシュ時の保存");
        }

        MessageBox.Show(
            $"予期しないエラーが発生しました。編集中の内容は保存されています。{Environment.NewLine}{Environment.NewLine}" +
            $"{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"詳細: {AppPaths.CrashLogFile}",
            "JP Scratch",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

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
