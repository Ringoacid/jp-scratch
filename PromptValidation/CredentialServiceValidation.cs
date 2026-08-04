using System.Text;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class CredentialServiceValidation
{
    internal static bool RunSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratch-CredentialValidation-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "credentials.dat");

        try
        {
            var service = new CredentialService(
                path,
                provider => provider == ApiProvider.Google ? " environment-test-key " : null);

            bool environmentPass =
                service.EnvironmentKeyAvailable(ApiProvider.Google) &&
                !service.EnvironmentKeyAvailable(ApiProvider.Anthropic) &&
                service.GetApiKey(ApiProvider.Google, ApiKeySource.EnvironmentVariable) ==
                    "environment-test-key";
            bool missingPass =
                service.StoredKeyState(ApiProvider.Google) == StoredCredentialState.Missing;

            service.SaveStoredApiKey(ApiProvider.Google, " stored-test-key ");
            service.SaveStoredApiKey(ApiProvider.OpenAi, " openai-test-key ");
            service.SaveStoredApiKey(ApiProvider.Anthropic, " anthropic-test-key ");
            service.SaveStoredApiKey(ApiProvider.PreferredNetworks, " plamo-test-key ");

            byte[] encrypted = File.ReadAllBytes(path);
            bool encryptedPass =
                service.StoredKeyState(ApiProvider.Google) == StoredCredentialState.Available &&
                service.StoredKeyState(ApiProvider.OpenAi) == StoredCredentialState.Available &&
                service.GetApiKey(ApiProvider.Google, ApiKeySource.Stored) == "stored-test-key" &&
                service.GetApiKey(ApiProvider.Google, ApiKeySource.Unspecified) == "stored-test-key" &&
                service.GetApiKey(ApiProvider.OpenAi, ApiKeySource.Stored) == "openai-test-key" &&
                !Encoding.UTF8.GetString(encrypted).Contains("stored-test-key", StringComparison.Ordinal);

            var reloaded = new CredentialService(path, _ => null);
            // 4プロバイダーが互いを潰さずに1ファイルへ同居していることを確認する。
            bool reloadPass =
                !reloaded.EnvironmentKeyAvailable(ApiProvider.Google) &&
                reloaded.GetApiKey(ApiProvider.Google, ApiKeySource.Stored) == "stored-test-key" &&
                reloaded.GetApiKey(ApiProvider.OpenAi, ApiKeySource.Stored) == "openai-test-key" &&
                reloaded.GetApiKey(ApiProvider.Anthropic, ApiKeySource.Stored) == "anthropic-test-key" &&
                reloaded.GetApiKey(ApiProvider.PreferredNetworks, ApiKeySource.Stored) == "plamo-test-key";

            reloaded.DeleteStoredApiKey(ApiProvider.OpenAi);
            bool perProviderDeletePass =
                reloaded.StoredKeyState(ApiProvider.OpenAi) == StoredCredentialState.Missing &&
                reloaded.GetApiKey(ApiProvider.Google, ApiKeySource.Stored) == "stored-test-key" &&
                reloaded.GetApiKey(ApiProvider.Anthropic, ApiKeySource.Stored) == "anthropic-test-key";

            reloaded.DeleteStoredApiKey(ApiProvider.Google);
            reloaded.DeleteStoredApiKey(ApiProvider.Anthropic);
            reloaded.DeleteStoredApiKey(ApiProvider.PreferredNetworks);
            bool deletePass =
                reloaded.StoredKeyState(ApiProvider.Google) == StoredCredentialState.Missing &&
                reloaded.GetApiKey(ApiProvider.Google, ApiKeySource.Stored) is null &&
                !File.Exists(path);

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            bool corruptPass =
                reloaded.StoredKeyState(ApiProvider.Google) == StoredCredentialState.Unreadable &&
                reloaded.GetApiKey(ApiProvider.Google, ApiKeySource.Stored) is null;

            Console.WriteLine($"資格情報（環境変数）: {(environmentPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（未保存）: {(missingPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（DPAPI暗号化）: {(encryptedPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（4プロバイダー再読込）: {(reloadPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（プロバイダー別削除）: {(perProviderDeletePass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（全削除）: {(deletePass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（破損検出）: {(corruptPass ? "PASS" : "FAIL")}");

            return environmentPass && missingPass && encryptedPass &&
                   reloadPass && perProviderDeletePass && deletePass && corruptPass;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
