using JpScratch.Proofreading;

namespace JpScratch.Services;

internal enum ProofreadingReaction
{
    Accept,
    Reject,
    RejectWithReason,
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
            ("$reacted_at", DateTime.Now.ToString("O")),
            ("$api_call_id", apiCallId),
            ("$tab_id", tabId),
            ("$original", proposal.Original),
            ("$suggestion", proposal.Suggestion),
            ("$left_context", proposal.LeftContext),
            ("$right_context", proposal.RightContext),
            ("$reaction", ToStorageValue(reaction)),
            ("$user_reason", normalizedReason));
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
            ORDER BY count(*) DESC, max(reacted_at) DESC
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
            _ => throw new ArgumentOutOfRangeException(nameof(reaction)),
        };
}
