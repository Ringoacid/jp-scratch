using System.IO;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// ゴミ箱の完全削除・空にする・復元（<see cref="TabRepository"/>）と一覧行の整形
/// （<see cref="TrashListItem"/>）の自己テスト。TabRepository の内部コンストラクタで
/// 一時ディレクトリを渡し、実 AppPaths（%APPDATA%）には一切触れない。
/// </summary>
internal static class TrashRepositoryValidation
{
    internal static bool RunSelfTests()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "JpScratchTrashValidation", Guid.NewGuid().ToString("N"));
        string tabsDir = Path.Combine(root, "tabs");
        string trashDir = Path.Combine(root, "tabs", "trash");
        Directory.CreateDirectory(tabsDir);
        Directory.CreateDirectory(trashDir);

        try
        {
            using var database = new Database(Path.Combine(root, "test.db"));
            var repository = new TabRepository(database, tabsDir, trashDir);

            bool deletePermanentlyPass = TestDeletePermanently(repository, tabsDir, trashDir);
            bool deleteAllTrashPass = TestDeleteAllTrash(repository, database, tabsDir, trashDir);
            bool restorePass = TestRestore(repository, tabsDir, trashDir);
            bool formattingPass = TestTrashListItemFormatting();

            bool passed = deletePermanentlyPass && deleteAllTrashPass && restorePass && formattingPass;
            Console.WriteLine("ゴミ箱（完全削除・空にする・復元・行数整形）: " + (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>個別の完全削除: 本文ファイルと DB 行が消え、アクティブ行はガードで守られる。</summary>
    private static bool TestDeletePermanently(TabRepository repository, string tabsDir, string trashDir)
    {
        var trashed = TrashedTab("本文です\n2行目");
        repository.Upsert(trashed);
        repository.SaveBody(trashed);

        var active = ActiveTab();
        repository.Upsert(active);
        repository.SaveBody(active);

        repository.DeletePermanently(trashed);

        bool fileGone = !File.Exists(Path.Combine(trashDir, trashed.Id + ".txt"));
        bool rowGone = repository.LoadTrash().All(t => t.Id != trashed.Id);

        // ガード: deleted_at IS NULL のアクティブタブは DeletePermanently では消えない。
        repository.DeletePermanently(active);
        bool activeRowKept = repository.LoadActive().Any(t => t.Id == active.Id);
        bool activeFileKept = File.Exists(Path.Combine(tabsDir, active.Id + ".txt"));

        return fileGone && rowGone && activeRowKept && activeFileKept;
    }

    /// <summary>空にする: ゴミ箱の行だけ全削除、アクティブ行と壊れた ID の行は残る。</summary>
    private static bool TestDeleteAllTrash(TabRepository repository, Database database,
                                           string tabsDir, string trashDir)
    {
        var first = TrashedTab("1件目");
        var second = TrashedTab("2件目");
        var active = ActiveTab();
        foreach (var tab in new[] { first, second, active })
        {
            repository.Upsert(tab);
            repository.SaveBody(tab);
        }

        // タブ ID の形式が不正な行（AppPaths.TabDataFile が InvalidDataException を投げる）。
        // 壊れた 1 行で全体が落ちず、その行だけ残ることを確認する。
        database.Execute("""
            INSERT INTO tabs (id, title, is_auto_title, sort_order, is_active, caret_offset,
                              created_at, updated_at, deleted_at)
            VALUES ($id, '壊れた行', 1, 0, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00',
                    '2026-01-01 00:00:00');
            """, ("$id", "broken-id"));

        int deletedCount = repository.DeleteAllTrash();

        bool trashOnlyBrokenRow = repository.LoadTrash().All(t => t.Id == "broken-id");
        bool activeKept = repository.LoadActive().Any(t => t.Id == active.Id);
        bool filesGone = !File.Exists(Path.Combine(trashDir, first.Id + ".txt"))
                         && !File.Exists(Path.Combine(trashDir, second.Id + ".txt"));
        bool activeFileKept = File.Exists(Path.Combine(tabsDir, active.Id + ".txt"));

        return deletedCount == 2 && trashOnlyBrokenRow && activeKept && filesGone && activeFileKept;
    }

    /// <summary>復元: 本文ファイルが tabs\ へ戻り、deleted_at がクリアされ sort_order が設定される。</summary>
    private static bool TestRestore(TabRepository repository, string tabsDir, string trashDir)
    {
        var trashed = TrashedTab("復元される本文");
        repository.Upsert(trashed);
        repository.SaveBody(trashed);

        repository.RestoreFromTrash(trashed, sortOrder: 3);

        bool fileMoved = File.Exists(Path.Combine(tabsDir, trashed.Id + ".txt"))
                         && !File.Exists(Path.Combine(trashDir, trashed.Id + ".txt"));
        bool flagsCleared = trashed.DeletedAt is null && trashed.SortOrder == 3;
        bool notInTrash = repository.LoadTrash().All(t => t.Id != trashed.Id);
        bool inActive = repository.LoadActive().Any(t => t.Id == trashed.Id);

        return fileMoved && flagsCleared && notInTrash && inActive;
    }

    /// <summary>一覧行の整形: 行数（改行数 + 1）と閉じた日時の表示。</summary>
    private static bool TestTrashListItemFormatting()
    {
        bool emptyIsOneLine = TrashListItem.CountLines("") == 1;
        bool twoLines = TrashListItem.CountLines("あ\nい") == 2;
        bool trailingNewlineCounts = TrashListItem.CountLines("あ\n") == 2;

        var deletedAt = new DateTime(2026, 1, 2, 3, 4, 5);
        bool dateFormat = TrashListItem.FormatDeletedAt(deletedAt) == "2026/01/02 03:04";
        bool nullDateIsDash = TrashListItem.FormatDeletedAt(null) == "—";

        var item = new TrashListItem(TrashedTab("a\nb"), body: "a\nb");
        bool lineCountFromBody = item.LineCount == 2;
        var unreadable = new TrashListItem(TrashedTab("x"), body: null);
        bool nullBodyShowsDash = unreadable.LineCount is null && unreadable.LineCountDisplay == "—";

        return emptyIsOneLine && twoLines && trailingNewlineCounts
            && dateFormat && nullDateIsDash && lineCountFromBody && nullBodyShowsDash;
    }

    private static ScratchTab TrashedTab(string body)
    {
        var tab = new ScratchTab
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "ゴミ箱テスト",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            DeletedAt = DateTime.Now,
        };
        tab.Document.Text = body;
        return tab;
    }

    private static ScratchTab ActiveTab()
    {
        var tab = new ScratchTab
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "アクティブテスト",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        tab.Document.Text = "アクティブ本文";
        return tab;
    }
}
