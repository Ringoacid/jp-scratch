using System.Windows;
using System.Windows.Interop;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// ウィンドウの表示位置決め（要件 3.1.2）。
///
/// WPF の Window.Left/Top はマルチモニタ + 混在 DPI では素直に扱えないため、
/// ここでは一貫して <b>物理ピクセル</b> で計算し SetWindowPos で置く。
/// 設定に保存するのも位置は物理ピクセル、サイズは DIP（モニタをまたいでも同じ見た目の大きさになるように）。
/// </summary>
internal static class WindowPlacer
{
    /// <summary>画面端からの余白（DIP）。ぴったり吸着させると影が切れて安っぽく見える。</summary>
    private const double MarginDip = 12;

    public static void Place(Window window, AppSettings settings)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var monitor = ResolveMonitor(hwnd, settings);
        if (!TryGetWorkArea(monitor, out var work)) return;

        var scale = GetScale(monitor);
        var margin = (int)Math.Round(MarginDip * scale);

        var width = (int)Math.Round(settings.WindowWidth * scale);
        var height = (int)Math.Round(settings.WindowHeight * scale);

        // 作業領域より大きいウィンドウは、そもそも置き場がない
        width = Math.Min(width, work.Width - margin * 2);
        height = Math.Min(height, work.Height - margin * 2);

        int x, y;
        if (settings.PositionMode == WindowPositionMode.RememberLast &&
            settings.LastLeft is { } lastLeft && settings.LastTop is { } lastTop)
        {
            x = (int)Math.Round(lastLeft);
            y = (int)Math.Round(lastTop);
        }
        else
        {
            x = work.Right - width - margin;
            y = work.Bottom - height - margin;
        }

        // モニタ構成や DPI が変わっても画面外に出さない（要件 3.1.2）
        (x, y) = Clamp(x, y, width, height, work);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>現在の位置とサイズを設定へ書き戻す。サイズは DIP、位置は物理ピクセル。</summary>
    public static void Capture(Window window, AppSettings settings)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // ウォームアップ中は画面外に置いてある。その座標を「前回位置」として覚えると次回出てこない。
        if (rect.Left < -20000 || rect.Top < -20000) return;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var scale = GetScale(monitor);

        settings.WindowWidth = rect.Width / scale;
        settings.WindowHeight = rect.Height / scale;
        settings.LastLeft = rect.Left;
        settings.LastTop = rect.Top;
    }

    private static IntPtr ResolveMonitor(IntPtr hwnd, AppSettings settings)
    {
        switch (settings.PositionMode)
        {
            case WindowPositionMode.CursorMonitorBottomRight:
                if (NativeMethods.GetCursorPos(out var pt))
                    return NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
                break;

            case WindowPositionMode.RememberLast:
                if (settings.LastLeft is { } l && settings.LastTop is { } t)
                {
                    var probe = new NativeMethods.RECT
                    {
                        Left = (int)l,
                        Top = (int)t,
                        Right = (int)l + 1,
                        Bottom = (int)t + 1,
                    };
                    return NativeMethods.MonitorFromRect(ref probe, NativeMethods.MONITOR_DEFAULTTONEAREST);
                }
                break;

            case WindowPositionMode.TaskbarBottomRight:
            default:
                var taskbar = GetTaskbarMonitor();
                if (taskbar != IntPtr.Zero) return taskbar;
                break;
        }

        return NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>タスクバーが載っているモニタ。既定の「右下」はここを基準にする。</summary>
    private static IntPtr GetTaskbarMonitor()
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        };

        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) == IntPtr.Zero)
            return IntPtr.Zero;

        var rect = data.rc;
        return NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    private static bool TryGetWorkArea(IntPtr monitor, out NativeMethods.RECT work)
    {
        work = default;
        if (monitor == IntPtr.Zero) return false;

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return false;

        work = info.rcWork;
        return true;
    }

    private static double GetScale(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero &&
            NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 &&
            dpiX > 0)
        {
            return dpiX / 96.0;
        }
        return 1.0;
    }

    private static (int X, int Y) Clamp(int x, int y, int width, int height, NativeMethods.RECT work)
    {
        x = Math.Min(x, work.Right - width);
        x = Math.Max(x, work.Left);
        y = Math.Min(y, work.Bottom - height);
        y = Math.Max(y, work.Top);
        return (x, y);
    }
}
