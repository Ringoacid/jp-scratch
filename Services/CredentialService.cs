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
/// Gemini / OpenAI API キーの取得と DPAPI 保存（要件 3.5.5）。
/// キーをログや例外文へ含めず、平文ファイルも作らない。
/// </summary>
internal sealed class CredentialService
{
    internal const string EnvironmentVariableName = "GEMINI_API_KEY";
    internal const string OpenAiEnvironmentVariableName = "OPENAI_API_KEY";

    private const int MaxCredentialFileBytes = 64 * 1024;
    private static readonly byte[] AdditionalEntropy =
        Encoding.UTF8.GetBytes("JpScratch.GeminiApiKey.v1");

    private readonly string _credentialsFile;
    private readonly Func<string?> _readEnvironmentKey;
    private readonly Func<string?> _readOpenAiEnvironmentKey;

    private sealed class StoredCredentials
    {
        public string? Gemini { get; set; }
        public string? OpenAi { get; set; }
    }

    private enum CredentialKind
    {
        Gemini,
        OpenAi,
    }

    internal CredentialService(
        string? credentialsFile = null,
        Func<string?>? readEnvironmentKey = null,
        Func<string?>? readOpenAiEnvironmentKey = null)
    {
        _credentialsFile = credentialsFile ?? AppPaths.CredentialsFile;
        _readEnvironmentKey = readEnvironmentKey ??
            (() => Environment.GetEnvironmentVariable(EnvironmentVariableName));
        _readOpenAiEnvironmentKey = readOpenAiEnvironmentKey ??
            (() => Environment.GetEnvironmentVariable(OpenAiEnvironmentVariableName));
    }

    internal bool EnvironmentKeyAvailable => GetEnvironmentApiKey() is not null;
    internal bool OpenAiEnvironmentKeyAvailable => GetEnvironmentOpenAiApiKey() is not null;

    internal StoredCredentialState StoredKeyState
    {
        get
        {
            if (!File.Exists(_credentialsFile)) return StoredCredentialState.Missing;
            return GetStoredKeyState(CredentialKind.Gemini);
        }
    }

    internal StoredCredentialState OpenAiStoredKeyState =>
        !File.Exists(_credentialsFile)
            ? StoredCredentialState.Missing
            : GetStoredKeyState(CredentialKind.OpenAi);

    /// <summary>
    /// 選択された取得元からキーを返す。未選択時は環境変数を暗黙使用せず、
    /// 保存済みキーだけを使う。
    /// </summary>
    internal string? GetApiKey(GeminiApiKeySource source)
        => source switch
        {
            GeminiApiKeySource.EnvironmentVariable => GetEnvironmentApiKey(),
            GeminiApiKeySource.Stored => GetStoredApiKey(CredentialKind.Gemini),
            GeminiApiKeySource.Unspecified => GetStoredApiKey(CredentialKind.Gemini),
            _ => null,
        };

    internal string? GetOpenAiApiKey(GeminiApiKeySource source)
        => source switch
        {
            GeminiApiKeySource.EnvironmentVariable => GetEnvironmentOpenAiApiKey(),
            GeminiApiKeySource.Stored => GetStoredApiKey(CredentialKind.OpenAi),
            GeminiApiKeySource.Unspecified => GetStoredApiKey(CredentialKind.OpenAi),
            _ => null,
        };

    internal void SaveStoredApiKey(string apiKey)
        => SaveStoredApiKey(CredentialKind.Gemini, apiKey);

    internal void SaveStoredOpenAiApiKey(string apiKey)
        => SaveStoredApiKey(CredentialKind.OpenAi, apiKey);

    private void SaveStoredApiKey(CredentialKind kind, string apiKey)
    {
        string normalized = apiKey.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));

        StoredCredentials credentials = TryGetStoredCredentials(out StoredCredentials? existing)
            ? existing!
            : new StoredCredentials();
        if (kind == CredentialKind.Gemini)
            credentials.Gemini = normalized;
        else
            credentials.OpenAi = normalized;

        SaveStoredCredentials(credentials);
    }

    internal void DeleteStoredApiKey()
        => DeleteStoredApiKey(CredentialKind.Gemini);

    internal void DeleteStoredOpenAiApiKey()
        => DeleteStoredApiKey(CredentialKind.OpenAi);

    private void DeleteStoredApiKey(CredentialKind kind)
    {
        if (!File.Exists(_credentialsFile)) return;

        if (!TryGetStoredCredentials(out StoredCredentials? credentials))
        {
            // 旧ファイルが壊れている場合も、ユーザーが削除を選んだ意図を優先する。
            File.Delete(_credentialsFile);
            return;
        }

        if (kind == CredentialKind.Gemini)
            credentials!.Gemini = null;
        else
            credentials!.OpenAi = null;

        if (string.IsNullOrWhiteSpace(credentials.Gemini) &&
            string.IsNullOrWhiteSpace(credentials.OpenAi))
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

    private string? GetEnvironmentApiKey()
    {
        string? value = _readEnvironmentKey();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? GetEnvironmentOpenAiApiKey()
    {
        string? value = _readOpenAiEnvironmentKey();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? GetStoredApiKey(CredentialKind kind)
        => TryGetStoredCredentials(out StoredCredentials? credentials)
            ? kind == CredentialKind.Gemini ? credentials!.Gemini : credentials!.OpenAi
            : null;

    private StoredCredentialState GetStoredKeyState(CredentialKind kind)
    {
        if (!TryGetStoredCredentials(out StoredCredentials? credentials))
            return StoredCredentialState.Unreadable;

        string? value = kind == CredentialKind.Gemini ? credentials!.Gemini : credentials!.OpenAi;
        return string.IsNullOrWhiteSpace(value)
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
                // 既存版はGeminiキー単体を暗号化していたため、移行時はGeminiとして読む。
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
