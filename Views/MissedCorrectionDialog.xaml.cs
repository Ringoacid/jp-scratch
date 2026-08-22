using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>
/// 校正漏れ報告（docs/proofreading-ux-fixes-plan.md §9）の入力ダイアログ。
/// 修正前（読み取り専用）・修正後・補足理由（任意）を入力し、操作種別に応じた実行ボタンを出す。
/// 「修正後」の入力に応じて、前後の文脈を込めたプレビューをライブで描く:
/// - 置換: 原文を赤の二重取り消し線、修正後を緑の太字で表示
/// - 挿入: 挿入文字を緑の太字で表示
/// - 削除: 原文を赤の二重取り消し線で表示
/// このダイアログ自体は API を呼ばない。本文の編集は呼び出し側（MainWindow）が
/// 「記録に成功してから」行う（記録だけ失敗して本文だけ変わる状態を作らない）。
/// </summary>
public partial class MissedCorrectionDialog : Window
{
    private readonly string _original;
    private readonly string _leftContext;
    private readonly string _rightContext;

    internal MissedCorrectionDialog(
        string original,
        bool hasSelection,
        string? leftContext = null,
        string? rightContext = null)
    {
        _original = original;
        _leftContext = leftContext ?? "";
        _rightContext = rightContext ?? "";
        InitializeComponent();

        if (hasSelection)
        {
            OriginalBox.Text = original;
        }
        else
        {
            OriginalBox.Text = "（選択なし = カーソル位置へ挿入）";
        }

        UpdatePreviewAndButton();
        CorrectedBox.Focus();
    }

    /// <summary>修正後の文字列（空なら削除）。</summary>
    internal string Corrected => CorrectedBox.Text;

    /// <summary>補足理由（任意）。空でも可。</summary>
    internal string Reason => ReasonBox.Text;

    private void CorrectedBox_TextChanged(object sender, TextChangedEventArgs e)
        => UpdatePreviewAndButton();

    /// <summary>
    /// 操作種別（置換・挿入・削除）と実行可否を判定し、プレビューと実行ボタンを更新する。
    /// 選択なし・修正後が空のときや、修正前と修正後が同一のときは実行不可。
    /// </summary>
    private void UpdatePreviewAndButton()
    {
        MissedCorrectionKind kind = MissedCorrectionAction.Determine(
            _original,
            CorrectedBox.Text,
            out bool allowed);
        ExecuteButton.Content = MissedCorrectionAction.ButtonText(kind);
        ExecuteButton.IsEnabled = allowed;
        UpdatePreview(kind, CorrectedBox.Text);
    }

    /// <summary>
    /// 前後の文脈と修正箇所を強調したプレビューを組み立てる。
    /// 例（挿入）: <c>…で終了します</c> の「ま」が緑の太字、例（置換）:
    /// <c>合について、げｍざい現在の進捗を</c> の「げｍざい」が赤の二重取り消し線・「現在」が緑の太字。
    /// 修正箇所が分かるよう、文脈は前後 <see cref="MissedCorrectionPreview.DefaultMaxContextChars"/> 文字で
    /// 切り詰め、省略は「…」で示す。
    /// </summary>
    private void UpdatePreview(MissedCorrectionKind kind, string corrected)
    {
        PreviewText.Inlines.Clear();

        Brush contextBrush = Brush("SubtleTextBrush");
        Brush originalBrush = Brush("ProofreadingOriginalBrush");
        Brush strikeBrush = Brush("ProofreadingStrikeBrush");
        Brush suggestionBrush = Brush("ProofreadingSuggestionBrush");

        string left = MissedCorrectionPreview.FlattenForPreview(_leftContext);
        string right = MissedCorrectionPreview.FlattenForPreview(_rightContext);
        string leftDisplay = MissedCorrectionPreview.TruncateLeft(
            left, MissedCorrectionPreview.DefaultMaxContextChars);
        string rightDisplay = MissedCorrectionPreview.TruncateRight(
            right, MissedCorrectionPreview.DefaultMaxContextChars);

        AddRun(leftDisplay, contextBrush);

        switch (kind)
        {
            case MissedCorrectionKind.Replace:
                AddRun(
                    MissedCorrectionPreview.TruncateOriginal(_original),
                    originalBrush,
                    strike: true,
                    strikeBrush);
                AddRun(corrected, suggestionBrush, bold: true);
                break;
            case MissedCorrectionKind.Insert:
                AddRun(corrected, suggestionBrush, bold: true);
                break;
            case MissedCorrectionKind.Delete:
                AddRun(
                    MissedCorrectionPreview.TruncateOriginal(_original),
                    originalBrush,
                    strike: true,
                    strikeBrush);
                break;
            default:
                // 実行不可（選択なし＋空・修正前後同一）: 強調なしで文脈だけを出す。
                break;
        }

        AddRun(rightDisplay, contextBrush);
    }

    private void AddRun(
        string text,
        Brush brush,
        bool strike = false,
        Brush? strikeBrush = null,
        bool bold = false)
    {
        if (text.Length == 0)
            return;

        var run = new Run(text)
        {
            Foreground = brush,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        if (strike)
            run.TextDecorations = CreateDoubleStrikethrough(strikeBrush ?? brush);
        PreviewText.Inlines.Add(run);
    }

    /// <summary>
    /// エディタのインライン差分（docs/proofreading-ux-fixes-plan.md §5）と同じ赤い二重取り消し線。
    /// 間隔 ±1.2px は実測値。PenOffsetUnit / PenThicknessUnit は Pixel 固定
    /// （FontRecommended のままにするとフォント推奨単位で解釈され線が外へ飛ぶ）。
    /// </summary>
    private static TextDecorationCollection CreateDoubleStrikethrough(Brush brush)
    {
        var pen = new Pen(brush, 1.0);
        pen.Freeze();

        var first = new TextDecoration
        {
            Location = TextDecorationLocation.Strikethrough,
            Pen = pen,
            PenOffset = -1.2,
            PenOffsetUnit = TextDecorationUnit.Pixel,
            PenThicknessUnit = TextDecorationUnit.Pixel,
        };
        var second = new TextDecoration
        {
            Location = TextDecorationLocation.Strikethrough,
            Pen = pen,
            PenOffset = 1.2,
            PenOffsetUnit = TextDecorationUnit.Pixel,
            PenThicknessUnit = TextDecorationUnit.Pixel,
        };

        var decorations = new TextDecorationCollection();
        decorations.Add(first);
        decorations.Add(second);
        decorations.Freeze();
        return decorations;
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
