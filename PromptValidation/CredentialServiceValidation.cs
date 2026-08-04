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
            var service = new CredentialService(path, () => " environment-test-key ");

            bool environmentPass =
                service.EnvironmentKeyAvailable &&
                service.GetApiKey(ApiKeySource.EnvironmentVariable) == "environment-test-key";
            bool missingPass = service.StoredKeyState == StoredCredentialState.Missing;

            service.SaveStoredApiKey(" stored-test-key ");
            service.SaveStoredOpenAiApiKey(" openai-test-key ");

            byte[] encrypted = File.ReadAllBytes(path);
            bool encryptedPass =
                service.StoredKeyState == StoredCredentialState.Available &&
                service.OpenAiStoredKeyState == StoredCredentialState.Available &&
                service.GetApiKey(ApiKeySource.Stored) == "stored-test-key" &&
                service.GetApiKey(ApiKeySource.Unspecified) == "stored-test-key" &&
                service.GetOpenAiApiKey(ApiKeySource.Stored) == "openai-test-key" &&
                !Encoding.UTF8.GetString(encrypted).Contains("stored-test-key", StringComparison.Ordinal);

            var reloaded = new CredentialService(path, () => null);
            bool reloadPass =
                !reloaded.EnvironmentKeyAvailable &&
                reloaded.GetApiKey(ApiKeySource.Stored) == "stored-test-key" &&
                reloaded.GetOpenAiApiKey(ApiKeySource.Stored) == "openai-test-key";

            reloaded.DeleteStoredOpenAiApiKey();
            bool openAiDeletePass =
                reloaded.OpenAiStoredKeyState == StoredCredentialState.Missing &&
                reloaded.GetApiKey(ApiKeySource.Stored) == "stored-test-key";
            reloaded.DeleteStoredApiKey();
            bool deletePass =
                reloaded.StoredKeyState == StoredCredentialState.Missing &&
                reloaded.GetApiKey(ApiKeySource.Stored) is null;

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            bool corruptPass =
                reloaded.StoredKeyState == StoredCredentialState.Unreadable &&
                reloaded.GetApiKey(ApiKeySource.Stored) is null;

            Console.WriteLine($"資格情報（環境変数）: {(environmentPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（未保存）: {(missingPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（DPAPI暗号化）: {(encryptedPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（再読込）: {(reloadPass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（Gemini/OpenAI別保存）: {(openAiDeletePass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（削除）: {(deletePass ? "PASS" : "FAIL")}");
            Console.WriteLine($"資格情報（破損検出）: {(corruptPass ? "PASS" : "FAIL")}");

            return environmentPass && missingPass && encryptedPass &&
                   reloadPass && openAiDeletePass && deletePass && corruptPass;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
