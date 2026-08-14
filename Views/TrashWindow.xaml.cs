using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>
/// ゴミ箱の中身を一覧表示し、復元・完全削除・空にするを提供する。
/// 本文は一覧の読み込み時に本文ファイルから読み、下部の読み取り専用プレビューに出す。
/// </summary>
public partial class TrashWindow : Window
{
    private readonly TabManager _tabs;
    private readonly TabRepository _repository;
    private readonly ObservableCollection<TrashListItem> _items = [];

    internal TrashWindow(TabManager tabs, TabRepository repository)
    {
        _tabs = tabs;
        _repository = repository;
        InitializeComponent();
        ResultsList.ItemsSource = _items;
        Refresh();
    }

    /// <summary>復元に成功したとき。MainWindow がステータスバーへ出す。</summary>
    public event Action<ScratchTab>? Restored;

    /// <summary>ゴミ箱を読み直す（開き直し・操作後に呼ぶ）。</summary>
    public void Refresh()
    {
        _items.Clear();
        foreach (var tab in _repository.LoadTrash())
        {
            // 行数とプレビュー用に本文をここで読む。ID の形式が壊れている行は表示から
            // 外さず、行数「—」のまま残す（復元・削除のどちらもできないことを見せたい）。
            string? body = null;
            try
            {
                if (AtomicFile.TryReadAllText(AppPaths.TrashFile(tab.Id), out var text))
                    body = text;
            }
            catch (InvalidDataException)
            {
                // AppPaths.TrashFile が投げる「タブIDの形式が不正」。本文の位置が決まらないので
                // body は null のまま（この行を壊れた 1 行として表示し続ける）。
            }

            _items.Add(new TrashListItem(tab, body));
        }

        UpdateSummary();
        UpdateButtons();
    }

    private void UpdateSummary() =>
        SummaryText.Text = _items.Count == 0 ? "ゴミ箱は空です" : $"{_items.Count} 件";

    private void SetMessage(string message) => SummaryText.Text = message;

    private void UpdateButtons()
    {
        bool selected = ResultsList.SelectedItem is TrashListItem;
        RestoreButton.IsEnabled = selected;
        DeleteButton.IsEnabled = selected;
        EmptyTrashButton.IsEnabled = _items.Count > 0;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (ResultsList.SelectedItem is TrashListItem item)
        {
            PreviewBox.Text = item.Body ?? "本文を読み込めませんでした";
            return;
        }

        PreviewBox.Text = "";
    }

    private void RestoreSelected()
    {
        if (ResultsList.SelectedItem is not TrashListItem item) return;

        try
        {
            var restored = _tabs.Restore(item.Tab);
            if (restored is null)
            {
                SetMessage("このタブは壊れているため復元できません");
                return;
            }

            _items.Remove(item);
            UpdateSummary();
            Restored?.Invoke(restored);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetMessage("タブを復元できませんでした（本文ファイルを移動できません）");
        }
    }

    private void DeleteSelected()
    {
        if (ResultsList.SelectedItem is not TrashListItem item) return;

        if (MessageBox.Show(this,
                $"タブ「{item.Tab.Title}」を完全に削除します。この操作は元に戻せません。",
                "JP Scratch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _tabs.DeletePermanently(item.Tab);
            _items.Remove(item);
            UpdateSummary();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetMessage("削除できませんでした（本文ファイルを削除できません）");
        }
    }

    private void EmptyTrash()
    {
        if (_items.Count == 0) return;

        if (MessageBox.Show(this,
                $"ゴミ箱の {_items.Count} 件をすべて完全に削除します。この操作は元に戻せません。",
                "JP Scratch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        // 壊れた ID・一時的な I/O 失敗で消えなかった分は飛ばして続行される。
        // 読み直して実態（残った行）を見せる。
        int deleted = _tabs.EmptyTrash();
        Refresh();
        SetMessage(deleted > 0 ? $"{deleted} 件を削除しました" : "削除できませんでした");
    }

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => RestoreSelected();

    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void EmptyTrashButton_Click(object sender, RoutedEventArgs e) => EmptyTrash();

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateButtons();

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => RestoreSelected();

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        RestoreSelected();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
