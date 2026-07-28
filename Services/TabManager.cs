using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// 開いているタブの一覧・アクティブタブ・自動保存を束ねる（要件 3.2.1 / 3.2.4）。
/// 「保存」という概念をユーザーに見せないので、ここが確実に動くことがデータの生命線になる。
/// </summary>
internal sealed class TabManager : INotifyPropertyChanged
{
    private readonly TabRepository _repository;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _autoSaveTimer;

    private ScratchTab? _active;

    public ObservableCollection<ScratchTab> Tabs { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>アクティブタブが差し替わったとき。エディタは Document を張り替える。</summary>
    public event Action<ScratchTab?>? ActiveChanged;

    /// <summary>いずれかのタブの本文が変わったとき。文字数表示や校正トリガーの起点になる。</summary>
    public event Action<ScratchTab>? TabTextChanged;

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
            SaveDirty();
        };
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
        _repository.PurgeExpiredTrash(_settings.Current.TrashRetentionDays);

        foreach (var tab in _repository.LoadActive())
        {
            _repository.LoadBody(tab);
            Attach(tab);
            Tabs.Add(tab);
        }

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
        Tabs.Remove(tab);
        _repository.MoveToTrash(tab);
        _repository.SaveOrder(Tabs);

        if (Tabs.Count == 0)
        {
            AddNew();
            return;
        }

        if (ReferenceEquals(Active, tab))
            Activate(Tabs[Math.Clamp(index, 0, Tabs.Count - 1)]);
    }

    /// <summary>直近に閉じたタブを戻す（Ctrl+Shift+T、要件 3.2.1）。</summary>
    public ScratchTab? RestoreLastClosed()
    {
        var trashed = _repository.LoadTrash().FirstOrDefault();
        if (trashed is null) return null;

        _repository.RestoreFromTrash(trashed, Tabs.Count);
        _repository.LoadBody(trashed);
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

    /// <summary>ウィンドウ非表示時・終了時に呼ぶ。未保存を全部落とす。</summary>
    public void SaveDirty()
    {
        _autoSaveTimer.Stop();

        foreach (var tab in Tabs)
        {
            if (!tab.IsDirty) continue;

            tab.UpdatedAt = DateTime.Now;
            _repository.SaveBody(tab);
            _repository.Upsert(tab);
        }
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

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();

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
