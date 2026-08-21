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
                // 最新のスキーマ版。api_call_daily（明細圧縮）はv4、料金未確認の列はv5で追加された。
                version == 5 &&
                count == 4 &&
                applied &&
                document.Text == "文章ア" &&
                reasons.SequenceEqual(["意図した表現です", "別の理由"]);
            Console.WriteLine(
                $"リアクションDB（移行・保存・理由候補）: " +
                $"{(passed ? "PASS" : "FAIL")}");
            bool trendPassed = RunRejectionRateTrendSelfTests(repository, proposal);
            bool missedPassed = RunMissedCorrectionSelfTests();
            return passed && trendPassed && missedPassed;
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
    /// 校正漏れ報告（proofreading-ux-fixes-plan.md §9）の保存と読み出しのテスト。
    /// 置換・挿入・削除の3種を記録し、reaction が missed_correction であること・
    /// 原文/修正後/左右文脈/理由が保存されること・few-shot候補として取得できること・
    /// 拒否率推移に含まれないことを確認する。
    /// </summary>
    private static bool RunMissedCorrectionSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchMissedCorrectionValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            var repository = new ReactionRepository(database);

            // 置換（選択あり・修正後あり）
            repository.AddMissedCorrection(
                "tab-m", "負具合", "不具合",
                "これは", "です。", "打ち間違い");
            // 挿入（選択なし・修正後あり）
            repository.AddMissedCorrection(
                "tab-m", "", "追加分", "前文", "後文", null);
            // 削除（選択あり・修正後が空）
            repository.AddMissedCorrection(
                "tab-m", "っ", "", "急増", "いたしました", "");

            int count = database.Read(
                "SELECT count(*) FROM reactions WHERE reaction = 'missed_correction';",
                reader => reader.Read() ? reader.GetInt32(0) : 0);
            if (count != 3)
                return false;

            bool rows = database.Read(
                """
                SELECT original, suggestion, left_context, right_context, user_reason
                FROM reactions WHERE reaction = 'missed_correction' ORDER BY id ASC;
                """,
                reader =>
                {
                    if (!reader.Read() ||
                        reader.GetString(0) != "負具合" ||
                        reader.GetString(1) != "不具合" ||
                        reader.GetString(2) != "これは" ||
                        reader.GetString(3) != "です。" ||
                        reader.GetString(4) != "打ち間違い")
                    {
                        return false;
                    }
                    if (!reader.Read() ||
                        reader.GetString(0) != "" ||
                        reader.GetString(1) != "追加分" ||
                        reader.IsDBNull(4) == false)
                    {
                        return false;
                    }
                    if (!reader.Read() ||
                        reader.GetString(0) != "っ" ||
                        reader.GetString(1) != "" ||
                        !reader.IsDBNull(4))
                    {
                        return false;
                    }
                    return !reader.Read();
                });

            // few-shot 候補として取得できる（学習例として将来の校正に使われる）。
            IReadOnlyList<FewShotCandidate> candidates = repository.GetFewShotCandidates();
            bool fewShot = candidates.Count == 3 &&
                           candidates.All(candidate =>
                               candidate.Reaction == ProofreadingReaction.MissedCorrection) &&
                           candidates.Any(candidate =>
                               candidate.Original == "負具合" && candidate.Suggestion == "不具合");

            // 拒否率推移には校正漏れ報告を**分母からも**含めない（提案への拒否ではないため。
            // 拒否数だけから除外すると「拒否10件＋校正漏れ10件」が拒否率50%に見えてしまう）。
            // レビュー指摘 P2 の例を再現: 校正漏れ3件＋拒否10件 → 拒否のみの区間は Total=10,
            // Rejected=10（100%）になる。
            var document = new TextDocument("文章ア");
            using var session = new ProofreadingSession(document);
            session.LoadCorrectedDocument("文章は");
            ProofreadingProposal proposal = session.Proposals.Single();
            for (int i = 0; i < 10; i++)
            {
                repository.Add("tab-m", proposal, ProofreadingReaction.Reject);
            }

            IReadOnlyList<RejectionRateBucket> buckets = repository.GetRejectionRateTrend(blockSize: 20);
            bool trend = buckets.Count == 1 &&
                         buckets[0] is { Total: 10, Rejected: 10, IsComplete: false };

            Console.WriteLine(
                $"リアクションDB（校正漏れの保存・few-shot候補・拒否率非計上）: " +
                $"{(rows && fewShot && trend ? "PASS" : "FAIL")}");
            return rows && fewShot && trend;
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
