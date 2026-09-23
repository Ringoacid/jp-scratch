using JpScratch.Models;

namespace JpScratch.Proofreading;

internal sealed class SubscriptionProofreadingClient(SubscriptionService service, BackendKind backend,
    string path, string model, string? account, bool allowUnknownQuota, TimeSpan timeout, int interval) : IProofreadingClient
{
    public string Model => model;
    private Task<GeminiRawTextResult> Send(string instruction, string prompt, CancellationToken token, Func<bool>? isCurrent = null)
        => service.SendAsync(backend, path, model, account, allowUnknownQuota, timeout, interval, instruction, prompt, token, isCurrent);

    public async Task<GeminiProofreadingResult> ProofreadAsync(ProofreadingRequest request, CancellationToken cancellationToken = default)
        => ProofreadingResultFactory.Create(request.SourceText, await Send(request.SystemInstructionOverride ?? ProofreadingPrompt.SystemInstruction,
            ProofreadingPrompt.BuildUserMessage(request.SourceText, request.BeforeContext, request.AfterContext), cancellationToken, request.IsCurrentBeforeSend));

    public async Task<GeminiAlternativeResult> GenerateAlternativeAsync(ProofreadingProposal proposal, string reason, CancellationToken cancellationToken = default)
    {
        if (!proposal.IsActive || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("別案の対象または理由が無効です。");
        var raw = await Send(ProofreadingPrompt.AlternativeSystemInstruction, ProofreadingPrompt.BuildAlternativeUserMessage(
            proposal.Original, proposal.Suggestion, reason.Trim(), proposal.LeftContext, proposal.RightContext), cancellationToken);
        return ProofreadingResultFactory.Alternative(proposal, ProofreadingResultFactory.Create(proposal.Original, raw));
    }

    public async Task<GeminiStyleGuideResult> GenerateStyleGuideAsync(IReadOnlyList<FewShotExample> reactionHistory, CancellationToken cancellationToken = default)
    {
        var raw = await Send(ProofreadingPrompt.StyleGuideSystemInstruction, ProofreadingPrompt.BuildStyleGuideUserMessage(reactionHistory), cancellationToken);
        if (string.IsNullOrWhiteSpace(raw.Text)) throw new GeminiClientException(GeminiClientError.InvalidResponse, "スタイルガイドが空です。", usage: raw.Usage);
        return new(raw.Text.Trim(), raw.Usage, raw.Elapsed, raw.Attempts);
    }
    public void Dispose() { }
}
