using System.Runtime.InteropServices;
using System.Windows;

namespace JpScratch.Infrastructure;

/// <summary>
/// クリップボード操作。
/// 他アプリがクリップボードを掴んでいると Win32 レベルで失敗するため、必ず数回リトライする。
/// ここが黙って失敗すると「コピーして隠す」が無言で無になり、原因も分からない。
/// </summary>
internal static class ClipboardHelper
{
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }

        return false;
    }
}
