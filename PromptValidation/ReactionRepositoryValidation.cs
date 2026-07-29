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
                version == 2 &&
                count == 4 &&
                applied &&
                document.Text == "文章ア" &&
                reasons.SequenceEqual(["意図した表現です", "別の理由"]);
            Console.WriteLine(
                $"リアクションDB（移行・保存・理由候補）: " +
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
