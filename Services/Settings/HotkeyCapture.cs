using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using JpScratch.Infrastructure;

namespace JpScratch.Services;

/// <summary>
/// ホットキー欄にフォーカスがある間だけキーを取得する。
/// 登録済みのキーは通常の KeyDown より先に他アプリへ届くため、ここで取得する。
/// 本文・APIキーの入力時は作成せず、取得内容も保存しない。
/// </summary>
internal sealed class HotkeyCapture : IDisposable
{
    private readonly HookProc _callback;
    private readonly HashSet<int> _heldKeys = [];
    private IntPtr _hook;
    private bool _disposed;

    public HotkeyCapture(IntPtr hwnd, Dispatcher dispatcher, Action<Key, ModifierKeys> received)
    {
        _callback = (code, message, data) =>
        {
            if (code >= 0 && !_disposed && NativeMethods.GetForegroundWindow() == hwnd)
            {
                int vk = Marshal.ReadInt32(data);
                Key key = KeyInterop.KeyFromVirtualKey(vk);
                bool keyDown = message == (IntPtr)0x0100 || message == (IntPtr)0x0104;
                bool keyUp = message == (IntPtr)0x0101 || message == (IntPtr)0x0105;
                // 修飾キーは通し、WPF と OS の押下状態を保つ。
                bool modifier = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
                ModifierKeys modifiers = ReadModifiers();
                bool navigation = key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift;
                if (keyUp && _heldKeys.Remove(vk)) return (IntPtr)1;
                if (keyDown && !modifier && !navigation)
                {
                    if (_heldKeys.Add(vk))
                    {
                        // フック内では描画や登録検査をせず、メッセージループへ戻してから処理する。
                        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            if (!_disposed) received(key, modifiers);
                        }));
                    }
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hook, code, message, data);
        };
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static ModifierKeys ReadModifiers()
    {
        ModifierKeys result = ModifierKeys.None;
        if (Down(0x11)) result |= ModifierKeys.Control;
        if (Down(0x12)) result |= ModifierKeys.Alt;
        if (Down(0x10)) result |= ModifierKeys.Shift;
        if (Down(0x5B) || Down(0x5C)) result |= ModifierKeys.Windows;
        return result;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
