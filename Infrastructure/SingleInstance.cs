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
    /// <see cref="AppPaths.DataDirEnvironmentVariable"/> と同じ環境変数。設定されている隔離実行時だけ
    /// Mutex/イベント名にサフィックスを付け、実データの常駐インスタンスと取り合わないようにする。
    /// 未設定なら従来どおり固定名のまま（1バイトも挙動を変えない）。
    /// </summary>
    private static readonly (string Mutex, string ActivateEvent) Names = ResolveNames(
        Environment.GetEnvironmentVariable(AppPaths.DataDirEnvironmentVariable));

    private static string MutexName => Names.Mutex;
    private static string ActivateEventName => Names.ActivateEvent;

    /// <summary>
    /// Mutex/イベント名を決定する純粋関数。<paramref name="dataDirEnvironmentValue"/> が
    /// 空・空白のみなら従来の固定名をそのまま返す。値があれば、そのディレクトリパスから
    /// 決定的に導出したサフィックスを固定名へ付与する（同じ値なら常に同じ名前、異なる値なら異なる名前）。
    /// </summary>
    internal static (string MutexName, string ActivateEventName) ResolveNames(
        string? dataDirEnvironmentValue)
    {
        if (string.IsNullOrWhiteSpace(dataDirEnvironmentValue))
            return (BaseMutexName, BaseActivateEventName);

        string suffix = ComputeSuffix(dataDirEnvironmentValue);
        return ($"{BaseMutexName}.{suffix}", $"{BaseActivateEventName}.{suffix}");
    }

    /// <summary>
    /// ディレクトリパスから、カーネルオブジェクト名に使える16進文字列のサフィックスを決定的に導出する。
    /// Windows のパスは大文字小文字を区別しないため正規化してからハッシュ化し、
    /// 同じディレクトリを指す表記ゆれ（末尾区切り文字・相対/絶対）をできる範囲で吸収する。
    /// </summary>
    private static string ComputeSuffix(string dataDirEnvironmentValue)
    {
        string trimmed = dataDirEnvironmentValue.Trim();
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
