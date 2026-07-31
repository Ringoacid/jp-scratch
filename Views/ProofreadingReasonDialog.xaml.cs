using System.Windows;
using System.Windows.Controls;

namespace JpScratch.Views;

public partial class ProofreadingReasonDialog : Window
{
    internal ProofreadingReasonDialog(
        IEnumerable<string> suggestions,
        bool generatesAlternative)
    {
        InitializeComponent();
        ReasonSuggestionBox.ItemsSource = suggestions.ToArray();
        DescriptionText.Text = generatesAlternative
            ? "この案を採用しない理由を入力してください。理由を反映した別案を生成します。"
            : "この案を採用しない理由を入力してください。";
        CostNoticeText.Text = generatesAlternative
            ? "Gemini APIの料金が発生します。決定後、送信前にもう一度確認します。"
            : "";
        Loaded += (_, _) => ReasonBox.Focus();
    }

    internal string Reason => ReasonBox.Text.Trim();

    private void ReasonSuggestionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReasonSuggestionBox.SelectedItem is string suggestion)
        {
            ReasonBox.Text = suggestion;
            ReasonBox.CaretIndex = ReasonBox.Text.Length;
            ReasonBox.Focus();
            ReasonSuggestionBox.SelectedIndex = -1;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (Reason.Length == 0)
        {
            MessageBox.Show(
                this,
                "理由を入力してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ReasonBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
