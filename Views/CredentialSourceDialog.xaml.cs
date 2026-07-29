using System.Windows;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>環境変数を初めて検出したときだけ表示する取得元選択（要件 3.5.5）。</summary>
public partial class CredentialSourceDialog : Window
{
    internal GeminiApiKeySource SelectedSource { get; private set; } = GeminiApiKeySource.Stored;

    internal CredentialSourceDialog(StoredCredentialState storedKeyState)
    {
        InitializeComponent();
        StoredKeyStatusText.Text = storedKeyState switch
        {
            StoredCredentialState.Available => "アプリには暗号化済みのAPIキーも保存されています。",
            StoredCredentialState.Unreadable => "アプリに保存されたAPIキーは読み取れません。",
            _ => "アプリに保存されたAPIキーはありません。",
        };
    }

    private void UseStoredButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSource = GeminiApiKeySource.Stored;
        DialogResult = true;
    }

    private void UseEnvironmentButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSource = GeminiApiKeySource.EnvironmentVariable;
        DialogResult = true;
    }
}
