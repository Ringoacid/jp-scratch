namespace JpScratch.Models;

/// <summary>Gemini API キーをどこから取得するか（要件 3.5.5）。</summary>
public enum GeminiApiKeySource
{
    /// <summary>
    /// まだユーザーが選んでいない。環境変数を検出した起動時だけ確認を表示する。
    /// 環境変数が無い間は、保存済みキーがあればそれを使う。
    /// </summary>
    Unspecified,

    /// <summary>DPAPI で暗号化してアプリのデータフォルダへ保存したキー。</summary>
    Stored,

    /// <summary>プロセス起動時の環境変数 GEMINI_API_KEY。</summary>
    EnvironmentVariable,
}
