namespace JpScratch.Services;

/// <summary>
/// メインウィンドウの自動非表示（要件 3.1.3）の抑止を参照カウントで管理する。
/// 設定ダイアログ・全タブ検索・課金履歴など、複数の子ウィンドウが同時に抑止を要求している
/// 状態で片方だけが解除しても、残りが要求している間は抑止され続けなければならない。
/// bool を共有する実装では、後から閉じた側が無条件に解除してしまい、まだ開いている側が
/// あっても自動非表示が働いてしまう不具合があった（全タブ検索と課金履歴の組み合わせで発生）。
/// </summary>
internal sealed class HideSuppressionCounter
{
    private int _count;

    internal bool IsSuppressed => _count > 0;

    internal void Suppress() => _count++;

    /// <summary>対応する <see cref="Suppress"/> より多く呼ばれても負にはしない。</summary>
    internal void Release()
    {
        if (_count > 0) _count--;
    }
}
