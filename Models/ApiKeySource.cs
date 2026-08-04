namespace JpScratch.Models;

/// <summary>
/// API キーをどこから取得するか（要件 3.5.5）。プロバイダーごとに 1 つ持つ。
/// v3 までは <c>GeminiApiKeySource</c> という名前だったが、OpenAI 以降でも共有するため改名した。
/// 型名は settings.json に現れない（プロパティ名は据え置き）ので、設定の互換性は保たれる。
/// </summary>
public enum ApiKeySource
{
    /// <summary>
    /// まだユーザーが選んでいない。環境変数を検出した起動時だけ確認を表示する。
    /// 環境変数が無い間は、保存済みキーがあればそれを使う。
    /// </summary>
    Unspecified,

    /// <summary>DPAPI で暗号化してアプリのデータフォルダへ保存したキー。</summary>
    Stored,

    /// <summary>プロセス起動時の環境変数（プロバイダーごとに名前が異なる）。</summary>
    EnvironmentVariable,
}

/// <summary>校正 API のプロバイダー（要件 3.5.1）。</summary>
public enum ApiProvider
{
    Google,
    OpenAi,
    Anthropic,
    PreferredNetworks,
}

/// <summary>
/// 校正実行の用途（要件 3.5.1）。使うモデル・タイムアウト・思考量がこれで決まる。
/// </summary>
public enum ProofreadingPurpose
{
    /// <summary>入力中の自動校正と、理由つき別案生成。高速なモデルを使う。</summary>
    Automatic,

    /// <summary>明示的な校正実行と、スタイルガイドの自動生成。低速でも品質の高いモデルを使う。</summary>
    Manual,
}
