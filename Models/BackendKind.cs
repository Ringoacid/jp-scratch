namespace JpScratch.Models;

public enum BackendKind { Api, CodexAppServer, GitHubCopilot }

public static class BackendNames
{
    public static string NormalizeModel(BackendKind backend, string? model, string apiDefault)
        => backend == BackendKind.Api && !ProofreadingModelCatalog.IsSupported(model) ? apiDefault : model?.Trim() ?? "";
    public static string Display(BackendKind backend) => backend switch
    {
        BackendKind.Api => "APIキー",
        BackendKind.CodexAppServer => "Codex（ChatGPT）",
        BackendKind.GitHubCopilot => "GitHub Copilot",
        _ => "未対応の接続方式",
    };
}
