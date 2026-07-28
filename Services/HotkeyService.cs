using System.Windows.Interop;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// グローバルホットキー（要件 3.1.4）。
/// 他アプリと衝突したときは黙って効かなくなるのが最悪なので、登録失敗を必ず呼び出し元へ返す（R-7）。
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    public const int IdToggle = 0xA001;
    public const int IdCopyAndHide = 0xA002;

    private readonly List<int> _registered = [];
    private IntPtr _hwnd;
    private HwndSource? _source;

    /// <summary>押されたホットキーの ID を通知する。</summary>
    public event Action<int>? Pressed;

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// 設定の内容で登録し直す。戻り値は登録できなかったホットキーの説明（空なら全部成功）。
    /// </summary>
    public IReadOnlyList<string> Reregister(AppSettings settings)
    {
        UnregisterAll();

        var failures = new List<string>();
        TryRegister(IdToggle, settings.ToggleHotkey, "表示 / 非表示トグル", failures);
        TryRegister(IdCopyAndHide, settings.CopyAndHideHotkey, "全文をコピーして隠す", failures);
        return failures;
    }

    private void TryRegister(int id, HotkeySpec spec, string label, List<string> failures)
    {
        if (_hwnd == IntPtr.Zero) return;

        if (!spec.IsValid)
        {
            failures.Add($"{label}: 「{spec}」は修飾キーとの組み合わせになっていません");
            return;
        }

        if (NativeMethods.RegisterHotKey(_hwnd, id, spec.Win32Modifiers, spec.VirtualKey))
        {
            _registered.Add(id);
        }
        else
        {
            failures.Add($"{label}: 「{spec.DisplayName}」は他のアプリが使用中です");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registered.Contains(id))
            {
                Pressed?.Invoke(id);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (var id in _registered) NativeMethods.UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
