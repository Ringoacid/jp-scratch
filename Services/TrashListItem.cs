using System.Globalization;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// ゴミ箱一覧の 1 行（ゴミ箱一覧ウィンドウ）。行数と日時の整形は WPF に依存しない純粋関数に
/// して、ウィンドウ本体（Views/TrashWindow）から分離し検証アプリ（PromptValidation）に
/// 取り込めるようにする（CrossTabSearchPreview と同じ方針）。
/// </summary>
public sealed class TrashListItem
{
    public ScratchTab Tab { get; }

    /// <summary>本文。読めなかった場合は null（プレビュー・行数とも「読めず」扱い）。</summary>
    public string? Body { get; }

    /// <summary>行数。本文が読めなかった場合は null。</summary>
    public int? LineCount { get; }

    /// <summary>閉じた日時（yyyy/MM/dd HH:mm）。null（通常あり得ない）は「—」。</summary>
    public string DeletedAtText { get; }

    /// <summary>行数の表示（null は「—」）。</summary>
    public string LineCountDisplay =>
        LineCount is null ? "—" : LineCount.Value.ToString(CultureInfo.InvariantCulture);

    public TrashListItem(ScratchTab tab, string? body)
    {
        Tab = tab;
        Body = body;
        LineCount = body is null ? null : CountLines(body);
        DeletedAtText = FormatDeletedAt(tab.DeletedAt);
    }

    /// <summary>行数 = 改行数 + 1。空文字も 1 行（AvalonEdit の行数と同義）。</summary>
    public static int CountLines(string text)
    {
        var lines = 1;
        foreach (var c in text)
        {
            if (c == '\n') lines++;
        }

        return lines;
    }

    /// <summary>閉じた日時の表示（yyyy/MM/dd HH:mm）。</summary>
    public static string FormatDeletedAt(DateTime? deletedAt)
        => deletedAt?.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) ?? "—";
}
