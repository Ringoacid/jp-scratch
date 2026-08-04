using System.Globalization;
using System.IO;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// タブの永続化。メタ情報は SQLite、本文は tabs\{id}.txt（要件 4）。
/// 本文を DB に入れないのは、アプリが壊れてもメモ帳でサルベージできるようにするため。
/// </summary>
internal sealed class TabRepository(Database db)
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>ゴミ箱にないタブを表示順で返す。本文はまだ読まない。</summary>
    public List<ScratchTab> LoadActive()
        => Load("deleted_at IS NULL", "sort_order ASC, created_at ASC");

    /// <summary>ゴミ箱のタブを新しく閉じた順で返す（要件 3.2.1 の復元用）。</summary>
    public List<ScratchTab> LoadTrash()
        => Load("deleted_at IS NOT NULL", "deleted_at DESC");

    private List<ScratchTab> Load(string where, string orderBy)
    {
        return db.Read($"""
            SELECT id, title, is_auto_title, sort_order, caret_offset, created_at, updated_at, deleted_at
            FROM tabs WHERE {where} ORDER BY {orderBy};
            """,
            reader =>
            {
                var list = new List<ScratchTab>();
                while (reader.Read())
                {
                    list.Add(new ScratchTab
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        IsAutoTitle = reader.GetInt32(2) != 0,
                        SortOrder = reader.GetInt32(3),
                        CaretOffset = reader.GetInt32(4),
                        CreatedAt = ParseDate(reader.GetString(5)),
                        UpdatedAt = ParseDate(reader.GetString(6)),
                        DeletedAt = reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
                    });
                }
                return list;
            });
    }

    /// <summary>本文をディスクから読み込んで Document に流し込む。</summary>
    /// <exception cref="IOException">本文ファイルがあるのに読み込めないとき。呼び出し側で
    /// 空として扱わず、上書きを防ぐためにタブを開かない等の対処をする。</exception>
    public void LoadBody(ScratchTab tab)
    {
        var path = tab.DeletedAt is null ? AppPaths.TabFile(tab.Id) : AppPaths.TrashFile(tab.Id);
        if (!AtomicFile.TryReadAllText(path, out var text))
        {
            // 読めなかった本文を空で扱うと、後の自動保存で元の内容を上書きしてしまう。
            // 「読めたが空」と区別して例外にする。
            throw new IOException($"本文ファイルを読み込めませんでした: {path}");
        }

        tab.Document.Text = text;
        // 復元直後は「保存済み」の状態にしておく。ここで UndoStack をクリアしないと
        // 起動直後の Ctrl+Z で本文が空になる。
        tab.Document.UndoStack.ClearAll();
        tab.IsDirty = false;
    }

    public void Upsert(ScratchTab tab)
    {
        db.Execute("""
            INSERT INTO tabs (id, title, is_auto_title, sort_order, is_active, caret_offset,
                              created_at, updated_at, deleted_at)
            VALUES ($id, $title, $auto, $sort, 0, $caret, $created, $updated, $deleted)
            ON CONFLICT(id) DO UPDATE SET
                title         = excluded.title,
                is_auto_title = excluded.is_auto_title,
                sort_order    = excluded.sort_order,
                caret_offset  = excluded.caret_offset,
                updated_at    = excluded.updated_at,
                deleted_at    = excluded.deleted_at;
            """,
            ("$id", tab.Id),
            ("$title", tab.Title),
            ("$auto", tab.IsAutoTitle ? 1 : 0),
            ("$sort", tab.SortOrder),
            ("$caret", tab.CaretOffset),
            ("$created", tab.CreatedAt.ToString(DateFormat, CultureInfo.InvariantCulture)),
            ("$updated", tab.UpdatedAt.ToString(DateFormat, CultureInfo.InvariantCulture)),
            ("$deleted", tab.DeletedAt?.ToString(DateFormat, CultureInfo.InvariantCulture)));
    }

    /// <summary>並べ替え結果をまとめて書く。</summary>
    public void SaveOrder(IEnumerable<ScratchTab> tabs)
    {
        db.InTransaction(_ =>
        {
            var order = 0;
            foreach (var tab in tabs)
            {
                tab.SortOrder = order++;
                db.Execute("UPDATE tabs SET sort_order = $sort WHERE id = $id;",
                    ("$sort", tab.SortOrder), ("$id", tab.Id));
            }
        });
    }

    public void SaveActive(string? tabId)
    {
        db.InTransaction(_ =>
        {
            db.Execute("UPDATE tabs SET is_active = 0;");
            if (tabId is not null)
                db.Execute("UPDATE tabs SET is_active = 1 WHERE id = $id;", ("$id", tabId));
        });
    }

    public string? LoadActiveId()
        => db.Read("SELECT id FROM tabs WHERE is_active = 1 AND deleted_at IS NULL LIMIT 1;",
            reader => reader.Read() ? reader.GetString(0) : null);

    /// <summary>
    /// 一時ファイル経由で本文を書き出し、既存の本文を壊さない（要件 3.2.4 / R-8）。
    ///
    /// <c>IsDirty</c> はここでは落とさない。本文の保存は「保存」の半分でしかなく、続く
    /// <see cref="Upsert"/>（タイトル・更新時刻・キャレット位置）が失敗しても保存済み扱いに
    /// なってしまうと、そのタブは通知にも再試行にも乗らなくなる。どこまで書けたら保存済みかは
    /// 呼び出し元（<see cref="TabManager.SaveDirty"/>）が決める。
    /// </summary>
    public void SaveBody(ScratchTab tab)
    {
        var path = tab.DeletedAt is null ? AppPaths.TabFile(tab.Id) : AppPaths.TrashFile(tab.Id);
        AtomicFile.WriteAllText(path, tab.Document.Text);
    }

    /// <summary>
    /// タブを閉じる = ゴミ箱へ移す。本文ファイルも trash\ へ移動する。
    /// 本文ファイルの移動に失敗したら例外を投げる（呼び出し元はタブを閉じずに済ませる）。
    /// 失敗を握ると「DB はゴミ箱済み・本文ファイルは残ったまま」の乖離が生じるため、
    /// 例外で伝える（要件 3.2.4: 本文はメモ帳でサルベージできることが前提）。
    /// </summary>
    public void MoveToTrash(ScratchTab tab)
    {
        var from = AppPaths.TabFile(tab.Id);
        var to = AppPaths.TrashFile(tab.Id);

        // 本文ファイルの移動・書き出しを先に済ませる。失敗したら DB・メモリの状態は変えない。
        if (File.Exists(from))
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
        else
        {
            AtomicFile.WriteAllText(to, tab.Document.Text);
        }

        tab.DeletedAt = DateTime.Now;
        tab.UpdatedAt = DateTime.Now;
        Upsert(tab);
    }

    /// <summary>
    /// ゴミ箱から戻す（Ctrl+Shift+T）。
    /// 本文ファイルの移動に失敗したら例外を投げ、DB（deleted_at）は更新しない。
    /// 失敗を握ると「DB は復元済み・本文ファイルが無い」になり、1文字打って保存した瞬間に
    /// 元の本文が失われる（実データの安全に関わるため、黙らせない）。
    /// </summary>
    public void RestoreFromTrash(ScratchTab tab, int sortOrder)
    {
        var from = AppPaths.TrashFile(tab.Id);
        var to = AppPaths.TabFile(tab.Id);
        if (File.Exists(from))
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }

        tab.DeletedAt = null;
        tab.SortOrder = sortOrder;
        tab.UpdatedAt = DateTime.Now;
        Upsert(tab);
    }

    /// <summary>
    /// 保持期間を過ぎたゴミ箱を削除する（既定 30 日、要件 3.2.1）。
    /// 起動時に一度だけ呼ぶ。
    /// </summary>
    public int PurgeExpiredTrash(int retentionDays)
    {
        var threshold = DateTime.Now.AddDays(-retentionDays).ToString(DateFormat, CultureInfo.InvariantCulture);

        var expiredIds = db.Read(
            "SELECT id FROM tabs WHERE deleted_at IS NOT NULL AND deleted_at < $threshold;",
            reader =>
            {
                var ids = new List<string>();
                while (reader.Read()) ids.Add(reader.GetString(0));
                return ids;
            },
            ("$threshold", threshold));

        var purgedCount = 0;
        foreach (var id in expiredIds)
        {
            try
            {
                var path = AppPaths.TrashFile(id);
                if (File.Exists(path)) File.Delete(path);

                // 本文ファイルを削除できた（または既に無い）行だけ消す。失敗した行はDBに
                // 残して、次回起動時に本文ファイルの削除を再試行できるようにする。
                purgedCount += db.Execute(
                    "DELETE FROM tabs WHERE id = $id AND deleted_at IS NOT NULL AND deleted_at < $threshold;",
                    ("$id", id), ("$threshold", threshold));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException)
            {
                // AppPaths.TrashFile が投げる「タブIDの形式が不正」。IOException 非派生なので
                // 明示的に拾わないと、壊れた 1 行だけで起動時の掃除ごと落ちて MainWindow の
                // 生成前に脱出する（CLAUDE.md「壊れた1行で全体を落とさない」）。
                // 本文ファイルの位置が決まらない＝消せないので、DB 行も残して次へ進む。
            }
        }

        return purgedCount;
    }

    private static DateTime ParseDate(string value)
        => DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.Now;
}
