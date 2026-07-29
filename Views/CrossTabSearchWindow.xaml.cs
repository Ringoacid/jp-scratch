using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using JpScratch.Infrastructure;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>全タブ横断検索の 1 ヒット（要件 3.2.3）。</summary>
public sealed record CrossTabHit(
    string TabId,
    string TabTitle,
    bool IsTrash,
    int LineNumber,
    int Offset,
    int Length,
    string Preview);

/// <summary>
/// 開いているタブとゴミ箱を横断して検索する（要件 3.2.3）。
/// タブ数は現実的に数十なので、FTS5 を持ち込まずファイルとメモリを素直に走査する。
/// </summary>
public partial class CrossTabSearchWindow : Window
{
    private readonly TabManager _tabs;
    private readonly TabRepository _repository;

    internal CrossTabSearchWindow(TabManager tabs, TabRepository repository)
    {
        _tabs = tabs;
        _repository = repository;
        InitializeComponent();
    }

    public event Action<CrossTabHit>? HitSelected;

    public void SetTerm(string term)
    {
        if (!string.IsNullOrEmpty(term))
        {
            TermBox.Text = term;
            Search();
        }

        TermBox.Focus();
        TermBox.SelectAll();
    }

    private void Search()
    {
        var term = TermBox.Text;
        if (string.IsNullOrEmpty(term))
        {
            ResultsList.ItemsSource = null;
            SummaryText.Text = "";
            return;
        }

        var pattern = RegexCheck.IsChecked == true ? term : Regex.Escape(term);
        var options = RegexOptions.Multiline;
        if (MatchCaseCheck.IsChecked != true) options |= RegexOptions.IgnoreCase;

        Regex regex;
        try
        {
            regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            SummaryText.Text = $"正規表現が不正です: {ex.Message}";
            ResultsList.ItemsSource = null;
            return;
        }

        var hits = new List<CrossTabHit>();

        // 開いているタブはメモリ上の本文をそのまま見る（未保存の変更も対象になる）
        foreach (var tab in _tabs.Tabs)
            CollectHits(hits, regex, tab.Id, tab.Title, isTrash: false, tab.Document.Text);

        if (IncludeTrashCheck.IsChecked == true)
        {
            foreach (var tab in _repository.LoadTrash())
            {
                var text = AtomicFile.ReadAllTextOrEmpty(AppPaths.TrashFile(tab.Id));
                CollectHits(hits, regex, tab.Id, tab.Title, isTrash: true, text);
            }
        }

        ResultsList.ItemsSource = hits;
        var tabCount = hits.Select(h => h.TabId).Distinct().Count();
        SummaryText.Text = hits.Count == 0
            ? "見つかりませんでした"
            : $"{hits.Count} 件 / {tabCount} タブ";

        if (hits.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private static void CollectHits(List<CrossTabHit> hits, Regex regex,
                                    string tabId, string tabTitle, bool isTrash, string text)
    {
        if (text.Length == 0) return;

        MatchCollection matches;
        try
        {
            matches = regex.Matches(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return;
        }

        foreach (Match match in matches)
        {
            if (match.Length == 0) continue;

            var (lineNumber, lineStart, lineEnd) = LocateLine(text, match.Index);
            var preview = text[lineStart..lineEnd].Trim();
            if (preview.Length > 120) preview = preview[..120] + "…";

            hits.Add(new CrossTabHit(
                tabId,
                isTrash ? $"[ゴミ箱] {tabTitle}" : tabTitle,
                isTrash,
                lineNumber,
                match.Index,
                match.Length,
                preview));
        }
    }

    /// <summary>ヒット位置が何行目か、その行の範囲はどこかを求める。</summary>
    private static (int LineNumber, int Start, int End) LocateLine(string text, int offset)
    {
        var lineNumber = 1;
        var start = 0;

        for (var i = 0; i < offset; i++)
        {
            if (text[i] != '\n') continue;
            lineNumber++;
            start = i + 1;
        }

        var end = text.IndexOf('\n', offset);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;

        return (lineNumber, start, end);
    }

    private void Jump()
    {
        if (ResultsList.SelectedItem is CrossTabHit hit) HitSelected?.Invoke(hit);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => Search();

    private void TermBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed || e.Key != Key.Enter) return;
        Search();
        e.Handled = true;
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Jump();

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Jump();
        e.Handled = true;
    }

    private void JumpButton_Click(object sender, RoutedEventArgs e) => Jump();

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
