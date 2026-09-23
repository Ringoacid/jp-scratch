using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using JpScratch.Models;
using JpScratch.Proofreading;
using Microsoft.Win32;

namespace JpScratch.Views;

public partial class SettingsWindow
{
    private readonly SubscriptionService _subscriptions;
    private readonly TextBox _codexPath = new();
    private readonly TextBox _copilotPath = new();
    private readonly CheckBox _subscriptionAuto = new();
    private CancellationTokenSource? _connectionCancellation;
    private readonly List<Action> _updateConnectionControls = [];
    private readonly List<Action> _applySubscriptionStates = [];
    private bool _subscriptionControlsClosed;
    private static BackendKind FamilyBackend(string? family) => family switch { "Codex" => BackendKind.CodexAppServer, "Copilot" => BackendKind.GitHubCopilot, _ => BackendKind.Api };
    private static string SelectionFamily(BackendKind backend, string model) => backend switch { BackendKind.Api => FamilyOf(model), BackendKind.CodexAppServer => "Codex", BackendKind.GitHubCopilot => "Copilot", _ => "未対応" };

    private T Styled<T>(T element, string key) where T : FrameworkElement
    {
        element.SetResourceReference(StyleProperty, key);
        return element;
    }
    private TextBlock Description(string text) => Styled(new TextBlock { Text = text }, "SettingsDescription");
    private Button ConnectionButton(string text) => Styled(new Button { Content = text, Margin = new Thickness(0, 8, 8, 0), MinHeight = 34 }, "AppButton");
    private StackPanel ConnectionRow(StackPanel card, bool last = false)
    {
        var content = new StackPanel();
        card.Children.Add(Styled(new Border { Child = content }, last ? "SettingsLastRow" : "SettingsRow"));
        return content;
    }

    private void OnSubscriptionStateChanged()
    {
        if (_subscriptionControlsClosed) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(OnSubscriptionStateChanged);
            return;
        }
        foreach (var apply in _applySubscriptionStates) apply();
        if (_connectionCancellation is null) RefreshSubscriptionModels();
    }

    private void RefreshSubscriptionModels()
    {
        bool wasLoading = _loadingProofreadingModelControls;
        _loadingProofreadingModelControls = true;
        try
        {
            foreach (var (family, model) in new[] { (AutoModelFamilyCombo, AutoProofreadingModelCombo), (ManualModelFamilyCombo, ManualProofreadingModelCombo) })
                if (family.SelectedItem is string selectedFamily && FamilyBackend(selectedFamily) != BackendKind.Api)
                    PopulateModels(model, selectedFamily, SelectedModelId(model));
        }
        finally { _loadingProofreadingModelControls = wasLoading; }
        RefreshHighCostModelWarning();
    }

    private void InitializeSubscriptionControls()
    {
        _codexPath.Text = _service.Current.CodexCliPath;
        _copilotPath.Text = _service.Current.CopilotCliPath;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 24) };
        System.Windows.Documents.TextElement.SetFontWeight(panel, FontWeights.Normal);
        SettingsTabs.Items.Add(new TabItem
        {
            Header = "契約サービス", Tag = "\uE753",
            ToolTip = "ChatGPT / GitHub Copilot に接続後、「校正」で自動・手動校正のモデルを選びます。",
            Content = new ScrollViewer
            {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        });
        AddConnection(panel, BackendKind.CodexAppServer, _codexPath);
        AddConnection(panel, BackendKind.GitHubCopilot, _copilotPath);
        panel.Children.Add(Styled(new TextBlock { Text = "自動校正" }, "SectionHeader"));
        var autoCard = new StackPanel();
        panel.Children.Add(Styled(new Border { Child = autoCard }, "SettingsCard"));
        var autoRow = ConnectionRow(autoCard, true);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.Children.Add(Styled(new TextBlock { Text = "自動校正で契約枠を使う" }, "FieldLabel"));
        Styled(_subscriptionAuto, "AppToggleCheckBox");
        AutomationProperties.SetName(_subscriptionAuto, "契約枠を使う自動校正を有効にする");
        Grid.SetColumn(_subscriptionAuto, 1); grid.Children.Add(_subscriptionAuto);
        autoRow.Children.Add(grid);
        autoRow.Children.Add(Description("本文の送信で契約枠を消費します。追加請求の有無は契約側の設定によります。"));
        var autoDetails = Styled(new Expander { Header = "自動送信の条件" }, "SettingsExpander");
        autoDetails.Content = Description("入力停止10秒以上・送信間隔30秒以上・同時実行1件。残量が不明な間は自動送信しません。");
        autoRow.Children.Add(autoDetails);
        panel.Children.Add(Description("インストール・アカウント操作は即時反映され、「キャンセル」では取り消せません。"));
    }

    private void AddConnection(StackPanel panel, BackendKind backend, TextBox path)
    {
        panel.Children.Add(Styled(new TextBlock { Text = BackendNames.Display(backend) }, "SectionHeader"));
        var card = new StackPanel();
        panel.Children.Add(Styled(new Border { Child = card }, "SettingsCard"));
        var setup = ConnectionRow(card);
        var setupActions = new WrapPanel(); setup.Children.Add(setupActions);
        var cliText = Description("");
        cliText.VerticalAlignment = VerticalAlignment.Center;
        cliText.Margin = new Thickness(0, 8, 16, 0);
        setupActions.Children.Add(cliText);
        var install = ConnectionButton("CLIをインストール"); setupActions.Children.Add(install);
        var installHelp = Styled(new Expander { Header = "インストールの詳細" }, "SettingsExpander");
        installHelp.Content = Description($"公式GitHubから対応版 CLI {SubscriptionCliInstaller.VersionFor(backend)}（約90〜140 MB）を取得します。JP Scratch専用フォルダーへ導入し、PATHや他のアプリのCLIは変更しません。");
        setup.Children.Add(installHelp);
        var account = ConnectionRow(card);
        var stateText = Styled(new TextBlock
        {
            Text = _subscriptions.State(backend)?.Description ?? "接続未確認",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        }, "FieldLabel");
        AutomationProperties.SetLiveSetting(stateText, AutomationLiveSetting.Polite);
        account.Children.Add(stateText);
        var primaryHelp = Description(""); account.Children.Add(primaryHelp);
        var primary = new WrapPanel(); account.Children.Add(primary);
        var login = ConnectionButton("ブラウザーでログイン"); primary.Children.Add(login);
        var refresh = ConnectionButton("接続を確認"); primary.Children.Add(refresh);
        var cancel = ConnectionButton("この接続操作を中断"); primary.Children.Add(cancel);
        cancel.ToolTip = "今回の待機・取得・ダウンロードだけを中断します。保存済みのログイン情報は削除しません。";
        var progress = new ProgressBar { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 6, 0, 4), Visibility = Visibility.Collapsed };
        progress.SetResourceReference(Control.ForegroundProperty, "AccentBrush"); account.Children.Add(progress);
        var storage = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 8) };
        storage.Children.Add(Styled(new TextBlock { Text = "ログイン情報の保存を許可しますか", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }, "FieldLabel"));
        storage.Children.Add(Description("ログインを完了するには、Copilot CLIがログイン情報を保存する必要があります。このアプリは接続を分離するため、Windows資格情報マネージャーを使いません。"));
        storage.Children.Add(Description("保存すると、CLIがログイン用トークンをアプリ専用フォルダー（LocalAppData\\JpScratchRuntimes）に暗号化せず保存します。このファイルを読める人はトークンを利用できます。文書バックアップには含めません。"));
        var storageActions = new WrapPanel(); storage.Children.Add(storageActions);
        var acceptStorage = ConnectionButton("保存を許可してログインを続ける"); storageActions.Children.Add(acceptStorage);
        var declineStorage = ConnectionButton("保存せず中止"); storageActions.Children.Add(declineStorage);
        account.Children.Add(storage);
        TaskCompletionSource<bool>? storageAnswer = null;
        acceptStorage.Click += (_, _) => storageAnswer?.TrySetResult(true);
        declineStorage.Click += (_, _) => storageAnswer?.TrySetResult(false);
        async Task<bool> ConfirmCredentialStorage(CancellationToken token)
        {
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => answer.TrySetCanceled(token));
            await Dispatcher.InvokeAsync(() =>
            {
                storageAnswer = answer; storage.Visibility = Visibility.Visible;
                stateText.Text = "ログイン情報の保存確認待ち";
                storage.BringIntoView();
            });
            try { return await answer.Task; }
            finally
            {
                await Dispatcher.InvokeAsync(() => { storageAnswer = null; storage.Visibility = Visibility.Collapsed; });
            }
        }
        var alternative = Styled(new Expander { Header = "ログインできない場合" }, "SettingsExpander");
        var alternativePanel = new StackPanel(); alternative.Content = alternativePanel;
        alternativePanel.Children.Add(Description("別のブラウザーや端末で認証ページを開き、コードを入力して同じアカウントにログインできます。"));
        var device = ConnectionButton("コードを表示してログイン"); device.HorizontalAlignment = HorizontalAlignment.Left;
        alternativePanel.Children.Add(device); account.Children.Add(alternative);
        var advancedRow = ConnectionRow(card, true);
        var advanced = Styled(new Expander { Header = "CLI設定・アカウント管理" }, "SettingsExpander"); advancedRow.Children.Add(advanced);
        var details = new StackPanel(); advanced.Content = details;
        details.Children.Add(Styled(new TextBlock { Text = "CLI実行ファイル" }, "FieldLabel"));
        details.Children.Add(Description("空欄の場合はアプリ専用CLI、次にPATHの順で検出します。"));
        var pathGrid = new Grid(); pathGrid.ColumnDefinitions.Add(new ColumnDefinition()); pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Styled(path, "AppTextBox"); path.Margin = new Thickness(0, 6, 6, 4); path.MinWidth = 0;
        AutomationProperties.SetName(path, BackendNames.Display(backend) + " のCLI実行ファイル"); pathGrid.Children.Add(path);
        var browse = ConnectionButton("参照…"); Grid.SetColumn(browse, 1); pathGrid.Children.Add(browse); details.Children.Add(pathGrid);
        var managedInstall = ConnectionButton("アプリ専用の対応版を導入"); managedInstall.HorizontalAlignment = HorizontalAlignment.Left;
        details.Children.Add(Description("別のバージョンが検出された場合は、アプリ専用の対応版を導入できます。公式GitHubから約90〜140 MBを取得します。")); details.Children.Add(managedInstall);
        var management = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; details.Children.Add(management);
        management.Children.Add(Description("一時停止：このアプリの接続を閉じ、自動送信を停止します。ログイン情報は残ります。「接続を再開」または手動校正で再接続できます。"));
        var disconnect = ConnectionButton("このアプリの接続を一時停止"); disconnect.HorizontalAlignment = HorizontalAlignment.Left; management.Children.Add(disconnect);
        var changeAccount = ConnectionButton("別のアカウントでログイン"); changeAccount.HorizontalAlignment = HorizontalAlignment.Left; management.Children.Add(changeAccount);
        var logout = ConnectionButton("ログイン情報を削除してログアウト"); logout.HorizontalAlignment = HorizontalAlignment.Left;
        if (backend == BackendKind.CodexAppServer)
        {
            management.Children.Add(Description("ログアウト：このアプリ専用のログイン情報を削除します。次回はログインが必要です。")); management.Children.Add(logout);
        }
        else management.Children.Add(Description("Copilotはここからログイン情報を削除できません。アカウントを切り替えるには「別のアカウントでログイン」を使います。"));

        bool ownBusy = false, disconnected = false, pathChanged = false;
        void Update()
        {
            bool available;
            try { SubscriptionRuntime.ResolveExecutable(path.Text.Trim(), backend == BackendKind.CodexAppServer ? "codex.exe" : "copilot.exe"); available = true; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException or UnauthorizedAccessException) { available = false; }
            bool authenticated = !pathChanged && _subscriptions.State(backend)?.Authenticated == true;
            bool starting = _subscriptions.IsInitializing(BackendKind.CodexAppServer) || _subscriptions.IsInitializing(BackendKind.GitHubCopilot);
            bool idle = !starting && _connectionCancellation is null && !_isWorkInProgress();
            cliText.Text = available ? $"CLI検出済み · 対応版 {SubscriptionCliInstaller.VersionFor(backend)}" : "まずCLIを導入";
            install.Visibility = installHelp.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            install.IsEnabled = managedInstall.IsEnabled = idle;
            path.IsEnabled = browse.IsEnabled = idle;
            login.Visibility = authenticated ? Visibility.Collapsed : Visibility.Visible;
            login.IsEnabled = device.IsEnabled = changeAccount.IsEnabled = idle && available;
            alternative.Visibility = authenticated ? Visibility.Collapsed : Visibility.Visible;
            refresh.Content = disconnected ? "接続を再開" : authenticated ? "残量・モデル一覧を更新" : "接続を確認";
            refresh.IsEnabled = idle && available;
            refresh.ToolTip = "保存済みのログイン情報で接続し、アカウント・残量・モデル一覧を取得します。校正本文は送信しません。";
            primaryHelp.Text = _subscriptions.IsInitializing(backend) ? "保存済みのログイン情報で自動接続しています。"
                : !available ? "インストール後にログインできます。" : authenticated
                ? "更新では残量とモデル一覧を取得します。校正本文は送りません。"
                : disconnected ? "ログインし直さずに接続を再開できます。"
                : backend == BackendKind.GitHubCopilot
                    ? "保存方法を確認し、ブラウザーで認証します。ここにアカウントが表示されると完了です。"
                    : "ブラウザーで認証後、アカウントの表示までお待ちください。ログイン済みなら「接続を確認」。";
            management.Visibility = authenticated || disconnected ? Visibility.Visible : Visibility.Collapsed;
            disconnect.IsEnabled = idle && authenticated;
            logout.IsEnabled = idle && available && (authenticated || disconnected);
            cancel.Visibility = ownBusy ? Visibility.Visible : Visibility.Collapsed;
            progress.Visibility = ownBusy || _subscriptions.IsInitializing(backend) ? Visibility.Visible : Visibility.Collapsed;
            cancel.IsEnabled = ownBusy && _connectionCancellation?.IsCancellationRequested == false;
        }
        _updateConnectionControls.Add(Update);
        void ApplyState()
        {
            if (!ownBusy && !pathChanged)
            {
                if (_subscriptions.IsInitializing(backend)) stateText.Text = "アカウント・残量・モデル一覧を自動取得中…";
                else if (_subscriptions.InitializationError(backend) is { } error) stateText.Text = error;
                else if (_subscriptions.State(backend) is { } state) stateText.Text = state.Description;
            }
            Update();
        }
        _applySubscriptionStates.Add(ApplyState);
        void UpdateAll() { foreach (var update in _updateConnectionControls) update(); }
        async Task Run(string message, Func<CancellationToken, Task> action)
        {
            if (_isWorkInProgress() || _connectionCancellation is not null) return;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            cancel.Content = message.Contains("インストール") ? "インストールを中断" :
                message.Contains("ログイン") || message.Contains("アカウントを選") ? "ログイン待ちを中断" : "この接続操作を中断";
            _connectionCancellation = cancellation; ownBusy = true; stateText.Text = message; UpdateAll();
            try
            {
                await action(cancellation.Token);
                RefreshSubscriptionModels();
            }
            catch (OperationCanceledException) { stateText.Text = "操作を中断しました。必要に応じて接続を確認してください。"; }
            catch (Exception ex) { stateText.Text = ex.Message; }
            finally { _connectionCancellation = null; ownBusy = false; UpdateAll(); }
        }
        void ShowCode(string value) => Dispatcher.InvokeAsync(() => { if (ownBusy) stateText.Text = value; });
        async Task Connect(bool signIn, bool code, CancellationToken token)
        {
            if (signIn) await _subscriptions.LoginAsync(backend, path.Text.Trim(), code, ShowCode, token, ConfirmCredentialStorage);
            else await _subscriptions.RefreshAsync(backend, path.Text.Trim(), token);
            pathChanged = false; disconnected = false; stateText.Text = _subscriptions.State(backend)?.Description ?? "接続未確認";
        }
        async Task Install(CancellationToken token)
        {
            await _subscriptions.DisconnectAsync(backend, false, token);
            path.Text = await SubscriptionCliInstaller.InstallAsync(backend, new Progress<string>(ShowCode), token);
            stateText.Text = "CLIの導入が完了しました。ブラウザーでログインしてください。ログイン済みなら接続を確認できます。";
        }
        install.Click += async (_, _) => await Run("インストールを準備中…", Install);
        managedInstall.Click += async (_, _) => await Run("インストールを準備中…", Install);
        refresh.Click += async (_, _) => await Run("アカウント・残量・モデル一覧を取得中…", t => Connect(false, false, t));
        login.Click += async (_, _) => await Run("ブラウザーでログインを完了してください…", t => Connect(true, false, t));
        changeAccount.Click += async (_, _) => await Run("ブラウザーで使用するアカウントを選んでください…", t => Connect(true, false, t));
        device.Click += async (_, _) => await Run("ログイン用コードを取得中…", t => Connect(true, true, t));
        disconnect.Click += async (_, _) => await Run("接続を閉じています…", async t =>
        {
            await _subscriptions.DisconnectAsync(backend, false, t); disconnected = true;
            stateText.Text = "接続を一時停止しました。ログイン情報は保存されています。";
        });
        logout.Click += async (_, _) => await Run("ログアウト中…", async t =>
        {
            await _subscriptions.DisconnectAsync(backend, true, t, path.Text.Trim()); disconnected = false;
            stateText.Text = "ログアウトしました。再利用するにはログインしてください。";
        });
        cancel.Click += (_, _) => { _connectionCancellation?.Cancel(); UpdateAll(); };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "CLI実行ファイル (*.exe)|*.exe", Title = BackendNames.Display(backend) + " のCLIを選択" };
            if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName;
        };
        path.TextChanged += (_, _) =>
        {
            pathChanged = true; disconnected = false;
            if (!ownBusy) stateText.Text = "実行ファイルを変更しました。「接続を確認」で確認してください。";
            Update();
        };
        Activated += (_, _) => Update();
        ApplyState();
    }
}
