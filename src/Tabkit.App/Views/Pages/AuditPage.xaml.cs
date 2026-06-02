using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tabkit.App.ViewModels.Pages;

namespace Tabkit.App.Views.Pages;

public partial class AuditPage : Page
{
    private static readonly string[] AcceptedExtensions = { ".twb", ".twbx" };

    public AuditPage(AuditViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private AuditViewModel? ViewModel => DataContext as AuditViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // No drop target while an audit is running — show the no-drop cursor.
        var ok = ViewModel?.CanAcceptDrop == true && AcceptsDrop(e);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var pick = paths.FirstOrDefault(IsAcceptedFile);
        if (pick is null || ViewModel is null) return;

        // Ignore drops during a run; the VM also guards this defensively.
        if (!ViewModel.CanAcceptDrop) { e.Handled = true; return; }

        e.Handled = true;
        await ViewModel.OpenDroppedFileAsync(pick);
    }

    private static bool AcceptsDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        return paths.Any(IsAcceptedFile);
    }

    private static bool IsAcceptedFile(string p)
    {
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) return false;
        var ext = Path.GetExtension(p).ToLowerInvariant();
        return AcceptedExtensions.Contains(ext);
    }

    private void OnRecentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not string path) return;
        // Reset so picking the same item again still fires.
        cb.SelectedIndex = -1;
        if (ViewModel is null) return;
        if (ViewModel.OpenRecentCommand.CanExecute(path))
            ViewModel.OpenRecentCommand.Execute(path);
    }
}
