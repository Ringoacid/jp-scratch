using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JpScratch.Infrastructure;

/// <summary>
/// 二重起動の防止と、先行インスタンスの呼び戻し。
/// 常駐アプリなので、2 つ目が起動したら「既に居るほうを前に出す」のが正しい振る舞いになる。
///
/// 呼び戻しには名前付きイベントを使う。ウィンドウメッセージのブロードキャストは使えない:
/// ShowInTaskbar=false のせいで WPF が隠しオーナーウィンドウを作るため、メインウィンドウは
/// 「所有されたウィンドウ」になり、PostMessage(HWND_BROADCAST) の配送対象から外れる。
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string BaseMutexName = @"Local\JpScratch.SingleInstance";
    private const string BaseActivateEventName = @"Local\JpScratch.Activate";

    /// <summary>
    /// 隔離実行時（<see cref="AppPaths.IsIsolated"/>）だけ Mutex/イベント名にサフィックスを付け、
    /// 実データの常駐インスタンスと取り合わないようにする。
    /// サフィックスは「実際に採用された <see cref="AppPaths.Root"/>」から導出する。環境変数の生値では
    /// なく Root を見るのは、AppPaths が不正値・作成失敗で既定へ落とした場合に「実データを書きながら
    /// 隔離用の名前」という食い違いを作らないため（作成失敗は AppPaths が IsolationFailure として
    /// 明示的に失敗させるので、実際には隔離名は使われない）。未設定・既定へ落ちた場合は従来どおり
    /// 固定名のまま（1バイトも挙動を変えない）。
    /// </summary>
    private static readonly (string Mutex, string ActivateEvent) Names = ResolveNames(
        AppPaths.IsIsolated ? AppPaths.Root : null);

    private static string MutexName => Names.Mutex;
    private static string ActivateEventName => Names.ActivateEvent;

    /// <summary>
    /// Mutex/イベント名を決定する純粋関数。<paramref name="isolatedRoot"/> が null（＝隔離でない）
    /// なら従来の固定名をそのまま返す。値があれば、そのディレクトリパス（実際に採用された
    /// データディレクトリ）から決定的に導出したサフィックスを固定名へ付与する
    /// （同じ値なら常に同じ名前、異なる値なら異なる名前）。
    /// </summary>
    internal static (string MutexName, string ActivateEventName) ResolveNames(
        string? isolatedRoot)
    {
        if (string.IsNullOrWhiteSpace(isolatedRoot))
            return (BaseMutexName, BaseActivateEventName);

        string suffix = ComputeSuffix(isolatedRoot);
        return ($"{BaseMutexName}.{suffix}", $"{BaseActivateEventName}.{suffix}");
    }

    /// <summary>
    /// ディレクトリパスから、カーネルオブジェクト名に使える16進文字列のサフィックスを決定的に導出する。
    /// Windows のパスは大文字小文字を区別しないため正規化してからハッシュ化し、
    /// 同じディレクトリを指す表記ゆれ（末尾区切り文字・相対/絶対）をできる範囲で吸収する。
    /// </summary>
    private static string ComputeSuffix(string isolatedRoot)
    {
        string trimmed = isolatedRoot.Trim();
        string normalized;
        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            // パスとして解決できない値でも、名前導出自体は失敗させない（決定的でありさえすればよい）。
            normalized = trimmed;
        }

        normalized = normalized
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        // 16進16桁（64ビット）で衝突確率は無視できる水準にしつつ、名前長を短く保つ。
        return Convert.ToHexString(hash)[..16];
    }

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registration;

    /// <summary>
    /// 自分が最初のインスタンスなら true。
    /// false のときは <see cref="SignalExistingInstance"/> を呼んでから終了すること。
    /// </summary>
    public bool TryAcquire()
    {
        // Local\ で十分。ユーザーセッションをまたいで 1 つに絞る必要はない。
        // Windows では new Mutex(initiallyOwned: true, ...) が AbandonedMutexException を
        // 投げることはない（それは WaitOne 側の例外）。前回インスタンスがハード終了して
        // ハンドルごと消えた場合は createdNew=true で最初のインスタンスとして続行できる。
        // よってここに AbandonedMutexException の catch は置かない: Mutex.OpenExisting は
        // 所有権を取らないのに createdNew=true を返すため、2つ目のインスタンスが同じデータを
        // 並走させてしまう。
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
    }

    /// <summary>
    /// 先行インスタンスとして、後続からの呼び出しを待ち受ける。
    /// <paramref name="onActivate"/> はスレッドプールから呼ばれるので、UI 操作は呼び出し側で marshal すること。
    ///
    /// コールバック全体を try/catch で囲うのは必須。ここは ThreadPool のスレッドなので、
    /// 例外を漏らすと DispatcherUnhandledException には拾われず AppDomain.UnhandledException を
    /// 経てプロセスが強制終了する（＝ショートカット再クリックのたびに常駐が落ちうる）。
    /// 呼び戻しは「ウィンドウを前に出す」だけの補助動作であり、失敗しても落とす理由がない。
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) =>
            {
                try
                {
                    onActivate();
                }
                catch (Exception)
                {
                    // 呼び戻しに失敗しても常駐は続ける。ユーザーはホットキーやトレイから開ける。
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>先行インスタンスにウィンドウを出させる。</summary>
    public static void SignalExistingInstance()
    {
        if (!EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle)) return;

        using (handle)
        {
            handle.Set();
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;

        _activateEvent?.Dispose();
        _activateEvent = null;

        if (_mutex is null) return;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 所有していなければ解放不要
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
