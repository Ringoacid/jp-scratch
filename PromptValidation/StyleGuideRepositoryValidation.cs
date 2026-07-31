using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="StyleGuideRepository"/>の自己テスト。世代管理（生成・編集・有効化・無効化・削除）と、
/// リアクション件数カーソル（app_metadata再利用）の往復を一時SQLiteだけで検査する。実APIは呼ばない。
/// </summary>
internal static class StyleGuideRepositoryValidation
{
    internal static bool RunSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchStyleGuideValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            var repository = new StyleGuideRepository(database);

            // 初回はカーソル0、生成なしでは何もない。
            bool initialCursorIsZero = repository.GetReviewCursor() == 0;
            bool initiallyNoActive = repository.GetActive() is null;

            StyleGuide first = repository.Generate("- 「ら」抜き言葉は修正しない", sourceReactionCount: 50);
            bool firstIsActive = repository.GetActive() is { } activeAfterFirst &&
                                  activeAfterFirst.Id == first.Id &&
                                  !activeAfterFirst.IsUserEdited;

            StyleGuide second = repository.Generate("- 「サーバー」表記を好む", sourceReactionCount: 100);
            bool secondReplacesActive = repository.GetActive() is { } activeAfterSecond &&
                                        activeAfterSecond.Id == second.Id;
            bool historyKeepsBoth = repository.ListAll().Count == 2;

            // 手編集は新しい世代を作らず、指定した行だけを書き換える。
            bool updated = repository.UpdateContent(second.Id, "- 「サーバー」表記を好む（編集済み）");
            bool editedFlagSet = repository.GetActive() is { IsUserEdited: true } edited &&
                                  edited.Content == "- 「サーバー」表記を好む（編集済み）";

            // 過去の世代を復元できる。
            bool reactivatedFirst = repository.SetActive(first.Id);
            bool firstIsActiveAgain = repository.GetActive()?.Id == first.Id;

            // 無効化すると、履歴は残ったまま有効なものが無くなる。
            repository.Deactivate();
            bool noneActiveAfterDeactivate = repository.GetActive() is null;
            bool historyStillHasBoth = repository.ListAll().Count == 2;

            // 削除は世代そのものを消す。
            bool deleted = repository.Delete(second.Id);
            bool historyShrinksAfterDelete = repository.ListAll().Count == 1;

            // カーソルは何度でも進められ、常に最後に書いた値を読み返す。
            repository.AdvanceReviewCursor(50);
            bool cursorAt50 = repository.GetReviewCursor() == 50;
            repository.AdvanceReviewCursor(120);
            bool cursorAt120 = repository.GetReviewCursor() == 120;

            bool passed =
                initialCursorIsZero && initiallyNoActive && firstIsActive &&
                secondReplacesActive && historyKeepsBoth && updated && editedFlagSet &&
                reactivatedFirst && firstIsActiveAgain && noneActiveAfterDeactivate &&
                historyStillHasBoth && deleted && historyShrinksAfterDelete &&
                cursorAt50 && cursorAt120;

            Console.WriteLine(
                "スタイルガイドDB（世代管理・手編集・有効化/無効化・削除・カーソル往復）: " +
                $"{(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
