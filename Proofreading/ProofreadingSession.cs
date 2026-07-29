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

    /// <summary>
    /// モデルが返した修正版全文を現在の本文と比較し、既存提案を置き換える。
    /// 安全検査に失敗した場合は提案を空にし、拒否理由を呼び出し元へ返す。
    /// </summary>
    internal DocumentDiffResult LoadCorrectedDocument(string corrected)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        DocumentDiffResult result = DocumentDiff.Create(_document.Text, corrected);
        _proposals.Clear();
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
