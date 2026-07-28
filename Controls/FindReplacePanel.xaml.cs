using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using JpScratch.Editor;

namespace JpScratch.Controls;

/// <summary>
/// 検索・置換パネル（要件 3.2.3）。Notepad++ 相当。
/// AvalonEdit の SearchPanel は検索専用で置換ができないため、自前で持つ。
/// </summary>
public partial class FindReplacePanel : UserControl
{
    private readonly SearchMatchRenderer _renderer = new();

    private TextEditor? _editor;
    private int _currentIndex = -1;

    /// <summary>本文が変わったときの再計算を抑えるためのフラグ。置換中は自分で更新する。</summary>
    private bool _suppressRecalculation;

    public FindReplacePanel()
    {
        InitializeComponent();
    }

    /// <summary>全タブ横断検索を開きたい（要件 3.2.3）。</summary>
    public event Action<string>? CrossTabSearchRequested;

    /// <summary>パネルを閉じたので、フォーカスをエディタに返してほしい。</summary>
    public event Action? Closed;

    public void Attach(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        editor.TextChanged += (_, _) =>
        {
            if (!_suppressRecalculation && IsVisible) UpdateMatches(resetIndex: false);
        };
    }

    /// <summary>テーマ切替時にハイライト色を差し替える。</summary>
    public void ApplyTheme(Brush match, Brush current)
    {
        _renderer.MatchBrush = match;
        _renderer.CurrentMatchBrush = current;
        Redraw();
    }

    public void Open(bool focusReplace)
    {
        // 選択中の文字列があれば、それを検索語の初期値にする
        if (_editor is { SelectionLength: > 0 } editor)
        {
            var selection = editor.SelectedText;
            if (!selection.Contains('\n')) SearchBox.Text = selection;
        }

        Visibility = Visibility.Visible;
        UpdateMatches(resetIndex: true);

        var target = focusReplace ? ReplaceBox : SearchBox;
        target.Focus();
        target.SelectAll();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        _renderer.Clear();
        _currentIndex = -1;
        Redraw();
        Closed?.Invoke();
    }

    /// <summary>タブが切り替わったときに呼ぶ。ヒット位置は文書ごとに別物なので作り直す。</summary>
    public void OnDocumentSwapped()
    {
        if (IsVisible) UpdateMatches(resetIndex: true);
    }

    // ================= 検索 =================

    private Regex? BuildRegex()
    {
        var term = SearchBox.Text;
        if (string.IsNullOrEmpty(term)) return null;

        var pattern = RegexToggle.IsChecked == true ? term : Regex.Escape(term);

        // 単語単位は英数字向け。日本語には \b の境界がほぼ現れないため、実質そちらでは無効になる。
        if (WholeWordToggle.IsChecked == true) pattern = $@"\b(?:{pattern})\b";

        var options = RegexOptions.Multiline;
        if (MatchCaseToggle.IsChecked != true) options |= RegexOptions.IgnoreCase;

        try
        {
            return new Regex(pattern, options, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            // 入力途中の不正な正規表現。エラーダイアログは出さず、ヒット 0 件として扱う。
            return null;
        }
    }

    private void UpdateMatches(bool resetIndex)
    {
        if (_editor?.Document is null) return;

        var regex = BuildRegex();
        if (regex is null)
        {
            _renderer.Clear();
            _currentIndex = -1;
            MatchCountText.Text = SearchBox.Text.Length == 0 ? "" : "不正な式";
            SearchBox.Foreground = SearchBox.Text.Length == 0 || RegexToggle.IsChecked != true
                ? (Brush)FindResource("TextBrush")
                : (Brush)FindResource("DangerBrush");
            Redraw();
            return;
        }

        SearchBox.Foreground = (Brush)FindResource("TextBrush");

        var text = _editor.Document.Text;
        var matches = new List<(int Offset, int Length)>();

        try
        {
            foreach (Match m in regex.Matches(text))
            {
                // 空マッチ（例: `a*`）は無限に見つかるだけで役に立たない
                if (m.Length == 0) continue;
                matches.Add((m.Index, m.Length));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            MatchCountText.Text = "時間切れ";
            return;
        }

        _renderer.SetMatches(matches);

        if (resetIndex || _currentIndex >= matches.Count)
            _currentIndex = matches.Count > 0 ? IndexNearCaret(matches) : -1;

        _renderer.CurrentIndex = _currentIndex;
        UpdateCountLabel();
        Redraw();
    }

    private int IndexNearCaret(List<(int Offset, int Length)> matches)
    {
        var caret = _editor?.CaretOffset ?? 0;
        for (var i = 0; i < matches.Count; i++)
            if (matches[i].Offset >= caret) return i;
        return 0;
    }

    private void UpdateCountLabel()
    {
        var count = _renderer.Matches.Count;
        MatchCountText.Text = count == 0
            ? (SearchBox.Text.Length == 0 ? "" : "0 件")
            : $"{_currentIndex + 1} / {count} 件";
    }

    public void FindNext(bool forward)
    {
        if (_editor is null) return;

        if (_renderer.Matches.Count == 0)
        {
            UpdateMatches(resetIndex: true);
            if (_renderer.Matches.Count == 0) return;
        }
        else
        {
            var count = _renderer.Matches.Count;
            _currentIndex = ((_currentIndex + (forward ? 1 : -1)) % count + count) % count;
        }

        _renderer.CurrentIndex = _currentIndex;
        SelectCurrent();
        UpdateCountLabel();
        Redraw();
    }

    private void SelectCurrent()
    {
        if (_editor is null || _currentIndex < 0 || _currentIndex >= _renderer.Matches.Count) return;

        var (offset, length) = _renderer.Matches[_currentIndex];
        _editor.Select(offset, length);
        _editor.ScrollTo(_editor.Document.GetLineByOffset(offset).LineNumber, 0);
    }

    // ================= 置換 =================

    private void ReplaceCurrent()
    {
        if (_editor is null || _currentIndex < 0 || _currentIndex >= _renderer.Matches.Count) return;

        var regex = BuildRegex();
        if (regex is null) return;

        var (offset, length) = _renderer.Matches[_currentIndex];
        var original = _editor.Document.GetText(offset, length);

        var replacement = RegexToggle.IsChecked == true
            ? regex.Match(original).Result(ReplaceBox.Text)   // $1 などの後方参照を効かせる
            : ReplaceBox.Text;

        _suppressRecalculation = true;
        try
        {
            _editor.Document.Replace(offset, length, replacement);
        }
        finally
        {
            _suppressRecalculation = false;
        }

        _editor.CaretOffset = offset + replacement.Length;
        UpdateMatches(resetIndex: false);
        FindNext(forward: true);
    }

    private void ReplaceAll()
    {
        if (_editor?.Document is null) return;

        var regex = BuildRegex();
        if (regex is null) return;

        var rangeStart = 0;
        var rangeEnd = _editor.Document.TextLength;

        if (InSelectionCheck.IsChecked == true)
        {
            if (_editor.SelectionLength == 0)
            {
                MatchCountText.Text = "範囲未選択";
                return;
            }
            rangeStart = _editor.SelectionStart;
            rangeEnd = rangeStart + _editor.SelectionLength;
        }

        var targets = _renderer.Matches
            .Where(m => m.Offset >= rangeStart && m.Offset + m.Length <= rangeEnd)
            .OrderByDescending(m => m.Offset)
            .ToList();

        if (targets.Count == 0)
        {
            MatchCountText.Text = "0 件";
            return;
        }

        _suppressRecalculation = true;
        // まとめて 1 回の Undo で戻せるようにする
        _editor.Document.BeginUpdate();
        try
        {
            // 後ろから置換すれば、前方のオフセットがずれない
            foreach (var (offset, length) in targets)
            {
                var original = _editor.Document.GetText(offset, length);
                var replacement = RegexToggle.IsChecked == true
                    ? regex.Match(original).Result(ReplaceBox.Text)
                    : ReplaceBox.Text;
                _editor.Document.Replace(offset, length, replacement);
            }
        }
        finally
        {
            _editor.Document.EndUpdate();
            _suppressRecalculation = false;
        }

        UpdateMatches(resetIndex: true);
        MatchCountText.Text = $"{targets.Count} 件置換";
    }

    private void Redraw() => _editor?.TextArea.TextView.InvalidateLayer(_renderer.Layer);

    // ================= イベント =================

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateMatches(resetIndex: true);

    private void Option_Changed(object sender, RoutedEventArgs e) => UpdateMatches(resetIndex: true);

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        FindNext(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
        e.Handled = true;
    }

    private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        ReplaceCurrent();
        e.Handled = true;
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) => FindNext(forward: true);
    private void PrevButton_Click(object sender, RoutedEventArgs e) => FindNext(forward: false);
    private void ReplaceButton_Click(object sender, RoutedEventArgs e) => ReplaceCurrent();
    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => ReplaceAll();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void CrossTabButton_Click(object sender, RoutedEventArgs e)
        => CrossTabSearchRequested?.Invoke(SearchBox.Text);
}
