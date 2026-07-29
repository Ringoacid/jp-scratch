using System.IO;
using System.Security.Cryptography;
using System.Text;
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
/// Gemini API キーの取得と DPAPI 保存（要件 3.5.5）。
/// キーをログや例外文へ含めず、平文ファイルも作らない。
/// </summary>
internal sealed class CredentialService
{
    internal const string EnvironmentVariableName = "GEMINI_API_KEY";

    private const int MaxCredentialFileBytes = 64 * 1024;
    private static readonly byte[] AdditionalEntropy =
        Encoding.UTF8.GetBytes("JpScratch.GeminiApiKey.v1");

    private readonly string _credentialsFile;
    private readonly Func<string?> _readEnvironmentKey;

    internal CredentialService(
        string? credentialsFile = null,
        Func<string?>? readEnvironmentKey = null)
    {
        _credentialsFile = credentialsFile ?? AppPaths.CredentialsFile;
        _readEnvironmentKey = readEnvironmentKey ??
            (() => Environment.GetEnvironmentVariable(EnvironmentVariableName));
    }

    internal bool EnvironmentKeyAvailable => GetEnvironmentApiKey() is not null;

    internal StoredCredentialState StoredKeyState
    {
        get
        {
            if (!File.Exists(_credentialsFile)) return StoredCredentialState.Missing;
            return TryGetStoredApiKey(out _) ? StoredCredentialState.Available : StoredCredentialState.Unreadable;
        }
    }

    /// <summary>
    /// 選択された取得元からキーを返す。未選択時は環境変数を暗黙使用せず、
    /// 保存済みキーだけを使う。
    /// </summary>
    internal string? GetApiKey(GeminiApiKeySource source)
        => source switch
        {
            GeminiApiKeySource.EnvironmentVariable => GetEnvironmentApiKey(),
            GeminiApiKeySource.Stored => GetStoredApiKey(),
            GeminiApiKeySource.Unspecified => GetStoredApiKey(),
            _ => null,
        };

    internal void SaveStoredApiKey(string apiKey)
    {
        string normalized = apiKey.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));

        byte[] plaintext = Encoding.UTF8.GetBytes(normalized);
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

    internal void DeleteStoredApiKey()
    {
        if (File.Exists(_credentialsFile)) File.Delete(_credentialsFile);
    }

    private string? GetEnvironmentApiKey()
    {
        string? value = _readEnvironmentKey();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? GetStoredApiKey()
        => TryGetStoredApiKey(out string? apiKey) ? apiKey : null;

    private bool TryGetStoredApiKey(out string? apiKey)
    {
        apiKey = null;
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

            apiKey = decoded;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or CryptographicException)
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
