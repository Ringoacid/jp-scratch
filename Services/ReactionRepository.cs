using JpScratch.Proofreading;

namespace JpScratch.Services;

internal enum ProofreadingReaction
{
    Accept,
    Reject,
    RejectWithReason,

    /// <summary>
    /// 校正漏れ報告（proofreading-ux-fixes-plan.md §9）。モデルが見逃した誤字・脱字・余分な文字を
    /// ユーザー自身が訂正して記録したもの。既存の許可・拒否リアクションと区別し、few-shot の
    /// 強い学習例として使う。
    /// </summary>
    MissedCorrection,
}

/// <summary>
/// 学習効果の可視化（<see cref="ReactionRepository.GetRejectionRateTrend"/>）における1区間。
/// <paramref name="IsComplete"/>がfalseなのは末尾（＝現在進行中）の区間だけで、まだ
/// <c>StartIndex</c>〜<c>EndIndex</c>の全件が揃っていないことを示す。
/// </summary>
internal readonly record struct RejectionRateBucket(
    int StartIndex, int EndIndex, int Total, int Rejected, bool IsComplete)
{
    internal double RejectionRate => Total == 0 ? 0 : (double)Rejected / Total;
    internal string Label => $"{StartIndex}〜{EndIndex}件目";
}

/// <summary>本文とは独立して校正へのリアクションを永続化する。</summary>
internal sealed class ReactionRepository
{
    private readonly Database _database;

    internal ReactionRepository(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    internal void Add(
        string? tabId,
        ProofreadingProposal proposal,
        ProofreadingReaction reaction,
        string? reason = null,
        long? apiCallId = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        string? normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim();

        if (reaction == ProofreadingReaction.RejectWithReason &&
            normalizedReason is null)
        {
            throw new ArgumentException(
                "理由つき拒否には理由が必要です。",
                nameof(reason));
        }

        _database.Execute(
            """
            INSERT INTO reactions (
                reacted_at, api_call_id, tab_id, original, suggestion,
                left_context, right_context, reaction, user_reason)
            VALUES (
                $reacted_at, $api_call_id, $tab_id, $original, $suggestion,
                $left_context, $right_context, $reaction, $user_reason);
            """,
            ("$reacted_at", DateTimeOffset.Now.ToString("O")),
            ("$api_call_id", apiCallId),
            ("$tab_id", tabId),
            ("$original", proposal.Original),
            ("$suggestion", proposal.Suggestion),
            ("$left_context", proposal.LeftContext),
            ("$right_context", proposal.RightContext),
            ("$reaction", ToStorageValue(reaction)),
            ("$user_reason", normalizedReason));
    }

    /// <summary>
    /// 校正漏れ報告（proofreading-ux-fixes-plan.md §9.4）の記録。
    /// 選択範囲の置換・挿入・削除を、左文脈・右文脈・任意理由とともに <c>reactions</c> へ保存する。
    /// 本文の編集とは独立しており、呼び出し側（MainWindow）が「保存に成功してから本文を変更する」
    /// 順序を守る（記録だけ失敗して本文だけ変わる状態を作らない）。
    /// </summary>
    internal void AddMissedCorrection(
        string? tabId,
        string original,
        string corrected,
        string? leftContext,
        string? rightContext,
        string? reason = null)
    {
        _database.Execute(
            """
            INSERT INTO reactions (
                reacted_at, api_call_id, tab_id, original, suggestion,
                left_context, right_context, reaction, user_reason)
            VALUES (
                $reacted_at, NULL, $tab_id, $original, $suggestion,
                $left_context, $right_context, $reaction, $user_reason);
            """,
            ("$reacted_at", DateTimeOffset.Now.ToString("O")),
            ("$tab_id", tabId),
            ("$original", original),
            ("$suggestion", corrected),
            ("$left_context", leftContext),
            ("$right_context", rightContext),
            ("$reaction", ToStorageValue(ProofreadingReaction.MissedCorrection)),
            ("$user_reason", string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()));
    }

    /// <summary>蓄積された全リアクション件数（スタイルガイド生成のしきい値判定に使う）。</summary>
    internal long GetTotalCount()
        => _database.Read(
            "SELECT COUNT(*) FROM reactions;",
            reader => reader.Read() ? reader.GetInt64(0) : 0L);

    /// <summary>
    /// few-shot選定（要件3.4.1）の候補プール。直近<paramref name="limit"/>件から
    /// <see cref="Proofreading.FewShotSelector"/>が優先度・語句の重なり・新しさで絞り込む。
    /// </summary>
    internal IReadOnlyList<Proofreading.FewShotCandidate> GetFewShotCandidates(int limit = 200)
    {
        int safeLimit = Math.Clamp(limit, 1, 1000);
        return _database.Read(
            """
            SELECT original, suggestion, reaction, user_reason, reacted_at
            FROM reactions
            ORDER BY id DESC
            LIMIT $limit;
            """,
            reader =>
            {
                List<Proofreading.FewShotCandidate> candidates = [];
                while (reader.Read())
                {
                    if (!TryFromStorageValue(reader.GetString(2), out ProofreadingReaction reaction) ||
                        !DateTimeOffset.TryParse(
                            reader.GetString(4),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out DateTimeOffset reactedAt))
                    {
                        continue;
                    }

                    candidates.Add(new Proofreading.FewShotCandidate(
                        reader.GetString(0),
                        reader.GetString(1),
                        reaction,
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reactedAt));
                }

                return (IReadOnlyList<Proofreading.FewShotCandidate>)candidates;
            },
            ("$limit", safeLimit));
    }

    /// <summary>
    /// 学習効果の可視化（要件3.4「完了の判断基準」＝拒否率の低下）向けの集計。
    /// 暦月ではなく「蓄積順に<paramref name="blockSize"/>件ずつ」で区切る。実データで確認したところ
    /// v2/v3実装が同日中に進んだため、全リアクションが暦1か月に収まっており、暦月区切りだと
    /// 棒が1本しか出ない「使い始めた頃と比べ」を比較しようがないグラフになる（要件3.4末尾の
    /// 完了基準を参照）。件数区切りなら初日から意味のある比較ができ、履歴が伸びても崩れない。
    /// <c>reactions</c>は明細圧縮の対象外（世代情報そのものが学習データのため）で、1ユーザーの
    /// 手作業の履歴という上限がある前提で、CSVエクスポートと同じ理由で読み取りに上限を掛けない
    /// （上限を掛けると境界のバケットが常に一部欠けた分母で拒否率を計算してしまう）。
    /// </summary>
    internal IReadOnlyList<RejectionRateBucket> GetRejectionRateTrend(int blockSize = 20)
    {
        int safeBlockSize = Math.Clamp(blockSize, 1, 1000);

        List<ProofreadingReaction> reactions = _database.Read(
            """
            SELECT reaction
            FROM reactions
            ORDER BY id ASC;
            """,
            reader =>
            {
                List<ProofreadingReaction> list = [];
                while (reader.Read())
                {
                    if (TryFromStorageValue(reader.GetString(0), out ProofreadingReaction reaction))
                        list.Add(reaction);
                }
                return list;
            });

        List<RejectionRateBucket> buckets = [];
        // 「拒否率」は提案を拒否した割合なので、校正漏れ報告（MissedCorrection）は提案への
        // 拒否ではなくユーザー自身の手訂正として、分母・分子のどちらにも含めない。
        // 拒否数だけから除外するのでは分母に残ってしまう（「拒否10件＋校正漏れ10件」が
        // 拒否率50%に見える）ため、バケット分割**前**に一覧から除外する。
        List<ProofreadingReaction> trendReactions = [];
        foreach (ProofreadingReaction reaction in reactions)
        {
            if (reaction == ProofreadingReaction.MissedCorrection)
                continue;
            trendReactions.Add(reaction);
        }

        for (int start = 0; start < trendReactions.Count; start += safeBlockSize)
        {
            int count = Math.Min(safeBlockSize, trendReactions.Count - start);
            int rejected = 0;
            for (int i = start; i < start + count; i++)
            {
                if (trendReactions[i] is ProofreadingReaction.Reject or ProofreadingReaction.RejectWithReason)
                    rejected++;
            }
            buckets.Add(new RejectionRateBucket(start + 1, start + count, count, rejected, count == safeBlockSize));
        }

        return buckets;
    }

    internal IReadOnlyList<string> GetRecentReasons(int limit = 8)
    {
        int safeLimit = Math.Clamp(limit, 1, 30);
        return _database.Read(
            """
            SELECT user_reason
            FROM reactions
            WHERE user_reason IS NOT NULL AND trim(user_reason) <> ''
            GROUP BY user_reason
            ORDER BY count(*) DESC, max(id) DESC
            LIMIT $limit;
            """,
            reader =>
            {
                List<string> reasons = [];
                while (reader.Read())
                    reasons.Add(reader.GetString(0));
                return (IReadOnlyList<string>)reasons;
            },
            ("$limit", safeLimit));
    }

    private static string ToStorageValue(ProofreadingReaction reaction)
        => reaction switch
        {
            ProofreadingReaction.Accept => "accept",
            ProofreadingReaction.Reject => "reject",
            ProofreadingReaction.RejectWithReason => "reject_with_reason",
            ProofreadingReaction.MissedCorrection => "missed_correction",
            _ => throw new ArgumentOutOfRangeException(nameof(reaction)),
        };

    private static bool TryFromStorageValue(string value, out ProofreadingReaction reaction)
    {
        reaction = value switch
        {
            "accept" => ProofreadingReaction.Accept,
            "reject" => ProofreadingReaction.Reject,
            "reject_with_reason" => ProofreadingReaction.RejectWithReason,
            "missed_correction" => ProofreadingReaction.MissedCorrection,
            _ => default,
        };
        return value is "accept" or "reject" or "reject_with_reason" or "missed_correction";
    }
}
