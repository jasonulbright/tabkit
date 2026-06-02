using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using Microsoft.Win32;
using Tabkit.App.Services;
using Tabkit.Core.Audit;
using Tabkit.Core.Loading;
using Tabkit.Core.Output;

namespace Tabkit.App.ViewModels.Pages;

public partial class AuditViewModel : ObservableObject
{
    private const int RecentCap = 8;

    private readonly WorkbookService _workbookSvc;
    private readonly AuditService _auditSvc;
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private ICollectionView? _findingsView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleRunAudit))]
    private string _workbookPath = string.Empty;

    [ObservableProperty] private string _statusMessage = "Open a .twb / .twbx workbook (or drag one in) to begin.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleOpenWorkbook))]
    [NotifyPropertyChangedFor(nameof(IsVisibleRunAudit))]
    [NotifyPropertyChangedFor(nameof(IsVisibleExport))]
    private bool _isRunning;

    // Visibility mirrors for hide-don't-disable button binding (one per CanExecute predicate).
    public bool IsVisibleOpenWorkbook => CanInteract();
    public bool IsVisibleRunAudit => CanRun();
    public bool IsVisibleExport => CanExport();

    // Total counts (always reflect raw audit results, not the filter view)
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warnCount;
    [ObservableProperty] private int _infoCount;

    [ObservableProperty] private string _pack = "all";

    // Filter state
    [ObservableProperty] private bool _showErrors = true;
    [ObservableProperty] private bool _showWarns  = true;
    [ObservableProperty] private bool _showInfo   = true;
    [ObservableProperty] private string _textFilter = string.Empty;
    [ObservableProperty] private int _visibleCount;

    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<string> RecentWorkbooks { get; } = new();

    public IReadOnlyList<string> Packs { get; } = new[] { "all", "audit", "governance" };

    public AuditViewModel(
        WorkbookService workbookSvc,
        AuditService auditSvc,
        SettingsService settings,
        ISnackbarService snackbar)
    {
        _workbookSvc = workbookSvc;
        _auditSvc = auditSvc;
        _settings = settings;
        _snackbar = snackbar;

        _findingsView = CollectionViewSource.GetDefaultView(Findings);
        _findingsView.Filter = FilterFinding;

        LoadRecentsFromSettings();
        _settings.Changed += (_, __) => LoadRecentsFromSettings();
    }

    public ICollectionView? FindingsView => _findingsView;

    partial void OnWorkbookPathChanged(string value) => RunAuditCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningChanged(bool value)
    {
        RunAuditCommand.NotifyCanExecuteChanged();
        OpenWorkbookCommand.NotifyCanExecuteChanged();
        OpenRecentCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
        ExportSarifCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }
    partial void OnShowErrorsChanged(bool value) => RefreshFilter();
    partial void OnShowWarnsChanged(bool value)  => RefreshFilter();
    partial void OnShowInfoChanged(bool value)   => RefreshFilter();
    partial void OnTextFilterChanged(string value) => RefreshFilter();

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task OpenWorkbookAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Tableau workbook",
            Filter = "Tableau workbooks (*.twb;*.twbx)|*.twb;*.twbx|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        await LoadFromPathAsync(dlg.FileName);
    }

    /// <summary>
    /// Called from the page code-behind when a file is dropped onto the page.
    /// The drop path is not command-gated, so guard reentrancy here: ignore
    /// drops while an audit is already running.
    /// </summary>
    public Task OpenDroppedFileAsync(string path)
    {
        if (IsRunning) return Task.CompletedTask;
        return LoadFromPathAsync(path);
    }

    /// <summary>True when the page may accept a dropped workbook (no run in flight).</summary>
    public bool CanAcceptDrop => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAuditAsync() => RunAuditCoreAsync();

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            RemoveRecent(path);
            _snackbar.Show("Not found", path, ControlAppearance.Caution,
                new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(4));
            return;
        }
        await LoadFromPathAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportJson() => ExportAt(".json", "JSON", () =>
        JsonOutput.FindingsToJson(Findings));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportSarif() => ExportAt(".sarif", "SARIF 2.1.0", () =>
        SarifOutput.FindingsToSarif(Findings, _auditSvc.Rules));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportHtml() => ExportAt(".html", "HTML", () =>
        HtmlOutput.FindingsToHtml(
            Findings,
            _auditSvc.Rules,
            title: $"tabkit audit — {Path.GetFileName(WorkbookPath)}"));

    private bool CanInteract() => !IsRunning;
    private bool CanRun() => !IsRunning && !string.IsNullOrWhiteSpace(WorkbookPath);
    private bool CanExport() => !IsRunning && Findings.Count > 0;

    private async Task LoadFromPathAsync(string path)
    {
        WorkbookPath = path;
        AddRecent(path);
        await RunAuditCoreAsync();
    }

    private async Task RunAuditCoreAsync()
    {
        // Prevent overlapping runs — a second run would clear/append the same
        // shared Findings collection mid-flight.
        if (IsRunning) return;
        IsRunning = true;

        // Snapshot the inputs before the first await. The pack selector stays
        // interactive during a run, so reading WorkbookPath / Pack after an
        // await could mix a new selection into the in-flight run.
        var workbookPath = WorkbookPath;
        var pack = Pack;

        StatusMessage = $"Loading {Path.GetFileName(workbookPath)}...";
        Findings.Clear();
        ErrorCount = WarnCount = InfoCount = 0;

        try
        {
            var wb = await _workbookSvc.LoadAsync(workbookPath);
            StatusMessage = "Running rule packs...";
            var packArg = string.Equals(pack, "all", StringComparison.OrdinalIgnoreCase) ? null : pack;
            var results = await _auditSvc.RunAsync(wb, packArg);

            foreach (var f in results.OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId))
                Findings.Add(f);

            ErrorCount = results.Count(f => f.Severity == Severity.Error);
            WarnCount = results.Count(f => f.Severity == Severity.Warn);
            InfoCount = results.Count(f => f.Severity == Severity.Info);

            RefreshFilter();
            StatusMessage = results.Count == 0
                ? "Clean. No findings."
                : $"{results.Count} finding(s): {ErrorCount} error / {WarnCount} warn / {InfoCount} info.";

            ExportJsonCommand.NotifyCanExecuteChanged();
            ExportSarifCommand.NotifyCanExecuteChanged();
            ExportHtmlCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsVisibleExport));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            _snackbar.Show("Audit failed", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(6));
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool FilterFinding(object o)
    {
        if (o is not Finding f) return false;

        var sevOk = f.Severity switch
        {
            Severity.Error => ShowErrors,
            Severity.Warn  => ShowWarns,
            Severity.Info  => ShowInfo,
            _ => true,
        };
        if (!sevOk) return false;

        if (!string.IsNullOrWhiteSpace(TextFilter))
        {
            var q = TextFilter.Trim();
            if (!(f.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                  f.Message.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                  f.RuleId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                  (f.Location?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))
                return false;
        }
        return true;
    }

    private void RefreshFilter()
    {
        _findingsView?.Refresh();
        VisibleCount = _findingsView?.Cast<object>().Count() ?? 0;
    }

    private void ExportAt(string ext, string label, Func<string> render)
    {
        var dlg = new SaveFileDialog
        {
            Title = $"Export {label}",
            Filter = ext switch
            {
                ".json"  => "JSON (*.json)|*.json|All files (*.*)|*.*",
                ".sarif" => "SARIF (*.sarif)|*.sarif|All files (*.*)|*.*",
                ".html"  => "HTML (*.html)|*.html|All files (*.*)|*.*",
                _ => "All files (*.*)|*.*",
            },
            FileName = string.IsNullOrEmpty(WorkbookPath)
                ? $"tabkit-audit-{DateTime.Now:yyyyMMdd-HHmmss}{ext}"
                : $"{Path.GetFileNameWithoutExtension(WorkbookPath)}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}",
            InitialDirectory = ResolveExportDir(),
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, render());
            StatusMessage = $"Wrote {label} to {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            _snackbar.Show("Export failed", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(6));
        }
    }

    private string? ResolveExportDir()
    {
        var configured = _settings.Current.AuditOutputDir;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;
        return string.IsNullOrEmpty(WorkbookPath) ? null : Path.GetDirectoryName(WorkbookPath);
    }

    private void LoadRecentsFromSettings()
    {
        RecentWorkbooks.Clear();
        foreach (var p in _settings.Current.RecentWorkbooks)
            RecentWorkbooks.Add(p);
    }

    private void AddRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var settings = _settings.Current;
        settings.RecentWorkbooks.RemoveAll(p =>
            string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        settings.RecentWorkbooks.Insert(0, path);
        while (settings.RecentWorkbooks.Count > RecentCap)
            settings.RecentWorkbooks.RemoveAt(settings.RecentWorkbooks.Count - 1);
        _settings.Save(settings);
    }

    private void RemoveRecent(string path)
    {
        var settings = _settings.Current;
        var changed = settings.RecentWorkbooks.RemoveAll(p =>
            string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (changed) _settings.Save(settings);
    }
}
