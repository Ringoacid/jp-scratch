using ICSharpCode.AvalonEdit.Document;

namespace JpScratch.Proofreading;

internal enum ProposalState
{
    Active,
    Applied,
    Rejected,
    Invalidated
}

/// <summary>
/// 差分から作られた1件の校正提案。位置は TextAnchor で本文編集へ追従する。
/// </summary>
internal sealed class ProofreadingProposal
{
    private readonly TextAnchor _start;
    private readonly TextAnchor _end;

    internal ProofreadingProposal(TextDocument document, DocumentChange change)
    {
        Original = change.Original;
        Suggestion = change.Suggestion;
        LeftContext = change.LeftContext;
        RightContext = change.RightContext;

        _start = document.CreateAnchor(change.Start);
        _start.MovementType = AnchorMovementType.AfterInsertion;
        _start.SurviveDeletion = true;

        _end = document.CreateAnchor(change.Start + change.Length);
        _end.MovementType = AnchorMovementType.BeforeInsertion;
        _end.SurviveDeletion = true;
    }

    internal string Original { get; }
    internal string Suggestion { get; }
    internal string LeftContext { get; }
    internal string RightContext { get; }
    internal ProposalState State { get; private set; } = ProposalState.Active;

    internal bool IsActive =>
        State == ProposalState.Active &&
        !_start.IsDeleted &&
        !_end.IsDeleted &&
        _start.Offset <= _end.Offset;

    internal int Start =>
        IsActive
            ? _start.Offset
            : throw new InvalidOperationException("失効した提案の位置は取得できません。");

    internal int Length =>
        IsActive
            ? _end.Offset - _start.Offset
            : throw new InvalidOperationException("失効した提案の範囲は取得できません。");

    internal void MarkApplied() => SetTerminalState(ProposalState.Applied);
    internal void MarkRejected() => SetTerminalState(ProposalState.Rejected);
    internal void Invalidate() => SetTerminalState(ProposalState.Invalidated);

    private void SetTerminalState(ProposalState state)
    {
        if (State == ProposalState.Active)
            State = state;
    }
}
