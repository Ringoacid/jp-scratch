using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using JpScratch.Models;
using Microsoft.Data.Sqlite;

namespace JpScratch.Services;

/// <summary>
/// 保存できなかったタブの通知内容。
/// <paramref name="WillRetry"/> は自動再試行を予約したか（false なら上限に達している）。
/// <paramref name="IsFirstFailure"/> は連続失敗の 1 回目か（通知の重複を抑えるのに使う）。
/// </summary>
internal sealed record TabSaveFailure(
    IReadOnlyList<string> Titles,
    bool WillRetry,
    bool IsFirstFailure);

/// <summary>
/// 開いているタブの一覧・アクティブタブ・自動保存を束ねる（要件 3.2.1 / 3.2.4）。
/// 「保存」という概念をユーザーに見せないので、ここが確実に動くことがデータの生命線になる。
/// </summary>
internal sealed class TabManager : INotifyPropertyChanged
{
    /// <summary>保存失敗後の自動再試行の上限回数。</summary>
    private const int MaxSaveRetries = 6;

    /// <summary>1 回目の再試行までの待ち時間。以降は倍々で <see cref="MaxSaveRetryDelay"/> まで伸ばす。</summary>
    private static readonly TimeSpan FirstSaveRetryDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan MaxSaveRetryDelay = TimeSpan.FromSeconds(60);

    private readonly TabRepository _repository;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _autoSaveTimer;

    /// <summary>
    /// 保存失敗後の再試行タイマー。<see cref="_autoSaveTimer"/> と共用しないのは、
    /// 設定変更（<see cref="ReloadAutoSaveInterval"/>）がバックオフ中の間隔を書き潰すため。
    /// </summary>
    private readonly DispatcherTimer _saveRetryTimer;

    /// <summary>直近の連続失敗で再試行した回数。保存成功と本文編集で 0 に戻す。</summary>
    private int _saveRetryCount;

    /// <summary>モーダル表示中など、タイマー発火による保存を止めているか。</summary>
    private bool _autoSaveSuspended;

    private ScratchTab? _active;

    public ObservableCollection<ScratchTab> Tabs { get; } = [];

    /// <summary>起動時に本文を読み込めなかったタブのタイトル（読み取り失敗を空で上書きしないためスキップした分）。</summary>
    public List<string> LoadFailures { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>アクティブタブが差し替わったとき。エディタは Document を張り替える。</summary>
    public event Action<ScratchTab?>? ActiveChanged;

    /// <summary>いずれかのタブの本文が変わったとき。文字数表示や校正トリガーの起点になる。</summary>
    public event Action<ScratchTab>? TabTextChanged;

    /// <summary>タブが閉じられた（ゴミ箱へ移された）とき。校正スケジュール等の掃除に使う。</summary>
    public event Action<ScratchTab>? TabRemoved;

    /// <summary>
    /// 保存できなかったタブが出たとき（引数はそのタイトル）。「保存」を見せない設計なので、
    /// 失敗を黙っていると編集が静かに消える。呼ばれた側は必ずユーザーへ伝えること。
    /// </summary>
    public event Action<TabSaveFailure>? SaveFailed;

    public TabManager(TabRepository repository, SettingsService settings)
    {
        _repository = repository;
        _settings = settings;

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(settings.Current.AutoSaveDebounceMs),
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            SaveFromTimer();
        };

        _saveRetryTimer = new DispatcherTimer { Interval = FirstSaveRetryDelay };
        _saveRetryTimer.Tick += (_, _) =>
        {
            _saveRetryTimer.Stop();
            SaveFromTimer();
        };
    }

    private void SaveFromTimer()
    {
        try
        {
            // 失敗の通知は SaveDirty 自身が行う（呼び出し元ごとに書くと漏れる）。
            SaveDirty();
        }
        catch (Exception)
        {
            // Tick で例外を漏らすと DispatcherUnhandledException に落ち、共通ハンドラが
            // 「編集中の内容は保存されています」という事実と逆のダイアログを出す。
            // タブ単位の I/O 失敗は SaveDirty 内で隔離済みなので、ここへ来るのは想定外の異常。
            // 未保存のまま残っているタブ＝保存できなかったタブとして正直に伝える。
            string[] dirty = Tabs.Where(tab => tab.IsDirty).Select(tab => tab.Title).ToArray();
            if (dirty.Length > 0)
                SaveFailed?.Invoke(new TabSaveFailure(dirty, WillRetry: false, IsFirstFailure: true));
        }
    }

    public ScratchTab? Active
    {
        get => _active;
        private set
        {
            if (ReferenceEquals(_active, value)) return;

            if (_active is not null) _active.IsActive = false;
            _active = value;
            if (_active is not null) _active.IsActive = true;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
            ActiveChanged?.Invoke(value);
        }
    }

    /// <summary>起動時の復元（要件 3.2.4）。タブが 1 枚もなければ空のタブを 1 枚作る。</summary>
    public void Initialize()
    {
        // 設定を読めなかった起動では、設定値に依存する破壊的処理を走らせない。
        // TrashRetentionDays の既定は 30 日。実際の設定が 365 日でも、一時的な settings.json の
        // 読み取り失敗（共有違反等）だけでゴミ箱の本文が消える＝一時的な障害が不可逆な削除に
        // 化ける。設定を読めるようになった起動で実行されるので、見送っても取りこぼしはない
        // （文字コード不正の場合は再起動しても読めるようにならないため、ユーザーが直すまで
        // 掃除は止まったままになる。その旨は起動時の警告で伝えている）。
        if (!_settings.IsReadFailed)
            _repository.PurgeExpiredTrash(_settings.Current.TrashRetentionDays);

        var failures = new List<string>();
        foreach (var tab in _repository.LoadActive())
        {
            try
            {
                _repository.LoadBody(tab);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                // 本文ファイルがあるのに読めない（一時的なロック等）。空タブとして開くと
                // その後の保存で元の本文を上書きしてしまうため、このタブは開かず、
                // ファイルをそのまま残す。起動後にユーザーへ警告する（LoadFailures）。
                //
                // InvalidDataException は AppPaths.TabDataFile が投げる「タブIDの形式が不正」。
                // IOException 非派生なので、ここに足さないと壊れた 1 行で MainWindow 生成前に
                // 脱出し、ShutdownMode=OnExplicitShutdown のため操作不能なプロセスが残る
                // （CLAUDE.md「壊れた1行で全体を落とさない」）。
                failures.Add(tab.Title);
                continue;
            }
            Attach(tab);
            Tabs.Add(tab);
        }
        LoadFailures.Clear();
        LoadFailures.AddRange(failures);

        if (Tabs.Count == 0)
        {
            var tab = ScratchTab.CreateNew(0);
            _repository.Upsert(tab);
            Attach(tab);
            Tabs.Add(tab);
        }

        var activeId = _repository.LoadActiveId();
        Activate(Tabs.FirstOrDefault(t => t.Id == activeId) ?? Tabs[0]);
    }

    public void Activate(ScratchTab tab)
    {
        if (!Tabs.Contains(tab)) return;

        // タブ切替は保存タイミングのひとつ（要件 3.2.4）
        SaveDirty();

        // 離れるタブのキャレット位置を確定させる。本文が未編集でも位置は覚えておきたい。
        if (_active is not null && !ReferenceEquals(_active, tab)) _repository.Upsert(_active);

        Active = tab;
        _repository.SaveActive(tab.Id);
    }

    public void ActivateByOffset(int offset)
    {
        if (Tabs.Count == 0 || Active is null) return;
        var index = Tabs.IndexOf(Active);
        var next = ((index + offset) % Tabs.Count + Tabs.Count) % Tabs.Count;
        Activate(Tabs[next]);
    }

    public ScratchTab AddNew()
    {
        var tab = ScratchTab.CreateNew(Tabs.Count);
        _repository.Upsert(tab);
        Attach(tab);
        Tabs.Add(tab);
        Activate(tab);
        return tab;
    }

    /// <summary>タブを閉じる = ゴミ箱へ。最後の 1 枚を閉じたら空のタブを補充する。</summary>
    public void Close(ScratchTab tab)
    {
        if (!Tabs.Contains(tab)) return;

        if (tab.IsDirty) _repository.SaveBody(tab);

        var index = Tabs.IndexOf(tab);
        // 本文ファイルのゴミ箱移動を先に済ませる。失敗（MoveToTrash が例外）したらタブを
        // 閉じずに呼び出し元へ伝える。Tabs.Remove を先にやると、失敗時だけ「UIから消えたのに
        // DBには残る」状態になる。
        _repository.MoveToTrash(tab);
        Tabs.Remove(tab);
        _repository.SaveOrder(Tabs);
        TabRemoved?.Invoke(tab);

        if (Tabs.Count == 0)
        {
            AddNew();
            return;
        }

        if (ReferenceEquals(Active, tab))
            Activate(Tabs[Math.Clamp(index, 0, Tabs.Count - 1)]);
    }

    /// <summary>
    /// ゴミ箱のタブを完全に削除する（ゴミ箱一覧ウィンドウ用）。本文ファイルと DB 行の両方を消す。
    /// 失敗は例外で伝える（呼び出し元がユーザーへ伝える）。
    /// </summary>
    public void DeletePermanently(ScratchTab trashed) => _repository.DeletePermanently(trashed);

    /// <summary>ゴミ箱を空にする（ゴミ箱一覧ウィンドウ用）。戻り値は削除できた件数。</summary>
    public int EmptyTrash() => _repository.DeleteAllTrash();

    /// <summary>
    /// 直近に閉じたタブを戻す（Ctrl+Shift+T、要件 3.2.1）。
    ///
    /// ID の形式が壊れている行は**飛ばして次の行を試す**。ここで諦めると、壊れた 1 行が
    /// 最新のまま残り続けるため、以後 Ctrl+Shift+T が毎回同じ行で失敗し、その後ろにある
    /// 正常なゴミ箱タブを二度と復元できなくなる（CLAUDE.md「壊れた1行で全体を落とさない」）。
    /// 飛ばすのは <see cref="InvalidDataException"/>（＝直しようがない）だけに限る。
    /// 一時的な I/O 失敗は投げ直す。ユーザーが戻したいのは「その」タブなので、黙って別の
    /// タブを戻すほうが困る。
    /// </summary>
    public ScratchTab? RestoreLastClosed()
    {
        foreach (var candidate in _repository.LoadTrash())
        {
            if (Restore(candidate) is { } restored) return restored;
        }
        return null;
    }

    /// <summary>
    /// ゴミ箱のタブを 1 件指定して復元する（ゴミ箱一覧ウィンドウ用）。復元したタブを返す。
    /// null は「この行は構造的に壊れている（タブIDの形式が不正）ので復元できない」。
    /// 一時的な I/O 失敗はゴミ箱へロールバック済みで例外を投げる（呼び出し元がユーザーへ伝える）。
    /// </summary>
    public ScratchTab? Restore(ScratchTab trashed) => TryRestore(trashed);

    /// <summary>戻り値 null は「この行は構造的に壊れているので飛ばす」。</summary>
    private ScratchTab? TryRestore(ScratchTab trashed)
    {
        try
        {
            _repository.RestoreFromTrash(trashed, Tabs.Count);
        }
        catch (InvalidDataException)
        {
            // AppPaths.TrashFile が投げる「タブIDの形式が不正」。本文ファイルの位置が決まらない
            // ＝復元しようがない。DB もファイルもまだ触っていないので、そのまま次の行へ。
            return null;
        }

        try
        {
            _repository.LoadBody(trashed);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // RestoreFromTrash は既に DB の deleted_at を NULL にしてしまっている。ここで何も
            // せず抜けると「DB 上は復元済み・UI には出ない」状態が再起動まで続くため、ゴミ箱へ
            // ロールバックしてから呼び出し元（ユーザー）へ例外を伝える。
            try
            {
                _repository.MoveToTrash(trashed);
            }
            catch (Exception)
            {
                // ロールバックも失敗した。ファイルは tabs\ に残っているので、次回起動時の
                // 起動時警告（LoadFailures）でユーザーに伝わる。ここは元の例外を優先する。
            }
            throw;
        }
        Attach(trashed);
        Tabs.Add(trashed);
        _repository.SaveOrder(Tabs);
        Activate(trashed);
        return trashed;
    }

    /// <summary>ドラッグでの並べ替え（要件 3.2.1）。</summary>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Tabs.Count) return;
        toIndex = Math.Clamp(toIndex, 0, Tabs.Count - 1);
        if (fromIndex == toIndex) return;

        Tabs.Move(fromIndex, toIndex);
        _repository.SaveOrder(Tabs);
    }

    public void Rename(ScratchTab tab, string title)
    {
        title = title.Trim();
        if (title.Length == 0)
        {
            // 空にされたら自動命名に戻す
            tab.IsAutoTitle = true;
            UpdateAutoTitle(tab);
        }
        else
        {
            tab.IsAutoTitle = false;
            tab.Title = title;
        }

        tab.UpdatedAt = DateTime.Now;
        _repository.Upsert(tab);
    }

    /// <summary>
    /// ウィンドウ非表示時・終了時に呼ぶ。未保存を全部落とす。
    /// 戻り値は保存できなかったタブのタイトル（空なら全部保存できた）。
    ///
    /// 失敗は**タブ単位で隔離する**。AtomicFile → File.Replace は共有違反（ウイルス対策スキャン・
    /// Windows Search・OneDrive 等の一時ロック）で IOException を、SQLite への Upsert は
    /// SqliteException（ロック・ディスク不足）を投げうるが、それをここで漏らすと
    /// ループが途中で抜け、後続タブの未保存本文が無警告で失われる。失敗したタブは IsDirty のまま
    /// 残して次のタブへ進み、次回の自動保存・終了時保存で再試行できるようにする。
    /// 1 タブの保存は「本文ファイル＋メタ情報」で 1 単位。片方だけ書けた状態を保存済みにしない。
    ///
    /// 失敗したときは <see cref="SaveFailed"/> を**ここで**発火させる。呼び出し元は
    /// 自動保存タイマー・タブ切替・ウィンドウ非表示・終了処理と多く、戻り値の確認を各所へ
    /// 書き分けると必ずどこかが黙る（例: <see cref="Activate"/> は戻り値を見ない）。
    ///
    /// 失敗したときは上限付きのバックオフで**自動再試行を予約する**。予約しないと、
    /// 通知の「自動で再試行します」が嘘になり、次の編集・タブ切替・非表示・終了まで
    /// 再試行されない（数秒で解ける共有違反でも未保存のまま放置される）。
    /// </summary>
    public IReadOnlyList<string> SaveDirty()
    {
        _autoSaveTimer.Stop();
        _saveRetryTimer.Stop();

        List<string>? failures = null;
        foreach (var tab in Tabs)
        {
            if (!tab.IsDirty) continue;

            try
            {
                tab.UpdatedAt = DateTime.Now;
                _repository.SaveBody(tab);
                _repository.Upsert(tab);
                // 本文とメタ情報の両方を書けて初めて「保存済み」。本文だけ書けた時点で落とすと、
                // Upsert が失敗したタブが未保存扱いから外れ、通知にも再試行にも乗らなくなる。
                tab.IsDirty = false;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or InvalidDataException
                    or SqliteException)
            {
                // SqliteException（DB ロック・ディスク不足）を漏らすと、このループが途中で
                // 抜けて後続タブの未保存本文が無警告で失われる。ファイル I/O と同じくタブ単位で隔離する。
                (failures ??= []).Add(tab.Title);
            }
        }

        if (failures is null)
        {
            _saveRetryCount = 0;
            return [];
        }

        bool isFirstFailure = _saveRetryCount == 0;
        bool willRetry = ScheduleSaveRetry();

        // 停止中（<see cref="SuspendAutoSave"/>）は呼び出し元がモーダルで直接ユーザーへ問う。
        // ここでも通知すると、終了確認ダイアログを出すたびにトレイのバルーンが重なる。
        if (!_autoSaveSuspended)
            SaveFailed?.Invoke(new TabSaveFailure(failures, willRetry, isFirstFailure));
        return failures;
    }

    /// <summary>
    /// 保存失敗後の再試行を予約する。間隔は 2 秒から倍々で最大 60 秒、回数は
    /// <see cref="MaxSaveRetries"/> で打ち切る。戻り値は予約したか。
    ///
    /// 上限を設けるのは、恒久的な失敗（権限エラー・ディスク不足）で常駐したまま
    /// 無限に再試行し続けないため。打ち切っても、次の編集・タブ切替・非表示・終了で
    /// もう一度試されるので、保存の機会そのものが失われることはない。
    /// </summary>
    private bool ScheduleSaveRetry()
    {
        if (_autoSaveSuspended || _saveRetryCount >= MaxSaveRetries) return false;

        double seconds = Math.Min(
            FirstSaveRetryDelay.TotalSeconds * Math.Pow(2, _saveRetryCount),
            MaxSaveRetryDelay.TotalSeconds);
        _saveRetryCount++;
        _saveRetryTimer.Interval = TimeSpan.FromSeconds(seconds);
        _saveRetryTimer.Start();
        return true;
    }

    /// <summary>
    /// タイマー発火による保存を止める。WPF のモーダルは入れ子のメッセージループを回すため、
    /// ダイアログ表示中に自動保存・再試行が <see cref="SaveDirty"/> へ再入する
    /// （CLAUDE.md「進行中フラグ…モーダル表示も『何か』に含む」と同じ理由）。
    ///
    /// この間は <see cref="SaveFailed"/> も発火しない。呼び出し元がモーダルで直接ユーザーへ
    /// 問う場面のための停止なので、通知は呼び出し元の責任になる。
    /// </summary>
    public void SuspendAutoSave()
    {
        _autoSaveSuspended = true;
        _autoSaveTimer.Stop();
        _saveRetryTimer.Stop();
    }

    /// <summary>
    /// <see cref="SuspendAutoSave"/> を解除する。未保存が残っていれば再試行を予約し直す。
    /// 解除せずにアプリへ戻すと、未保存タブが誰にも保存されないまま残る。
    /// </summary>
    public void ResumeAutoSave()
    {
        _autoSaveSuspended = false;
        if (!Tabs.Any(tab => tab.IsDirty)) return;

        // ユーザーが自分の意思でアプリへ戻ってきた＝原因を直す機会があるので、
        // 打ち切っていた再試行の回数をここで戻す。
        _saveRetryCount = 0;
        ScheduleSaveRetry();
    }

    /// <summary>
    /// タブ名の一覧を通知用に整形する（先頭 10 件＋残件数）。読み込み失敗・保存失敗の
    /// どちらの通知でも同じ見え方にするため一箇所に置く。
    /// </summary>
    internal static string FormatTitles(IReadOnlyList<string> titles)
    {
        var names = string.Join(Environment.NewLine, titles.Take(10));
        return titles.Count > 10
            ? names + $"{Environment.NewLine}ほか {titles.Count - 10} 件"
            : names;
    }

    /// <summary>キャレット位置を控える。次回起動時にここへ戻す。</summary>
    public void SaveCaret(ScratchTab tab, int offset)
    {
        if (tab.CaretOffset == offset) return;
        tab.CaretOffset = offset;
        _repository.Upsert(tab);
    }

    public void ReloadAutoSaveInterval()
        => _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(_settings.Current.AutoSaveDebounceMs);

    /// <summary>
    /// 本文の変更を監視する。購読は Document とタブが一緒に寿命を終えるため、明示的な解除はいらない。
    /// </summary>
    private void Attach(ScratchTab tab)
        => tab.Document.TextChanged += (_, _) => OnDocumentChanged(tab);

    private void OnDocumentChanged(ScratchTab tab)
    {
        tab.IsDirty = true;
        UpdateAutoTitle(tab);

        // 新しい編集は新しい未保存データなので、打ち切っていた再試行の回数を戻す
        // （そのぶん通知も繰り返されるが、失われうる編集を黙って抱えるよりよい）。
        _saveRetryCount = 0;
        _saveRetryTimer.Stop();

        _autoSaveTimer.Stop();
        if (!_autoSaveSuspended) _autoSaveTimer.Start();

        TabTextChanged?.Invoke(tab);
    }

    /// <summary>本文 1 行目からタブ名を作る（要件 3.2.1）。</summary>
    private void UpdateAutoTitle(ScratchTab tab)
    {
        if (!tab.IsAutoTitle) return;

        tab.Title = FirstMeaningfulLine(tab.Document.Text, _settings.Current.AutoTitleMaxLength);
    }

    internal static string FirstMeaningfulLine(string text, int maxLength)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().Trim('\r');
            if (line.Length == 0) continue;

            return line.Length <= maxLength
                ? line
                : string.Concat(line.AsSpan(0, maxLength), "…");
        }
        return "無題";
    }
}
