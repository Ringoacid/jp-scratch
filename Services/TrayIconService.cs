using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace JpScratch.Services;

/// <summary>
/// タスクトレイ常駐（要件 3.1.1）。
/// WPF に通知領域アイコンの API がないため、ここだけ WinForms の NotifyIcon を借りる。
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;

    /// <summary>
    /// 状態ごとのアイコン。必要になった時点で読み込んで使い回す（要件 3.1.1）。
    /// 起動時に4つとも読むとコールドスタートの実測値（0.63秒）を削るので、通常以外は遅延させる。
    /// 差し替えた後も古いアイコンを破棄せず持ち続けるのは、NotifyIcon が参照している
    /// ハンドルを解放してしまわないため。数十KBなので保持したままでよい。
    /// </summary>
    private readonly Dictionary<TrayIconState, Icon> _icons = [];

    private TrayIconState _state = TrayIconState.Normal;
    private string _baseTooltip = "JP Scratch";

    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? BillingHistoryRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("表示 / 非表示(&T)", null, (_, _) => ToggleRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定(&S)...", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("課金履歴(&B)...", null, (_, _) => BillingHistoryRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了(&X)", null, (_, _) => ExitRequested?.Invoke());

        // MainWindow のコンストラクタはここより前に走り、その中の RefreshUsageDisplay が
        // 既に SetState を呼んでいることがある（起動時点で月間上限に到達している場合など）。
        // _state はその呼び出しで更新済みなので、初期アイコンは通常固定ではなく _state から選ぶ。
        _notifyIcon = new NotifyIcon
        {
            Icon = GetIcon(_state),
            Text = BuildTooltip(),
            Visible = true,
            ContextMenuStrip = menu,
        };

        // 左クリックでトグル。ダブルクリックだと 1 アクションで出す目的から遠のく。
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleRequested?.Invoke();
        };
    }

    /// <summary>
    /// ツールチップの基本文（ホットキーの案内）。状態表示はここへ後から足すので、
    /// <see cref="Initialize"/> より前に呼ばれても失わないよう保持しておく。
    /// </summary>
    public void SetTooltip(string text)
    {
        _baseTooltip = text;
        if (_notifyIcon is not null) _notifyIcon.Text = BuildTooltip();
    }

    /// <summary>
    /// トレイアイコンの状態を切り替える（要件 3.1.1）。
    /// <see cref="Initialize"/> より前に呼ばれても状態だけは覚えておき、初期化時に反映する
    /// （MainWindow のコンストラクタはトレイ初期化より前に走る）。
    /// </summary>
    public void SetState(TrayIconState state)
    {
        if (_state == state) return;
        _state = state;

        if (_notifyIcon is null) return;
        _notifyIcon.Icon = GetIcon(state);
        _notifyIcon.Text = BuildTooltip();
    }

    /// <summary>ツールチップは 63 文字までしか出ないので、切り詰めてから渡す。</summary>
    private string BuildTooltip()
    {
        string? suffix = TrayIconStateResolver.TooltipSuffix(_state);
        string text = suffix is null ? _baseTooltip : $"[{suffix}] {_baseTooltip}";
        return text.Length <= 63 ? text : text[..60] + "...";
    }

    /// <summary>
    /// バルーン通知を出す。<see cref="Initialize"/> より前（tray未初期化）に呼ばれた場合は
    /// 何もせず <c>false</c> を返す。呼び出し側はこれを見て「通知を実際に発行できたか」を判定し、
    /// できていないなら「発行済み」を記録してはいけない（さもないと機会を静かに失う）。
    /// </summary>
    public bool ShowMessage(string title, string body, bool isWarning = false)
    {
        if (_notifyIcon is null) return false;
        _notifyIcon.ShowBalloonTip(
            5000, title, body,
            isWarning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        return true;
    }

    /// <summary>状態に対応するアイコンを取り出す。読み込みは初回だけで、以降は使い回す。</summary>
    private Icon GetIcon(TrayIconState state)
    {
        if (_icons.TryGetValue(state, out Icon? cached)) return cached;

        Icon icon = LoadIcon(TrayIconStateResolver.ResourcePath(state));
        _icons[state] = icon;
        return icon;
    }

    /// <summary>
    /// 埋め込みリソースからトレイ用のサイズでアイコンを取り出す。
    /// .ico の小サイズは DIB で格納してあるので System.Drawing.Icon が展開できる。
    /// </summary>
    private static Icon LoadIcon(string resourcePath)
    {
        var size = SystemInformation.SmallIconSize;

        try
        {
            var resource = Application.GetResourceStream(new Uri(resourcePath, UriKind.Relative));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return new Icon(ms, size);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // 下のフォールバックへ
        }

        // SystemIcons の実体は共有インスタンスなので、複製してから渡す。
        // そのまま渡すと Dispose でプロセス全体のシステムアイコンを壊す。
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            // Visible を落としてから捨てないと、通知領域にゴーストが残る。
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        foreach (Icon icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
