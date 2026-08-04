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
            // FindResource は「今の辞書から一度だけ取り出す」ため、テーマ切替に追随しない
            // （ThemeService が辞書を差し替えても色が古いまま残る）。他の色と同じく
            // SetResourceReference で参照として張る。
            SetSearchBoxForeground(
                SearchBox.Text.Length == 0 || RegexToggle.IsChecked != true
                    ? "TextBrush"
                    : "DangerBrush");
            Redraw();
            return;
        }

        SetSearchBoxForeground("TextBrush");

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

    /// <summary>検索ボックスの文字色をリソース参照として張り替える（テーマ切替に追随させる）。</summary>
    private void SetSearchBoxForeground(string resourceKey)
        => SearchBox.SetResourceReference(ForegroundProperty, resourceKey);

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

        var replaceInSelection = InSelectionCheck.IsChecked == true;
        var rangeStart = 0;
        var rangeEnd = _editor.Document.TextLength;

        if (replaceInSelection)
        {
            if (_editor.SelectionLength == 0)
            {
                MatchCountText.Text = "範囲未選択";
                return;
            }

            rangeStart = _editor.SelectionStart;
            rangeEnd = rangeStart + _editor.SelectionLength;

            // 現在の検索位置が選択範囲外なら、選択範囲内の先頭の一致を対象にする。
            // 検索ハイライトの「現在位置」は選択範囲とは独立しているため、
            // ここを確認しないと「選択範囲内」を有効にしても範囲外を置換してしまう。
            var firstIndexInRange = -1;
            for (var i = 0; i < _renderer.Matches.Count; i++)
            {
                var match = _renderer.Matches[i];
                if (match.Offset < rangeStart || match.Offset + match.Length > rangeEnd) continue;

                if (firstIndexInRange < 0) firstIndexInRange = i;
                if (i == _currentIndex) break;
            }

            var current = _renderer.Matches[_currentIndex];
            if (current.Offset < rangeStart || current.Offset + current.Length > rangeEnd)
                _currentIndex = firstIndexInRange;

            if (_currentIndex < 0)
            {
                MatchCountText.Text = "0 件";
                return;
            }
        }

        var (offset, length) = _renderer.Matches[_currentIndex];

        // 後方参照（$1 など）の解決は、実際にマッチした文字列に対して行う。
        // 存在しないグループ参照・末尾の $・{$ は実行時に ArgumentException にならない
        // （リテラルとして出力される）が、Int32.MaxValue を超えるグループ番号などは
        // RegexParseException（ArgumentException の派生）になるため、例外時は置換せず
        // ユーザーへ伝える。文書はまだ変更していないので部分適用は起きない。
        string replacement;
        try
        {
            if (!TryResolveReplacement(regex, _editor.Document.Text, offset, length, out replacement))
            {
                MatchCountText.Text = "置換対象を取り直せませんでした";
                return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or RegexMatchTimeoutException)
        {
            MatchCountText.Text = "置換文字列が不正です";
            return;
        }

        _suppressRecalculation = true;
        try
        {
            _editor.Document.Replace(offset, length, replacement);
        }
        finally
        {
            _suppressRecalculation = false;
        }

        if (!replaceInSelection)
        {
            _editor.CaretOffset = offset + replacement.Length;
            UpdateMatches(resetIndex: false);
            // UpdateMatches(resetIndex:false) は _currentIndex を保持するため、置換対象を消した
            // 後のリストでは「次の一致」を指している。ここで FindNext を呼ぶとさらに +1 進んで
            // 1件飛ばすため、選択だけを更新する（件数表示・再描画は UpdateMatches 内で済んでいる）。
            SelectCurrent();
            return;
        }

        // 選択範囲を置換後の長さへ追従させて保持する。これにより、続けて「置換」を
        // 押しても選択がキャレットへ縮まず、範囲外へ処理が漏れない。
        rangeEnd += replacement.Length - length;
        UpdateMatches(resetIndex: false);

        var nextIndex = -1;
        var firstIndex = -1;
        var nextOffset = offset + replacement.Length;
        for (var i = 0; i < _renderer.Matches.Count; i++)
        {
            var match = _renderer.Matches[i];
            if (match.Offset < rangeStart || match.Offset + match.Length > rangeEnd) continue;

            if (firstIndex < 0) firstIndex = i;
            if (nextIndex < 0 && match.Offset >= nextOffset) nextIndex = i;
        }

        _currentIndex = nextIndex >= 0 ? nextIndex : firstIndex;
        _renderer.CurrentIndex = _currentIndex;
        _editor.Select(rangeStart, Math.Max(0, rangeEnd - rangeStart));
        UpdateCountLabel();
        Redraw();
    }

    /// <summary>
    /// 1 件ぶんの置換文字列を決める。正規表現モードでは <c>$1</c> などの後方参照を効かせるため
    /// <see cref="Match"/> が要るが、**マッチした部分文字列だけを取り出して再マッチしてはいけない**。
    /// <c>(?&lt;=¥)(\d+)</c> のような先読み・後読みを含むパターンは、部分文字列単体では
    /// Success=false になり、Result() が NotSupportedException を投げて「置換文字列が不正です」に
    /// なってしまう（実際にはパターンも置換文字列も正しい）。
    ///
    /// 文書全文を入力のまま <c>Match(input, startat)</c> で取り直すのが正しい。この形なら
    /// 先読み・後読みは startat より前後の文字を通常どおり参照できる。
    /// </summary>
    private bool TryResolveReplacement(
        Regex regex,
        string documentText,
        int offset,
        int length,
        out string replacement)
    {
        if (RegexToggle.IsChecked != true)
        {
            replacement = ReplaceBox.Text;
            return true;
        }

        Match match = regex.Match(documentText, offset);
        if (!match.Success || match.Index != offset || match.Length != length)
        {
            replacement = "";
            return false;
        }

        replacement = match.Result(ReplaceBox.Text);
        return true;
    }

    private void ReplaceAll()
    {
        if (_editor?.Document is null) return;

        var regex = BuildRegex();
        if (regex is null) return;

        var rangeStart = 0;
        var rangeEnd = _editor.Document.TextLength;
        var replaceInSelection = InSelectionCheck.IsChecked == true;

        if (replaceInSelection)
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

        // 置換文字列はすべて、文書を変更する前に解決しておく。後方参照の解決で例外が出ても
        // 1件も置換していないため部分適用にならない（例外時は「置換文字列が不正です」で中断）。
        // 文書はまだ無変更なので、全文を一度だけ取ってすべての解決に使い回せる。
        var documentText = _editor.Document.Text;
        var replacements = new List<string>(targets.Count);
        try
        {
            foreach (var (offset, length) in targets)
            {
                if (!TryResolveReplacement(regex, documentText, offset, length, out var resolved))
                {
                    MatchCountText.Text = "置換対象を取り直せませんでした";
                    return;
                }
                replacements.Add(resolved);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or RegexMatchTimeoutException)
        {
            MatchCountText.Text = "置換文字列が不正です";
            return;
        }

        var lengthDelta = 0;
        _suppressRecalculation = true;
        try
        {
            // 進行中フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            // BeginUpdate も「何か」に含む: ここで例外が出ると _suppressRecalculation が
            // true のまま固着し、以後の本文変更でヒット位置が再計算されず、古いオフセットで
            // 置換して本文を壊しうる。
            // まとめて 1 回の Undo で戻せるようにする。
            _editor.Document.BeginUpdate();
            try
            {
                // 後ろから置換すれば、前方のオフセットがずれない
                for (var i = 0; i < targets.Count; i++)
                {
                    var (offset, length) = targets[i];
                    var replacement = replacements[i];
                    _editor.Document.Replace(offset, length, replacement);
                    lengthDelta += replacement.Length - length;
                }
            }
            finally
            {
                _editor.Document.EndUpdate();
            }
        }
        finally
        {
            _suppressRecalculation = false;
        }

        UpdateMatches(resetIndex: true);
        if (replaceInSelection)
            _editor.Select(rangeStart, Math.Max(0, rangeEnd + lengthDelta - rangeStart));
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
