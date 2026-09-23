using System.Windows;
using JpScratch.Models;
using JpScratch.Proofreading;

namespace JpScratch.Views;

public partial class MainWindow
{
    private bool _preparingBackend;
    private bool _allowUnknownQuota;
    private static string FormatTokenUsage(GeminiUsage usage) => usage.IsKnown
        ? $"入力 {usage.PromptTokens:N0}、出力・推論 {usage.BillableOutputTokens:N0} tokens"
        : "トークン使用量は不明";
    private CancellationTokenSource _generationCancellation = new();
    private ProofreadingClientRouter Router => (ProofreadingClientRouter)_proofreadingClient;
    private BackendKind BackendFor(ProofreadingPurpose? purpose = null) => purpose is { } p ? Router.BackendFor(p) : Router.Backend;
    private bool SubscriptionAutomaticBlocked => BackendFor(ProofreadingPurpose.Automatic) != BackendKind.Api &&
        (!_settings.Current.SubscriptionAutomaticEnabled || Router.Subscriptions.Suspended(BackendFor(ProofreadingPurpose.Automatic)));

    private async Task<bool> EnsureBackendAsync(ProofreadingPurpose purpose, bool automatic)
    {
        BackendKind backend = BackendFor(purpose);
        _allowUnknownQuota = false;
        if (backend == BackendKind.Api)
        {
            // 互換接続はプロファイルごとにキーを持ち、ローカルサーバーではキーなしも有効。
            // 送信時の認証ヘッダーは OpenAiCompatibleProofreadingClient が決める。
            if (ProofreadingModelCatalog.ProviderOf(ModelForPurpose(purpose)) == ApiProvider.OpenAiCompatible)
                return true;
            if (!string.IsNullOrWhiteSpace(GetActiveApiKey(purpose))) return true;
            SetProofreadingStatus($"{ActiveProviderName(purpose)} APIキーを設定してください", force: true);
            return false;
        }
        if (automatic && SubscriptionAutomaticBlocked) return false;
        _preparingBackend = true;
        try
        {
            _proofreadingTimer.Stop();
            string path = Router.PathFor(backend);
            string model = Router.ModelFor(purpose);
            var state = Router.Subscriptions.State(backend);
            if (state is null || !SubscriptionPolicy.IsFresh(state.CheckedAt, DateTimeOffset.Now))
                state = await Router.Subscriptions.RefreshAsync(backend, path);
            if (backend != BackendFor(purpose) || path != Router.PathFor(backend) || model != Router.ModelFor(purpose))
                throw new InvalidOperationException("接続設定が変更されました。再実行してください。");
            if (!state.Authenticated) throw new InvalidOperationException("設定画面の「契約サービス」でログインしてください。");
            if (state.Exhausted) throw new InvalidOperationException("利用枠に達したため送信を停止しています。");
            if (!state.Models.Any(m => m.Id == model)) throw new InvalidOperationException("選択モデルは現在利用できません。設定画面でモデルを選択してください。");
            if (state.UsedPercent is null)
            {
                if (automatic) throw new InvalidOperationException("残量を取得できないため自動校正を停止しています。設定画面で接続を確認してください。");
                if (MessageBox.Show(this, $"{BackendNames.Display(backend)} の残量を確認できません。契約枠を消費し、契約設定によって追加料金が発生する可能性があります。実行しますか？",
                    "利用枠の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
                _allowUnknownQuota = true;
            }
            if (state.UsedPercent >= 80) SetProofreadingStatus(state.Description, force: true);
            return true;
        }
        catch (Exception ex)
        {
            Router.Subscriptions.Suspend(backend);
            SetProofreadingStatus(ex is OperationCanceledException ? "接続確認がタイムアウトしました" : ex.Message, force: true);
            return false;
        }
        finally { _preparingBackend = false; UpdateTrayIconState(); }
    }

    private bool CanCancelGeneration => !_generationCancellation.IsCancellationRequested &&
        (_proofreadingRunInProgress || _alternativeInProgress || _styleGuideGenerationInProgress);

    private void CancelGeneration_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCancelGeneration) return;
        _generationCancellation.Cancel();
        SetProofreadingStatus("校正の中断を要求しました", force: true);
    }
}
