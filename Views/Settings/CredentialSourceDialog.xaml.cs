using System.Windows;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>
/// 環境変数を初めて検出したときだけ表示する取得元選択（要件 3.5.5）。
/// Gemini / OpenAI の両プロバイダーで共用し、タイトルと本文は呼び出し側が指定する。
/// </summary>
public partial class CredentialSourceDialog : Window
{
    internal ApiKeySource SelectedSource { get; private set; } = ApiKeySource.Stored;

    internal CredentialSourceDialog(
        StoredCredentialState storedKeyState,
        string providerName,
        string environmentVariableName)
    {
        InitializeComponent();
        StoredKeyStatusText.Text = storedKeyState switch
        {
            StoredCredentialState.Available => "アプリには暗号化済みのAPIキーも保存されています。",
            StoredCredentialState.Unreadable => "アプリに保存されたAPIキーは読み取れません。",
            _ => "アプリに保存されたAPIキーはありません。",
        };
        Title = $"{providerName} API キー";
        WindowTitleText.Text = Title;
        DescriptionText.Text =
            $"環境変数 {environmentVariableName} が見つかりました。こちらを使用しますか？";
    }

    private void UseStoredButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSource = ApiKeySource.Stored;
        DialogResult = true;
    }

    private void UseEnvironmentButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSource = ApiKeySource.EnvironmentVariable;
        DialogResult = true;
    }

    /// <summary>
    /// タイトルバーの×。選択を確定せず閉じる（キャンセル扱い）。呼び出し側は
    /// <see cref="DialogResult"/> を確認して採用しない。× を「保存済みキーを使う」の確定に
    /// 配線していたのは驚きの挙動だった（ツールチップは「閉じる」）。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    /// <summary>
    /// Esc も × と同じキャンセル扱いにする。このダイアログには IsCancel のボタンを置いていない
    /// （IsCancel を「アプリに保存したキーを使う」へ付けると、Esc が保存済みキーの選択を
    /// 確定してしまうため）。Esc で選択を確定させないのは × と同じ挙動。
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
