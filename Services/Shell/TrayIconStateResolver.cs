namespace JpScratch.Services;

/// <summary>トレイアイコンが示す状態（要件 3.1.1）。</summary>
internal enum TrayIconState
{
    /// <summary>通常。</summary>
    Normal,

    /// <summary>校正リクエスト中（自動・手動・別案生成を含む、API 応答待ち）。</summary>
    Proofreading,

    /// <summary>直近の API 呼び出しが失敗したまま。次の成功で解除する。</summary>
    ApiError,

    /// <summary>当月累計が月間上限に達している（3.6.3）。</summary>
    LimitReached,
}

/// <summary>
/// 同時に成り立ちうる複数の条件から、トレイに出す 1 つの状態を決める（要件 3.1.1）。
/// WPF・DB に依存しない純粋関数として書き、<c>PromptValidation</c> から優先順位を検証する。
/// </summary>
internal static class TrayIconStateResolver
{
    /// <summary>
    /// 優先順位は <see cref="TrayIconState.Proofreading"/> &gt; <see cref="TrayIconState.ApiError"/>
    /// &gt; <see cref="TrayIconState.LimitReached"/>。
    ///
    /// 「校正中」を最優先にするのは、これだけが**自分で消える一時的な状態**だから。
    /// 応答が返れば必ず解除され、そのとき残りの条件で再計算されるので、エラーや上限到達の表示が
    /// 失われることはない。逆にエラー・上限到達を優先すると、上限到達中に手動校正を実行しても
    /// アイコンが何も変わらず、通信しているのかどうか分からなくなる（手動は上限到達後も実行できる）。
    ///
    /// エラーが上限到達より上なのは、エラーが「今すぐ直せる異常」で、上限到達は設定どおりの
    /// 想定内の状態だから。上限到達はステータスバーの進捗バーと `⚠自動停止(上限)` でも常に見えている。
    /// </summary>
    internal static TrayIconState Resolve(bool proofreading, bool apiError, bool limitReached)
    {
        if (proofreading) return TrayIconState.Proofreading;
        if (apiError) return TrayIconState.ApiError;
        if (limitReached) return TrayIconState.LimitReached;
        return TrayIconState.Normal;
    }

    /// <summary>
    /// 状態ごとのアイコンリソース（<c>Assets/</c> 配下、csproj の <c>Resource</c> に登録済み）。
    /// 通常以外は <c>tools/build-tray-icons.py</c> が <c>app.ico</c> から生成する派生物で、
    /// 小サイズは DIB で格納してある（NotifyIcon は PNG 圧縮エントリを展開できない）。
    /// </summary>
    internal static string ResourcePath(TrayIconState state) => state switch
    {
        TrayIconState.Proofreading => "Assets/app-proofreading.ico",
        TrayIconState.ApiError => "Assets/app-error.ico",
        TrayIconState.LimitReached => "Assets/app-limit.ico",
        _ => "Assets/app.ico",
    };

    /// <summary>
    /// ツールチップの末尾へ足す状態表示。16px のアイコンだけでは意味を確定できないので、
    /// 同じ情報を文字でも読めるようにする。通常状態では何も足さない（<c>null</c>）。
    /// </summary>
    internal static string? TooltipSuffix(TrayIconState state) => state switch
    {
        TrayIconState.Proofreading => "校正中",
        TrayIconState.ApiError => "APIエラー",
        TrayIconState.LimitReached => "月間上限に到達",
        _ => null,
    };
}
