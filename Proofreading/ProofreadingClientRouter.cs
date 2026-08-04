using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>
/// 用途（自動 / 手動）と設定中のモデルに応じて、プロバイダー別のクライアントへ振り分ける。
/// 設定画面でモデルを変更した後も再起動せずに次の校正から反映できる。
///
/// クライアントは**使うときに初めて作る**。4 プロバイダー分の <see cref="System.Net.Http.HttpClient"/> を
/// 起動時にまとめて作ると、実際には 1 つしか使わないのに接続プールを 4 つ抱えることになるため
/// （起動速度とメモリを優先する方針）。
/// </summary>
internal sealed class ProofreadingClientRouter : IProofreadingClient
{
    private readonly SettingsService _settings;
    private readonly Dictionary<ApiProvider, Lazy<IProofreadingClient>> _clients;

    private string? _pinnedModel;
    private ProofreadingPurpose? _pinnedPurpose;

    internal ProofreadingClientRouter(
        SettingsService settings,
        CredentialService credentials)
    {
        _settings = settings;

        // どのクライアントも「現在のモデル」「現在のタイムアウト」「現在の用途」を遅延解決する。
        // 1 つのプロバイダーが自動用と手動用で別のモデルを持ちうるため（例: 自動 Luna / 手動 Terra）、
        // 生成時に固定してはいけない。実行中に揺れないことは PinModel が保証する。
        _clients = new Dictionary<ApiProvider, Lazy<IProofreadingClient>>
        {
            [ApiProvider.Google] = new(() => new GeminiProofreadingClient(
                credentials,
                () => settings.Current.GeminiApiKeySource,
                () => Model,
                () => CurrentTimeout,
                () => CurrentPurpose)),
            [ApiProvider.OpenAi] = new(() => new OpenAiProofreadingClient(
                credentials,
                () => settings.Current.OpenAiApiKeySource,
                () => Model,
                () => CurrentTimeout,
                () => CurrentPurpose)),
            [ApiProvider.Anthropic] = new(() => new AnthropicProofreadingClient(
                credentials,
                () => settings.Current.AnthropicApiKeySource,
                () => Model,
                () => CurrentTimeout,
                () => CurrentPurpose)),
            [ApiProvider.PreferredNetworks] = new(() => new PlamoProofreadingClient(
                credentials,
                () => settings.Current.PlamoApiKeySource,
                () => Model,
                () => CurrentTimeout,
                () => CurrentPurpose)),
        };
    }

    public string Model => _pinnedModel ?? ModelFor(CurrentPurpose);

    /// <summary>
    /// ピン留めされていないときは自動用として扱う。実行経路はすべて実行前にピン留めするため、
    /// ここへ落ちるのは表示用の参照だけ。安い側へ倒しておく。
    /// </summary>
    private ProofreadingPurpose CurrentPurpose => _pinnedPurpose ?? ProofreadingPurpose.Automatic;

    private TimeSpan CurrentTimeout => TimeoutFor(CurrentPurpose);

    internal string ModelFor(ProofreadingPurpose purpose)
        => purpose == ProofreadingPurpose.Manual
            ? _settings.Current.ManualProofreadingModel
            : _settings.Current.AutoProofreadingModel;

    internal TimeSpan TimeoutFor(ProofreadingPurpose purpose)
        => ProofreadingModelCatalog.ClampTimeout(TimeSpan.FromSeconds(
            purpose == ProofreadingPurpose.Manual
                ? _settings.Current.ManualProofreadingTimeoutSeconds
                : _settings.Current.AutoProofreadingTimeoutSeconds));

    /// <summary>
    /// 1回の校正実行の間だけ使う用途とモデルを固定する。実行中に設定画面でモデルを切り替えても、
    /// 同一実行の途中でプロバイダ・APIキー取得元・料金単価・タイムアウトが揺れないようにする。
    /// 呼び出し側（MainWindow）は実行開始前に用途を指定して固定し、対応する finally で
    /// <see cref="UnpinModel"/> を必ず呼ぶ。
    /// </summary>
    internal void PinModel(ProofreadingPurpose purpose)
    {
        _pinnedPurpose = purpose;
        _pinnedModel = ModelFor(purpose);
    }

    internal void UnpinModel()
    {
        _pinnedModel = null;
        _pinnedPurpose = null;
    }

    private IProofreadingClient Active =>
        _clients[ProofreadingModelCatalog.ProviderOf(Model)].Value;

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
        foreach (Lazy<IProofreadingClient> client in _clients.Values)
        {
            // 一度も使っていないプロバイダーは生成自体していないので破棄も不要。
            if (client.IsValueCreated) client.Value.Dispose();
        }
    }
}
