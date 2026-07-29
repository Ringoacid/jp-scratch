using System.ComponentModel;
using System.Runtime.CompilerServices;
using ICSharpCode.AvalonEdit.Document;
using JpScratch.Proofreading;

namespace JpScratch.Models;

/// <summary>
/// タブ 1 枚。メタ情報は SQLite の tabs テーブル、本文は tabs\{id}.txt に対応する（要件 4）。
/// </summary>
public sealed class ScratchTab : INotifyPropertyChanged
{
    private string _title = "";
    private bool _isDirty;
    private bool _isActive;
    private bool _isEditing;

    public required string Id { get; init; }

    /// <summary>表示名。<see cref="IsAutoTitle"/> が true なら本文 1 行目から自動生成される。</summary>
    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool IsAutoTitle { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>再起動時にキャレット位置まで復元する（要件 3.2.1）。</summary>
    public int CaretOffset { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>null でなければゴミ箱にある。</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 本文。タブごとに TextDocument を持たせることで、Undo 履歴もタブ単位で保たれる。
    /// エディタは 1 つだけ生成し、タブ切替ではこの Document を差し替える（メモリ 80MB 目標のため）。
    /// </summary>
    public TextDocument Document { get; } = new();

    private ProofreadingSession? _proofreading;

    /// <summary>このタブの校正提案。初回の校正結果を受け取るまで生成しない。</summary>
    internal ProofreadingSession Proofreading =>
        _proofreading ??= new ProofreadingSession(Document);

    /// <summary>最後にディスクへ書いた内容と一致しているか。自動保存の空振りを避けるために持つ。</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set => Set(ref _isDirty, value);
    }

    // ---- 以下はタブストリップの表示状態。永続化しない ----

    /// <summary>今表示されているタブか。タブ見出しの塗り分けに使う。</summary>
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    /// <summary>タブ名をその場で編集中か（見出しのダブルクリック）。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => Set(ref _isEditing, value);
    }

    public static ScratchTab CreateNew(int sortOrder) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = "無題",
        IsAutoTitle = true,
        SortOrder = sortOrder,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
