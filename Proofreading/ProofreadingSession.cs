using ICSharpCode.AvalonEdit.Document;

namespace JpScratch.Proofreading;

/// <summary>
/// 1つの TextDocument に属する校正提案を管理する。
/// 提案外の編集には TextAnchor で追従し、提案範囲への編集だけを失効させる。
/// </summary>
internal sealed class ProofreadingSession : IDisposable
{
    private readonly TextDocument _document;
    private readonly List<ProofreadingProposal> _proposals = [];
    private bool _isApplyingProposal;
    private bool _disposed;

    internal ProofreadingSession(TextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Changing += OnDocumentChanging;
    }

    internal IReadOnlyList<ProofreadingProposal> Proposals => _proposals;

    /// <summary>提案一覧の追加・削除・失効時に発火する。描画層と下部パネルの更新起点。</summary>
    internal event Action? Changed;

    internal ProofreadingProposal? FindAtOffset(int offset)
        => _proposals.FirstOrDefault(proposal =>
            proposal.IsActive &&
            offset >= proposal.Start &&
            offset < proposal.Start + proposal.Length);

    internal ProofreadingProposal? GetRelative(
        ProofreadingProposal? current,
        int direction)
    {
        IReadOnlyList<ProofreadingProposal> active = _proposals
            .Where(proposal => proposal.IsActive)
            .OrderBy(proposal => proposal.Start)
            .ToArray();
        if (active.Count == 0)
            return null;

        int currentIndex = current is null ? -1 : active.IndexOfReference(current);
        if (currentIndex < 0)
            return direction < 0 ? active[^1] : active[0];

        int next = ((currentIndex + Math.Sign(direction)) % active.Count + active.Count) %
                   active.Count;
        return active[next];
    }

    /// <summary>
    /// モデルが返した修正版全文を現在の本文と比較し、既存提案を置き換える。
    /// 安全検査に失敗した場合は提案を空にし、拒否理由を呼び出し元へ返す。
    /// </summary>
    internal DocumentDiffResult LoadCorrectedDocument(string corrected)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 旧提案を Active のまま残さない。外部（_selectedProposal 等）が参照を持っていても
        // IsActive=false になるよう、リストから外す前に明示的に失効させる。
        foreach (ProofreadingProposal proposal in _proposals)
            proposal.Invalidate();
        _proposals.Clear();

        // corrected は段落単位で検証済みの差分を全文へ統合した結果（ProofreadingResultMerger）。
        // 段落ごとの上限（変更箇所数・編集距離・変更比率）を合計が超えることが正当にあり得るため、
        // 全文の再検証ではグローバル上限を再適用しない（緩和モード）。安全検査の本体（過大な
        // 1箇所の変更・範囲の重複・適用で再現できること）は段落単位の検証で既に済んでいる。
        DocumentDiffResult result = DocumentDiff.Create(
            _document.Text,
            corrected,
            relaxedGlobalLimits: true);
        if (result.Accepted)
        {
            _proposals.AddRange(
                result.Changes.Select(change => new ProofreadingProposal(_document, change)));
        }

        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// アンカー位置の原文が今も一致するときだけ提案を適用する。
    /// UndoStack 上では通常の1回の置換として扱われる。
    /// </summary>
    internal bool TryApply(ProofreadingProposal proposal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_proposals.Contains(proposal) || !proposal.IsActive)
            return false;

        int start = proposal.Start;
        int length = proposal.Length;
        if (!string.Equals(
                _document.GetText(start, length),
                proposal.Original,
                StringComparison.Ordinal))
        {
            RemoveAsInvalid(proposal);
            return false;
        }

        _isApplyingProposal = true;
        try
        {
            _document.Replace(start, length, proposal.Suggestion);
        }
        finally
        {
            _isApplyingProposal = false;
        }

        proposal.MarkApplied();
        _proposals.Remove(proposal);
        Changed?.Invoke();
        return true;
    }

    internal bool Reject(ProofreadingProposal proposal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_proposals.Remove(proposal))
            return false;

        proposal.MarkRejected();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 理由つき別案生成が成功したとき、本文を変えずに同じ範囲の提案だけを差し替える。
    /// </summary>
    internal bool TryReplaceSuggestion(
        ProofreadingProposal proposal,
        string alternative)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_proposals.Contains(proposal) ||
            !proposal.IsActive ||
            string.IsNullOrWhiteSpace(alternative) ||
            string.Equals(alternative, proposal.Original, StringComparison.Ordinal) ||
            string.Equals(alternative, proposal.Suggestion, StringComparison.Ordinal))
        {
            return false;
        }

        int start = proposal.Start;
        if (!string.Equals(
                _document.GetText(start, proposal.Length),
                proposal.Original,
                StringComparison.Ordinal))
        {
            RemoveAsInvalid(proposal);
            return false;
        }

        var replacement = new ProofreadingProposal(
            _document,
            new DocumentChange(
                start,
                proposal.Length,
                proposal.Original,
                alternative,
                proposal.LeftContext,
                proposal.RightContext));

        proposal.MarkRejected();
        int index = _proposals.IndexOf(proposal);
        _proposals[index] = replacement;
        Changed?.Invoke();
        return true;
    }

    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_proposals.Count == 0)
            return;

        foreach (ProofreadingProposal proposal in _proposals)
            proposal.Invalidate();
        _proposals.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _document.Changing -= OnDocumentChanging;
        foreach (ProofreadingProposal proposal in _proposals)
            proposal.Invalidate();
        _proposals.Clear();
        _disposed = true;
    }

    private void OnDocumentChanging(object? sender, DocumentChangeEventArgs change)
    {
        if (_isApplyingProposal || _proposals.Count == 0)
            return;

        List<ProofreadingProposal> invalidated = [];
        foreach (ProofreadingProposal proposal in _proposals)
        {
            if (!proposal.IsActive || IntersectsProposal(change, proposal))
                invalidated.Add(proposal);
        }

        if (invalidated.Count == 0)
            return;

        foreach (ProofreadingProposal proposal in invalidated)
        {
            proposal.Invalidate();
            _proposals.Remove(proposal);
        }
        Changed?.Invoke();
    }

    private static bool IntersectsProposal(
        DocumentChangeEventArgs change,
        ProofreadingProposal proposal)
    {
        int proposalStart = proposal.Start;
        int proposalEnd = proposalStart + proposal.Length;

        if (change.RemovalLength > 0)
        {
            int removalEnd = change.Offset + change.RemovalLength;
            if (change.Offset < proposalEnd && removalEnd > proposalStart)
                return true;
        }

        // 境界での挿入は範囲外。開始アンカーは挿入後へ、終了アンカーは
        // 挿入前へ動くため、提案対象の原文を保ったまま追従できる。
        return change.InsertionLength > 0 &&
            change.Offset > proposalStart &&
            change.Offset < proposalEnd;
    }

    private void RemoveAsInvalid(ProofreadingProposal proposal)
    {
        proposal.Invalidate();
        _proposals.Remove(proposal);
        Changed?.Invoke();
    }
}

file static class ProofreadingProposalListExtensions
{
    internal static int IndexOfReference(
        this IReadOnlyList<ProofreadingProposal> proposals,
        ProofreadingProposal target)
    {
        for (int index = 0; index < proposals.Count; index++)
        {
            if (ReferenceEquals(proposals[index], target))
                return index;
        }

        return -1;
    }
}
