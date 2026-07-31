using System.Text.Json.Serialization;

namespace JpScratch.Models;

/// <summary>ウィンドウの表示位置の決め方（要件 3.1.2）。</summary>
public enum WindowPositionMode
{
    /// <summary>タスクバーのあるモニタの作業領域右下に吸着する（既定）。</summary>
    TaskbarBottomRight,

    /// <summary>ホットキー起動時に、マウスカーソルのあるモニタの右下に出す。</summary>
    CursorMonitorBottomRight,

    /// <summary>前回置いた位置を復元する。初回のみ右下。</summary>
    RememberLast,
}

/// <summary>
/// テーマ（要件 3.2.2）。
/// .NET 10 の <c>System.Windows.ThemeMode</c> と名前が衝突するため AppTheme とする。
/// </summary>
public enum AppTheme
{
    /// <summary>OS のライト/ダーク設定に追従する。</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// settings.json の内容。既定値は要件定義書の「既定」列に一致させてある。
/// </summary>
public sealed class AppSettings
{
    // ---- 3.1.2 表示位置とサイズ ----
    public WindowPositionMode PositionMode { get; set; } = WindowPositionMode.TaskbarBottomRight;
    public double WindowWidth { get; set; } = 480;
    public double WindowHeight { get; set; } = 600;
    public double? LastLeft { get; set; }
    public double? LastTop { get; set; }
    public bool Topmost { get; set; } = true;

    // ---- 3.1.3 隠すときの挙動 ----
    public bool HideOnFocusLost { get; set; } = true;

    /// <summary>
    /// 隠す直前に本文をクリップボードへ入れる。フォーカス喪失に限らず、
    /// ホットキー・Esc・閉じるボタンなど、すべての非表示経路が対象（要件 3.1.3）。
    /// </summary>
    public bool CopyToClipboardOnHide { get; set; } = false;

    /// <summary>
    /// v1 で書き出していた旧キーの読み替え。setter だけなので settings.json には出力されない。
    /// </summary>
    [JsonPropertyName("copyToClipboardOnFocusLost")]
    public bool LegacyCopyToClipboardOnFocusLost { set => CopyToClipboardOnHide = value; }

    // ---- 3.1.4 グローバルホットキー ----
    public string HotkeyToggle { get; set; } = "Alt+Space";
    public string HotkeyCopyAndHide { get; set; } = "Ctrl+Alt+Enter";

    // ---- 3.2.2 エディタ ----
    /// <summary>空文字なら <see cref="FontFallback"/> の順に解決する。</summary>
    public string FontFamily { get; set; } = "";
    public double FontSize { get; set; } = 14;
    public bool WordWrap { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public bool ShowWhitespace { get; set; } = false;
    public bool HighlightCurrentLine { get; set; } = true;
    public bool ShowEndOfLine { get; set; } = false;
    public AppTheme Theme { get; set; } = AppTheme.System;

    // ---- 3.2.4 永続化 ----
    public int AutoSaveDebounceMs { get; set; } = 1000;
    public int TrashRetentionDays { get; set; } = 30;

    // ---- 3.5.5 API キー ----
    /// <summary>キー本体は settings.json に入れず、取得元の選択だけを記録する。</summary>
    public GeminiApiKeySource GeminiApiKeySource { get; set; } = GeminiApiKeySource.Unspecified;
    /// <summary>OpenAI APIキーの取得元。Geminiとは別のキーを管理する。</summary>
    public GeminiApiKeySource OpenAiApiKeySource { get; set; } = GeminiApiKeySource.Unspecified;

    // ---- 校正モデル ----
    /// <summary>実際に校正へ使うモデルID。料金表のキーと一致させる。</summary>
    public string ProofreadingModel { get; set; } = ProofreadingModelCatalog.GeminiModel;

    // ---- 3.3.1 自動校正 ----
    public bool AutoProofreadingEnabled { get; set; } = true;
    public int ProofreadingDebounceMs { get; set; } = 2000;
    public int ProofreadingMinimumIntervalSeconds { get; set; } = 10;
    /// <summary>課金APIを実行する前に確認ダイアログを表示するか。</summary>
    public bool ConfirmPaidApiCalls { get; set; } = true;

    // ---- 3.6.3 課金ガード ----
    /// <summary>
    /// 月間上限額（USD）。要件 3.6.3 の既定は $2.00。
    /// <c>0以下は無制限</c>として扱う（自動チェックのガード・進捗バーとも無効になる）。
    /// 負値は不正な入力として <see cref="SettingsService"/> の正規化で 0（無制限）へ倒す。
    /// </summary>
    public decimal MonthlyLimitUsd { get; set; } = 2.00m;

    /// <summary>
    /// 上限接近の警告閾値。0〜1の割合で、既定 0.80（＝80%）。
    /// 不正な値（0以下または1超）は <see cref="SettingsService"/> の正規化で既定へ戻す。
    /// </summary>
    public decimal MonthlyLimitWarningRatio { get; set; } = 0.80m;

    // ---- 3.6.2 課金ログの保持 ----
    /// <summary>
    /// <c>api_calls</c> の明細を残す月数。要件 3.6.2 の既定は 12 か月。
    /// これより古い月の明細は起動時に日次サマリ（<c>api_call_daily</c>）へ圧縮して削除する。
    /// <c>0以下は無期限</c>として扱い、圧縮を一切行わない（<see cref="MonthlyLimitUsd"/> と同じ規約）。
    /// 期間合計は圧縮後もサマリを合算するので変わらない。失われるのは1件ごとの明細だけ。
    /// </summary>
    public int ApiLogRetentionMonths { get; set; } = 12;

    // ---- その他 ----
    public bool StartWithWindows { get; set; } = true;
    /// <summary>タブ名の自動生成で使う最大文字数（要件 3.2.1）。</summary>
    public int AutoTitleMaxLength { get; set; } = 20;

    /// <summary>
    /// フォント未指定時のフォールバック順（要件 3.2.2）。
    /// 日本語が化けない・等幅すぎない・Windows 11 に必ず載っている、の順で選ぶ。
    /// </summary>
    [JsonIgnore]
    public static IReadOnlyList<string> FontFallback { get; } =
        new[] { "Meiryo UI", "游ゴシック", "Yu Gothic UI", "Yu Gothic", "MS Gothic" };

    [JsonIgnore]
    public HotkeySpec ToggleHotkey => HotkeySpec.ParseOrDefault(
        HotkeyToggle, new HotkeySpec(System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.Space));

    [JsonIgnore]
    public HotkeySpec CopyAndHideHotkey => HotkeySpec.ParseOrDefault(
        HotkeyCopyAndHide,
        new HotkeySpec(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt,
                       System.Windows.Input.Key.Enter));

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
