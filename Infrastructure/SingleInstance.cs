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
    private const string MutexName = @"Local\JpScratch.SingleInstance";
    private const string ActivateEventName = @"Local\JpScratch.Activate";

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
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => onActivate(),
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
