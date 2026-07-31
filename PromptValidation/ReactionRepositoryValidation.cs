using ICSharpCode.AvalonEdit.Document;
using JpScratch.Proofreading;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class ReactionRepositoryValidation
{
    internal static bool RunSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchReactionValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            var repository = new ReactionRepository(database);
            var document = new TextDocument("文章ア");
            using var session = new ProofreadingSession(document);
            session.LoadCorrectedDocument("文章は");
            ProofreadingProposal proposal = session.Proposals.Single();

            repository.Add(
                "tab-test",
                proposal,
                ProofreadingReaction.RejectWithReason,
                "意図した表現です");
            repository.Add(
                "tab-test",
                proposal,
                ProofreadingReaction.RejectWithReason,
                "別の理由");
            repository.Add(
                "tab-test",
                proposal,
                ProofreadingReaction.RejectWithReason,
                "意図した表現です");

            IReadOnlyList<string> reasons = repository.GetRecentReasons();
            repository.Add(
                "tab-test",
                proposal,
                ProofreadingReaction.Accept);
            bool applied = session.TryApply(proposal);
            document.UndoStack.Undo();
            int version = database.Read(
                "PRAGMA user_version;",
                reader => reader.Read() ? reader.GetInt32(0) : 0);
            int count = database.Read(
                "SELECT count(*) FROM reactions;",
                reader => reader.Read() ? reader.GetInt32(0) : 0);
            bool passed =
                // 最新のスキーマ版。api_call_daily（明細圧縮）を足した v4。
                version == 4 &&
                count == 4 &&
                applied &&
                document.Text == "文章ア" &&
                reasons.SequenceEqual(["意図した表現です", "別の理由"]);
            Console.WriteLine(
                $"リアクションDB（移行・保存・理由候補）: " +
                $"{(passed ? "PASS" : "FAIL")}");
            bool trendPassed = RunRejectionRateTrendSelfTests(repository, proposal);
            return passed && trendPassed;
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

    /// <summary>
    /// 学習効果の可視化（要件3.4「完了の判断基準」）向け<see cref="ReactionRepository.GetRejectionRateTrend"/>
    /// の自己テスト。暦月ではなく件数（既定20件）で区切ること、末尾の未完区間に
    /// <c>IsComplete=false</c>が立つことを確認する。既存の4件（拒否3・許可1）に加えて、
    /// 1〜16件目区間を「許可12・拒否4」で埋めて合計20件ちょうどの完了区間を作り、
    /// 続けて5件（許可4・拒否1）を積んで進行中の第2区間を作る。
    /// </summary>
    private static bool RunRejectionRateTrendSelfTests(ReactionRepository repository, ProofreadingProposal proposal)
    {
        // 既存4件（Reject×3, Accept×1）に16件（Accept×12, Reject×4）を足して、
        // ちょうど20件・拒否7件の完了区間を作る。
        for (int i = 0; i < 12; i++)
        {
            repository.Add("tab-test", proposal, ProofreadingReaction.Accept);
        }
        for (int i = 0; i < 4; i++)
        {
            repository.Add("tab-test", proposal, ProofreadingReaction.Reject);
        }
        // 進行中の第2区間: 5件（許可4・拒否1）。
        for (int i = 0; i < 4; i++)
        {
            repository.Add("tab-test", proposal, ProofreadingReaction.Accept);
        }
        repository.Add("tab-test", proposal, ProofreadingReaction.RejectWithReason, "理由");

        IReadOnlyList<RejectionRateBucket> buckets = repository.GetRejectionRateTrend();

        bool passed =
            buckets.Count == 2 &&
            buckets[0] is { StartIndex: 1, EndIndex: 20, Total: 20, Rejected: 7, IsComplete: true } &&
            buckets[0].Label == "1〜20件目" &&
            buckets[1] is { StartIndex: 21, EndIndex: 25, Total: 5, Rejected: 1, IsComplete: false };

        Console.WriteLine(
            $"リアクションDB（拒否率推移の区間分け・件数区切り・進行中フラグ）: " +
            $"{(passed ? "PASS" : "FAIL")}");
        return passed;
    }
}
