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
    private Icon? _icon;

    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        _icon = LoadIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("表示 / 非表示(&T)", null, (_, _) => ToggleRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定(&S)...", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了(&X)", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "JP Scratch",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // 左クリックでトグル。ダブルクリックだと 1 アクションで出す目的から遠のく。
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleRequested?.Invoke();
        };
    }

    /// <summary>ツールチップは 63 文字までしか出ないので、切り詰めてから渡す。</summary>
    public void SetTooltip(string text)
    {
        if (_notifyIcon is null) return;
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "...";
    }

    public void ShowMessage(string title, string body, bool isWarning = false)
    {
        _notifyIcon?.ShowBalloonTip(
            5000, title, body,
            isWarning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    /// <summary>
    /// 埋め込みリソースからトレイ用のサイズでアイコンを取り出す。
    /// app.ico の小サイズは DIB で格納してあるので System.Drawing.Icon が展開できる。
    /// </summary>
    private static Icon LoadIcon()
    {
        var size = SystemInformation.SmallIconSize;

        try
        {
            var resource = Application.GetResourceStream(new Uri("Assets/app.ico", UriKind.Relative));
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

        _icon?.Dispose();
        _icon = null;
    }
}
