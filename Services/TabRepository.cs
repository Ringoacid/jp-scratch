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
    public void LoadBody(ScratchTab tab)
    {
        var path = tab.DeletedAt is null ? AppPaths.TabFile(tab.Id) : AppPaths.TrashFile(tab.Id);
        var text = AtomicFile.ReadAllTextOrEmpty(path);

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

    /// <summary>本文を原子的に書き出す（要件 3.2.4 / R-8）。</summary>
    public void SaveBody(ScratchTab tab)
    {
        var path = tab.DeletedAt is null ? AppPaths.TabFile(tab.Id) : AppPaths.TrashFile(tab.Id);
        AtomicFile.WriteAllText(path, tab.Document.Text);
        tab.IsDirty = false;
    }

    /// <summary>タブを閉じる = ゴミ箱へ移す。本文ファイルも trash\ へ移動する。</summary>
    public void MoveToTrash(ScratchTab tab)
    {
        tab.DeletedAt = DateTime.Now;
        tab.UpdatedAt = DateTime.Now;

        var from = AppPaths.TabFile(tab.Id);
        var to = AppPaths.TrashFile(tab.Id);
        try
        {
            if (File.Exists(from))
            {
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
            else
            {
                AtomicFile.WriteAllText(to, tab.Document.Text);
            }
        }
        catch (IOException) { }

        Upsert(tab);
    }

    /// <summary>ゴミ箱から戻す（Ctrl+Shift+T）。</summary>
    public void RestoreFromTrash(ScratchTab tab, int sortOrder)
    {
        var from = AppPaths.TrashFile(tab.Id);
        var to = AppPaths.TabFile(tab.Id);
        try
        {
            if (File.Exists(from))
            {
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
        }
        catch (IOException) { }

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

        foreach (var id in expiredIds)
        {
            try
            {
                var path = AppPaths.TrashFile(id);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (expiredIds.Count > 0)
            db.Execute("DELETE FROM tabs WHERE deleted_at IS NOT NULL AND deleted_at < $threshold;",
                ("$threshold", threshold));

        return expiredIds.Count;
    }

    private static DateTime ParseDate(string value)
        => DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.Now;
}
