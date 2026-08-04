using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

internal enum StoredCredentialState
{
    Missing,
    Available,
    Unreadable,
}

/// <summary>
/// プロバイダー別 API キーの取得と DPAPI 保存（要件 3.5.5）。
/// キーをログや例外文へ含めず、平文ファイルも作らない。
/// </summary>
internal sealed class CredentialService
{
    private const int MaxCredentialFileBytes = 64 * 1024;
    private static readonly byte[] AdditionalEntropy =
        Encoding.UTF8.GetBytes("JpScratch.GeminiApiKey.v1");

    private readonly string _credentialsFile;
    private readonly Func<ApiProvider, string?> _readEnvironmentKey;

    /// <summary>
    /// 保存形式。プロパティを足すだけで新プロバイダーへ対応できる
    /// （System.Text.Json は欠けたプロパティを許容するので旧ファイルもそのまま読める）。
    /// </summary>
    private sealed class StoredCredentials
    {
        public string? Gemini { get; set; }
        public string? OpenAi { get; set; }
        public string? Anthropic { get; set; }
        public string? Plamo { get; set; }

        public string? Get(ApiProvider provider)
            => provider switch
            {
                ApiProvider.Google => Gemini,
                ApiProvider.OpenAi => OpenAi,
                ApiProvider.Anthropic => Anthropic,
                ApiProvider.PreferredNetworks => Plamo,
                _ => null,
            };

        public void Set(ApiProvider provider, string? value)
        {
            switch (provider)
            {
                case ApiProvider.Google: Gemini = value; break;
                case ApiProvider.OpenAi: OpenAi = value; break;
                case ApiProvider.Anthropic: Anthropic = value; break;
                case ApiProvider.PreferredNetworks: Plamo = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(provider));
            }
        }

        public bool IsEmpty
            => string.IsNullOrWhiteSpace(Gemini) &&
               string.IsNullOrWhiteSpace(OpenAi) &&
               string.IsNullOrWhiteSpace(Anthropic) &&
               string.IsNullOrWhiteSpace(Plamo);
    }

    internal CredentialService(
        string? credentialsFile = null,
        Func<ApiProvider, string?>? readEnvironmentKey = null)
    {
        _credentialsFile = credentialsFile ?? AppPaths.CredentialsFile;
        _readEnvironmentKey = readEnvironmentKey ??
            (provider => Environment.GetEnvironmentVariable(
                ProofreadingModelCatalog.EnvironmentVariableName(provider)));
    }

    internal bool EnvironmentKeyAvailable(ApiProvider provider)
        => GetEnvironmentApiKey(provider) is not null;

    internal StoredCredentialState StoredKeyState(ApiProvider provider)
        => !File.Exists(_credentialsFile)
            ? StoredCredentialState.Missing
            : GetStoredKeyState(provider);

    /// <summary>
    /// 選択された取得元からキーを返す。未選択時は環境変数を暗黙使用せず、
    /// 保存済みキーだけを使う。
    /// </summary>
    internal string? GetApiKey(ApiProvider provider, ApiKeySource source)
        => source switch
        {
            ApiKeySource.EnvironmentVariable => GetEnvironmentApiKey(provider),
            ApiKeySource.Stored => GetStoredApiKey(provider),
            ApiKeySource.Unspecified => GetStoredApiKey(provider),
            _ => null,
        };

    internal void SaveStoredApiKey(ApiProvider provider, string apiKey)
    {
        string normalized = apiKey.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));

        StoredCredentials credentials = TryGetStoredCredentials(out StoredCredentials? existing)
            ? existing!
            : new StoredCredentials();
        credentials.Set(provider, normalized);
        SaveStoredCredentials(credentials);
    }

    internal void DeleteStoredApiKey(ApiProvider provider)
    {
        if (!File.Exists(_credentialsFile)) return;

        if (!TryGetStoredCredentials(out StoredCredentials? credentials))
        {
            // 旧ファイルが壊れている場合も、ユーザーが削除を選んだ意図を優先する。
            File.Delete(_credentialsFile);
            return;
        }

        credentials!.Set(provider, null);

        if (credentials.IsEmpty)
        {
            File.Delete(_credentialsFile);
            return;
        }

        SaveStoredCredentials(credentials);
    }

    private void SaveStoredCredentials(StoredCredentials credentials)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(credentials));
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(
                plaintext,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);
            AtomicFile.WriteAllBytes(_credentialsFile, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private string? GetEnvironmentApiKey(ApiProvider provider)
    {
        string? value = _readEnvironmentKey(provider);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? GetStoredApiKey(ApiProvider provider)
        => TryGetStoredCredentials(out StoredCredentials? credentials)
            ? credentials!.Get(provider)
            : null;

    private StoredCredentialState GetStoredKeyState(ApiProvider provider)
    {
        if (!TryGetStoredCredentials(out StoredCredentials? credentials))
            return StoredCredentialState.Unreadable;

        return string.IsNullOrWhiteSpace(credentials!.Get(provider))
            ? StoredCredentialState.Missing
            : StoredCredentialState.Available;
    }

    private bool TryGetStoredCredentials(out StoredCredentials? credentials)
    {
        credentials = null;
        byte[]? protectedBytes = null;
        byte[]? plaintext = null;

        try
        {
            var file = new FileInfo(_credentialsFile);
            if (!file.Exists || file.Length is <= 0 or > MaxCredentialFileBytes) return false;

            protectedBytes = File.ReadAllBytes(_credentialsFile);
            plaintext = ProtectedData.Unprotect(
                protectedBytes,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);

            string decoded = Encoding.UTF8.GetString(plaintext).Trim();
            if (decoded.Length == 0) return false;

            if (!decoded.StartsWith("{", StringComparison.Ordinal))
            {
                // 最初期版はGeminiキー単体を暗号化していたため、移行時はGeminiとして読む。
                credentials = new StoredCredentials { Gemini = decoded };
                return true;
            }

            credentials = JsonSerializer.Deserialize<StoredCredentials>(decoded);
            if (credentials is null) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or CryptographicException
                                   or JsonException)
        {
            return false;
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
