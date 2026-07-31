using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>
/// 設定中のモデルに応じてGeminiまたはOpenAIへ振り分ける。
/// 設定画面でモデルを変更した後も再起動せずに次の校正から反映できる。
/// </summary>
internal sealed class ProofreadingClientRouter : IProofreadingClient
{
    private readonly SettingsService _settings;
    private readonly GeminiProofreadingClient _gemini;
    private readonly OpenAiProofreadingClient _openAi;

    internal ProofreadingClientRouter(
        SettingsService settings,
        CredentialService credentials)
    {
        _settings = settings;
        _gemini = new GeminiProofreadingClient(
            credentials,
            () => settings.Current.GeminiApiKeySource,
            ProofreadingModelCatalog.GeminiModel);
        _openAi = new OpenAiProofreadingClient(
            credentials,
            () => settings.Current.OpenAiApiKeySource,
            ProofreadingModelCatalog.OpenAiModel);
    }

    public string Model => _settings.Current.ProofreadingModel;

    private IProofreadingClient Active =>
        ProofreadingModelCatalog.IsOpenAi(Model) ? _openAi : _gemini;

    public Task<GeminiProofreadingResult> ProofreadAsync(
        ProofreadingRequest request,
        CancellationToken cancellationToken = default)
        => Active.ProofreadAsync(request, cancellationToken);

    public Task<GeminiAlternativeResult> GenerateAlternativeAsync(
        ProofreadingProposal proposal,
        string reason,
        CancellationToken cancellationToken = default)
        => Active.GenerateAlternativeAsync(proposal, reason, cancellationToken);

    public Task<GeminiStyleGuideResult> GenerateStyleGuideAsync(
        IReadOnlyList<FewShotExample> reactionHistory,
        CancellationToken cancellationToken = default)
        => Active.GenerateStyleGuideAsync(reactionHistory, cancellationToken);

    public void Dispose()
    {
        _gemini.Dispose();
        _openAi.Dispose();
    }
}
