using System.Runtime.InteropServices;

namespace JpScratch.Infrastructure;

/// <summary>
/// Win32 相互運用。常駐アプリの中核（グローバルホットキー・フォアグラウンド制御・
/// モニタ作業領域の取得・IME 変換状態の検出）は WPF だけでは書けないためここに集約する。
/// </summary>
internal static class NativeMethods
{
    // ---- ウィンドウメッセージ ----
    public const int WM_HOTKEY = 0x0312;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_IME_STARTCOMPOSITION = 0x010D;
    public const int WM_IME_ENDCOMPOSITION = 0x010E;

    // ---- ホットキー修飾子 ----
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- フォアグラウンド制御 ----
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    // ---- カーソルとモニタ ----
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>Windows 10 1607 以降。ウィンドウが載っているモニタの実効 DPI を返す。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    public const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>移動先モニタの DPI を、移動する前に知るために使う。</summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ---- ウィンドウ矩形 ----
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                           int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ---- タスクバー位置 ----
    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    public const uint ABM_GETTASKBARPOS = 0x00000005;

    [DllImport("shell32.dll")]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ---- IME ----
    public const int GCS_COMPSTR = 0x0008;

    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    public static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, IntPtr lpBuf, int dwBufLen);

    /// <summary>
    /// 変換中の未確定文字列があるか（要件 3.1.3 / R-5）。
    /// フックでメッセージを見張るより、必要な瞬間に問い合わせるほうが取りこぼさない。
    /// </summary>
    public static bool HasImeComposition(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        var context = ImmGetContext(hWnd);
        if (context == IntPtr.Zero) return false;

        try
        {
            // バッファに null を渡すと、必要なバイト数（= 未確定文字列の長さ）が返る
            return ImmGetCompositionString(context, GCS_COMPSTR, IntPtr.Zero, 0) > 0;
        }
        finally
        {
            ImmReleaseContext(hWnd, context);
        }
    }

    // ---- ワーキングセット ----
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// 使っていない物理メモリを OS へ返す（要件 2.1 の常駐時メモリ目標）。
    /// ウィンドウを隠した後に呼ぶ。次に表示するときページフォールトで戻るが、
    /// 一日中トレイに居るアプリとしてはそちらのほうが行儀がよい。
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            EmptyWorkingSet(GetCurrentProcess());
        }
        catch (EntryPointNotFoundException)
        {
            // psapi が無い環境は想定しないが、ここで落ちる価値はない
        }
    }

    /// <summary>
    /// 他プロセスがフォアグラウンドを持っている状況でも自分を前面に出せるようにする。
    /// 入力スレッドを一時的にアタッチしないと <see cref="SetForegroundWindow"/> は黙って失敗する。
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);

        var foreground = GetForegroundWindow();
        if (foreground == hWnd)
        {
            SetForegroundWindow(hWnd);
            return;
        }

        uint foreignThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        uint ownThread = GetCurrentThreadId();

        if (foreignThread != 0 && foreignThread != ownThread)
        {
            AttachThreadInput(ownThread, foreignThread, true);
            try
            {
                SetForegroundWindow(hWnd);
            }
            finally
            {
                AttachThreadInput(ownThread, foreignThread, false);
            }
        }
        else
        {
            SetForegroundWindow(hWnd);
        }
    }
}
