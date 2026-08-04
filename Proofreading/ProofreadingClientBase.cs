using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using JpScratch.Models;

namespace JpScratch.Proofreading;

/// <summary>
/// 校正クライアントの共通骨格。APIキー確認・タイムアウト・再試行・応答の解釈順序を1か所に集める。
///
/// **応答の解釈順序は派生クラスから変更できない**（<see cref="ParseRaw"/> 参照）。
/// 打ち切られた応答を「修正版全文」として差分に掛けると本文末尾を削除する提案になり、
/// 削除量が 200 書記素以下かつ変更比率 20% 以下なら安全検査を通過して誤字修正と同じ見た目で
/// 提示される（一括許可で本文が失われる）。そのため <see cref="EnsureCompleted"/> を抽象メソッドにし、
/// 新しいプロバイダーを足すときに完了判定の実装を忘れるとコンパイルエラーになるようにしている。
/// </summary>
internal abstract class ProofreadingClientBase : IProofreadingClient
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<TimeSpan> _requestTimeoutProvider;
    private readonly Func<string> _modelProvider;
    private readonly string _defaultModel;
    private readonly bool _ownsHttpClient;

    protected HttpClient HttpClient { get; }

    /// <summary>
    /// 現在のモデルID。**呼ぶたびに解決する**。1 つのプロバイダーが自動用と手動用で別のモデルを
    /// 持ちうる（例: 自動 gpt-5.6-luna / 手動 gpt-5.6-terra）ため、生成時に固定してはいけない。
    /// 1 回の実行の中で揺れないことは <see cref="ProofreadingClientRouter"/> のピン留めが保証する。
    /// </summary>
    public string Model
    {
        get
        {
            string model = _modelProvider();
            return string.IsNullOrWhiteSpace(model) ? _defaultModel : model.Trim();
        }
    }

    /// <summary>エラーメッセージへ出す表示名（例: <c>Gemini</c>）。APIキーは絶対に含めない。</summary>
    protected abstract string ProviderName { get; }

    protected ProofreadingClientBase(
        Func<string?> apiKeyProvider,
        HttpClient httpClient,
        Func<string> modelProvider,
        string defaultModel,
        Uri defaultBaseAddress,
        Func<TimeSpan, CancellationToken, Task>? delay,
        Func<TimeSpan>? requestTimeoutProvider,
        bool ownsHttpClient)
    {
        _apiKeyProvider = apiKeyProvider;
        HttpClient = httpClient;
        HttpClient.BaseAddress ??= defaultBaseAddress;
        _modelProvider = modelProvider;
        _defaultModel = defaultModel;
        _delay = delay ?? Task.Delay;
        _requestTimeoutProvider = requestTimeoutProvider ??
            (() => ProofreadingModelCatalog.DefaultRequestTimeout);
        _ownsHttpClient = ownsHttpClient;
    }

    // ---- 派生クラスが実装する差分 ----

    /// <summary>プロバイダー固有のリクエスト本文を組み立てる。</summary>
    protected abstract string BuildRequestJson(string systemInstruction, string userMessage);

    /// <summary>エンドポイント・認証ヘッダーを設定した <see cref="HttpRequestMessage"/> を作る。</summary>
    protected abstract HttpRequestMessage CreateHttpRequest(string requestJson, string apiKey);

    /// <summary>
    /// 生成が最後まで完了したことを確認する。打ち切り・安全フィルタ・拒否のときは
    /// <see cref="GeminiClientException"/>（<see cref="GeminiClientError.InvalidResponse"/>）を投げる。
    /// **本文を取り出す前に必ず呼ばれる**。
    /// </summary>
    protected abstract void EnsureCompleted(JsonElement root);

    /// <summary>応答から本文テキストを取り出す。<see cref="EnsureCompleted"/> の後にだけ呼ばれる。</summary>
    protected abstract string ExtractText(JsonElement root);

    /// <summary>応答から使用量を取り出す。取得できない項目は 0 とする。</summary>
    protected abstract GeminiUsage ExtractUsage(JsonElement root);

    // ---- 共通の呼び出し口 ----

    internal async Task<GeminiProofreadingResult> ProofreadAsync(
        string sourceText,
        CancellationToken cancellationToken = default)
        => await GenerateAsync(
            sourceText,
            ProofreadingPrompt.BuildUserMessage(sourceText),
            ProofreadingPrompt.SystemInstruction,
            cancellationToken);

    public async Task<GeminiProofreadingResult> ProofreadAsync(
        ProofreadingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await GenerateAsync(
            request.SourceText,
            ProofreadingPrompt.BuildUserMessage(
                request.SourceText,
                request.BeforeContext,
                request.AfterContext),
            request.SystemInstructionOverride ?? ProofreadingPrompt.SystemInstruction,
            cancellationToken);
    }

    public async Task<GeminiAlternativeResult> GenerateAlternativeAsync(
        ProofreadingProposal proposal,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!proposal.IsActive)
            throw new ArgumentException("失効した提案の別案は生成できません。", nameof(proposal));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("別案生成には理由が必要です。", nameof(reason));

        GeminiProofreadingResult generated = await GenerateAsync(
            proposal.Original,
            ProofreadingPrompt.BuildAlternativeUserMessage(
                proposal.Original,
                proposal.Suggestion,
                reason.Trim(),
                proposal.LeftContext,
                proposal.RightContext),
            ProofreadingPrompt.AlternativeSystemInstruction,
            cancellationToken);

        string alternative = generated.CorrectedText.Trim();
        if (string.Equals(alternative, proposal.Original, StringComparison.Ordinal) ||
            string.Equals(alternative, proposal.Suggestion, StringComparison.Ordinal))
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                $"{ProviderName} APIから有効な別案が返されませんでした。",
                usage: generated.Usage,
                elapsed: generated.Elapsed);
        }

        return new GeminiAlternativeResult(
            alternative,
            generated.Usage,
            generated.Elapsed,
            generated.Attempts);
    }

    public async Task<GeminiStyleGuideResult> GenerateStyleGuideAsync(
        IReadOnlyList<FewShotExample> reactionHistory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reactionHistory);

        GeminiRawTextResult raw = await SendWithRetryAsync(
            ProofreadingPrompt.StyleGuideSystemInstruction,
            ProofreadingPrompt.BuildStyleGuideUserMessage(reactionHistory),
            ParseRawSuccess,
            cancellationToken);

        string content = raw.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                $"{ProviderName} APIから有効なスタイルガイドが返されませんでした。",
                usage: raw.Usage,
                elapsed: raw.Elapsed);
        }

        return new GeminiStyleGuideResult(content, raw.Usage, raw.Elapsed, raw.Attempts);
    }

    public void Dispose()
    {
        if (_ownsHttpClient) HttpClient.Dispose();
    }

    private async Task<GeminiProofreadingResult> GenerateAsync(
        string sourceText,
        string userMessage,
        string systemInstruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (sourceText.Length == 0)
            throw new ArgumentException("空の文書は校正できません。", nameof(sourceText));

        return await SendWithRetryAsync(
            systemInstruction,
            userMessage,
            (body, elapsed, attempt) => ParseSuccess(body, sourceText, elapsed, attempt),
            cancellationToken);
    }

    /// <summary>
    /// APIキー確認・リクエスト構築・タイムアウト・再試行を、校正とスタイルガイド生成で共有する。
    /// 応答の解釈だけが異なるため、成功時のパースを <paramref name="parseSuccess"/> へ委譲する。
    /// </summary>
    private async Task<T> SendWithRetryAsync<T>(
        string systemInstruction,
        string userMessage,
        Func<string, TimeSpan, int, T> parseSuccess,
        CancellationToken cancellationToken)
    {
        string? apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiClientException(
                GeminiClientError.MissingApiKey,
                $"{ProviderName} APIキーが設定されていません。設定画面で登録または取得元を選択してください。");
        }

        apiKey = apiKey.Trim();
        string requestJson = BuildRequestJson(systemInstruction, userMessage);
        // 1回の呼び出しの中でタイムアウトが揺れないよう、先頭で一度だけ確定させる。
        TimeSpan requestTimeout = _requestTimeoutProvider();
        Stopwatch stopwatch = new();

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 所要時間は「今回の送信」だけを計る。先頭で一度だけ開始すると、再試行の
            // バックオフと初回の失敗分が含まれ、ログの所要時間が実送信より膨らむ。
            stopwatch.Restart();

            try
            {
                using HttpResponseMessage response =
                    await SendOnceAsync(requestJson, apiKey, requestTimeout, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 1 && IsTransient(response.StatusCode))
                    {
                        await BackoffAsync(response, cancellationToken);
                        continue;
                    }

                    throw new GeminiClientException(
                        GeminiClientError.RequestFailed,
                        $"{ProviderName} APIがHTTP {(int)response.StatusCode}を返しました。",
                        response.StatusCode);
                }

                T result = parseSuccess(body, stopwatch.Elapsed, attempt);
                stopwatch.Stop();
                return result;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // タイムアウトは再試行しない。1回目がサーバー側で完走していれば二重課金になり、
                // かつタイムアウトを長く設定しているほど待ち時間が倍になるため（要件3.5.1）。
                throw new GeminiClientException(
                    GeminiClientError.Timeout,
                    $"{ProviderName} APIへの接続が{FormatSeconds(requestTimeout)}秒以内に完了しませんでした。",
                    innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 1)
                {
                    await _delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                throw new GeminiClientException(
                    GeminiClientError.RequestFailed,
                    $"{ProviderName} APIへ接続できませんでした。",
                    innerException: ex);
            }
        }

        throw new UnreachableException();
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        string requestJson,
        string apiKey,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(requestTimeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using HttpRequestMessage request = CreateHttpRequest(requestJson, apiKey);

        return await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            linked.Token);
    }

    private async Task BackoffAsync(
        HttpResponseMessage? response,
        CancellationToken cancellationToken)
    {
        // 429 で Retry-After が指定されていればそれに従う（Delta と HTTP-date の両形式に対応）。
        // 無ければ固定1秒。長すぎる待ちで無駄に占有しないよう、下限1秒・上限5秒でクランプする。
        TimeSpan delay = TimeSpan.FromSeconds(1);
        if (response is { StatusCode: HttpStatusCode.TooManyRequests } &&
            response.Headers.RetryAfter is { } retryAfter)
        {
            TimeSpan? retryAfterDelay = retryAfter.Delta ??
                (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (retryAfterDelay is { } d)
                delay = TimeSpan.FromSeconds(Math.Clamp(d.TotalSeconds, 1, 5));
        }

        await _delay(delay, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           (int)statusCode >= 500;

    private static string FormatSeconds(TimeSpan timeout)
        => timeout.TotalSeconds.ToString(
            timeout.TotalSeconds % 1 == 0 ? "0" : "0.#",
            System.Globalization.CultureInfo.InvariantCulture);

    private GeminiProofreadingResult ParseSuccess(
        string body,
        string sourceText,
        TimeSpan elapsed,
        int attempts)
    {
        (string rawText, GeminiUsage usage) = ParseRaw(body);
        // 送信時に逃がした閉じタグを元へ戻してから差分を取る（逃がしたままだと
        // 「\/document を /document へ直す」提案が出る）。
        string correctedText = ProofreadingPrompt.UnescapeDocumentBoundary(rawText);
        DocumentDiffResult diff = DocumentDiff.Create(sourceText, correctedText);
        return new GeminiProofreadingResult(correctedText, diff, usage, elapsed, attempts);
    }

    private GeminiRawTextResult ParseRawSuccess(
        string body,
        TimeSpan elapsed,
        int attempts)
    {
        (string text, GeminiUsage usage) = ParseRaw(body);
        return new GeminiRawTextResult(text, usage, elapsed, attempts);
    }

    /// <summary>
    /// 応答の解釈順序を固定する。**完了判定が本文抽出より前にあることが本メソッドの存在理由**であり、
    /// 派生クラスからこの順序は変えられない（クラス冒頭の説明を参照）。
    /// </summary>
    private (string Text, GeminiUsage Usage) ParseRaw(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            EnsureCompleted(root);
            string text = ExtractText(root);
            GeminiUsage usage = ExtractUsage(root);
            return (text, usage);
        }
        catch (GeminiClientException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException
                                   or InvalidOperationException
                                   or KeyNotFoundException)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                $"{ProviderName} APIのレスポンスを読み取れませんでした。",
                innerException: ex);
        }
    }

    protected static int ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) &&
           value.TryGetInt32(out int count)
            ? count
            : 0;
}
