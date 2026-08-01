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
    private string? _pinnedModel;

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

    public string Model => _pinnedModel ?? _settings.Current.ProofreadingModel;

    /// <summary>
    /// 1回の校正実行の間だけ使うモデルを固定する。実行中に設定画面でモデルを切り替えても、
    /// 同一実行の途中でプロバイダ・APIキー取得元・料金単価が揺れないようにする。
    /// 呼び出し側（MainWindow）は実行開始前に現在のモデルを固定し、対応する finally で
    /// <see cref="UnpinModel"/> を必ず呼ぶ。
    /// </summary>
    internal void PinModel(string model) => _pinnedModel = model;

    internal void UnpinModel() => _pinnedModel = null;

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
