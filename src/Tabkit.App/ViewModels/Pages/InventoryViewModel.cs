using System.Data;
using System.IO;
using Microsoft.Win32;
using Tabkit.App.Services;

namespace Tabkit.App.ViewModels.Pages;

public enum InventoryQueryMode
{
    AllWorkbooks,
    ReferencesTable,
    EmbeddedCredentials,
    UsernameUsers,
    ServerSummary,
}

public partial class InventoryViewModel : ObservableObject
{
    private readonly InventoryService _inv;
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private CancellationTokenSource? _scanCts;

    public InventoryViewModel(InventoryService inv, SettingsService settings, ISnackbarService snackbar)
    {
        _inv = inv;
        _settings = settings;
        _snackbar = snackbar;
        _databasePath = ResolveInitialDbPath();
        _scanRoot = string.Empty;
        _settings.Changed += (_, __) =>
        {
            // If the user edited InventoryDbPath in Settings and we haven't opened
            // anything yet (or are still on the default), pick up the new value.
            if (!_inv.IsOpen) DatabasePath = ResolveInitialDbPath();
        };
    }

    private string ResolveInitialDbPath()
    {
        var configured = _settings.Current.InventoryDbPath;
        return string.IsNullOrWhiteSpace(configured)
            ? InventoryService.DefaultDbPath()
            : configured;
    }

    [ObservableProperty] private string _databasePath;
    [ObservableProperty] private string _scanRoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleScan))]
    [NotifyPropertyChangedFor(nameof(IsVisibleRunQuery))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleRunQuery))]
    private bool _isQuerying;

    [ObservableProperty] private string _statusMessage = "Open or scan an inventory to begin.";
    [ObservableProperty] private string _currentScanPath = string.Empty;

    // Visibility mirrors for hide-don't-disable. CancelScan stays visible-but-disabled
    // (Gallery convention exception for operation-control pairs).
    public bool IsVisibleScan => CanScan();
    public bool IsVisibleRunQuery => CanRunQuery();

    [ObservableProperty] private long _statWorkbooks;
    [ObservableProperty] private long _statDatasources;
    [ObservableProperty] private long _statConnections;
    [ObservableProperty] private long _statFields;
    [ObservableProperty] private long _statWithEmbeddedCreds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleRunQuery))]
    private InventoryQueryMode _queryMode = InventoryQueryMode.AllWorkbooks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleRunQuery))]
    private string _tableSearch = string.Empty;
    [ObservableProperty] private DataView? _results;
    [ObservableProperty] private int _resultCount;
    [ObservableProperty] private string _resultFilter = string.Empty;

    public IReadOnlyList<InventoryQueryMode> QueryModes { get; } = Enum.GetValues<InventoryQueryMode>();

    public bool TableSearchEnabled => QueryMode == InventoryQueryMode.ReferencesTable;

    partial void OnQueryModeChanged(InventoryQueryMode value)
    {
        OnPropertyChanged(nameof(TableSearchEnabled));
        RunQueryCommand.NotifyCanExecuteChanged();
    }

    partial void OnTableSearchChanged(string value) => RunQueryCommand.NotifyCanExecuteChanged();
    partial void OnResultFilterChanged(string value) => ApplyResultFilter();
    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
        RunQueryCommand.NotifyCanExecuteChanged();
        // Opening / re-picking the DB disposes the current InventoryStore. A
        // scan in flight captured that store, so allowing a reopen mid-scan
        // disposes it out from under the running task. Gate both on !IsScanning.
        OpenDatabaseCommand.NotifyCanExecuteChanged();
        PickDatabaseCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsVisibleDbControls));
    }
    partial void OnIsQueryingChanged(bool value) => RunQueryCommand.NotifyCanExecuteChanged();

    private bool CanModifyDatabase() => !IsScanning;

    // Visibility mirror for the DB-bar controls (Open/Create + Pick), collapsed
    // during a scan, consistent with the hide-don't-disable convention.
    public bool IsVisibleDbControls => CanModifyDatabase();

    [RelayCommand(CanExecute = nameof(CanModifyDatabase))]
    private void OpenDatabase()
    {
        try
        {
            _inv.Open(DatabasePath);
            DatabasePath = _inv.DatabasePath;
            RefreshStats();
            StatusMessage = $"Opened {DatabasePath}.";
        }
        catch (Exception ex)
        {
            Snack("Open failed", ex.Message, danger: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyDatabase))]
    private void PickDatabase()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Inventory database (creates if missing)",
            Filter = "SQLite (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            FileName = Path.GetFileName(DatabasePath),
            InitialDirectory = Path.GetDirectoryName(DatabasePath),
            OverwritePrompt = false,
            CheckFileExists = false,
        };
        if (dlg.ShowDialog() == true)
        {
            DatabasePath = dlg.FileName;
            OpenDatabase();
        }
    }

    [RelayCommand]
    private void PickScanRoot()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pick a folder to scan for .twb / .twbx",
        };
        if (dlg.ShowDialog() == true)
            ScanRoot = dlg.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanRoot) || !Directory.Exists(ScanRoot))
        {
            Snack("Scan failed", "Pick a folder that exists.", danger: true);
            return;
        }
        if (!_inv.IsOpen) _inv.Open(DatabasePath);

        IsScanning = true;
        CurrentScanPath = string.Empty;
        StatusMessage = $"Scanning {ScanRoot}...";
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<string>(p => CurrentScanPath = p);

        try
        {
            var result = await _inv.ScanAsync(ScanRoot, progress, _scanCts.Token);
            RefreshStats();
            StatusMessage =
                $"Scanned {result.Scanned}: indexed {result.Indexed}, " +
                $"skipped {result.Skipped}, errors {result.Errors.Count}.";
            if (result.Errors.Count > 0)
                Snack(
                    $"{result.Errors.Count} parse error(s)",
                    string.Join("; ", result.Errors.Take(3).Select(e => Path.GetFileName(e.Path) + ": " + e.Message)),
                    danger: false);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
            Snack("Scan failed", ex.Message, danger: true);
        }
        finally
        {
            IsScanning = false;
            CurrentScanPath = string.Empty;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRunQuery))]
    private void RunQuery()
    {
        if (!_inv.IsOpen)
        {
            Snack("No database open", "Open or scan first.", danger: true);
            return;
        }

        IsQuerying = true;
        try
        {
            DataTable table = QueryMode switch
            {
                InventoryQueryMode.AllWorkbooks         => _inv.QueryAllWorkbooks(),
                InventoryQueryMode.ReferencesTable      => _inv.QueryReferencesTable(TableSearch.Trim()),
                InventoryQueryMode.EmbeddedCredentials  => _inv.QueryEmbeddedCredentials(),
                InventoryQueryMode.UsernameUsers        => _inv.QueryUsernameUsers(),
                InventoryQueryMode.ServerSummary        => _inv.QueryServerSummary(),
                _ => new DataTable(),
            };
            Results = table.DefaultView;
            ResultCount = table.Rows.Count;
            ApplyResultFilter();
            StatusMessage = ResultCount switch
            {
                0 => "Query returned no rows.",
                1 => "1 row.",
                _ => $"{ResultCount} rows.",
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"Query failed: {ex.Message}";
            Snack("Query failed", ex.Message, danger: true);
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (Results is null || Results.Count == 0)
        {
            Snack("Nothing to export", "Run a query first.", danger: false);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "Export results to CSV",
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"tabkit-{QueryMode}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            // Export the filtered DataView (honors RowFilter/Sort), not the
            // underlying table — otherwise the count says "filtered" but the
            // file contains every row.
            InventoryService.ExportCsv(Results, dlg.FileName);
            StatusMessage = $"Wrote {Results.Count} row(s) to {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            Snack("Export failed", ex.Message, danger: true);
        }
    }

    private void ApplyResultFilter()
    {
        if (Results is null) return;
        if (string.IsNullOrWhiteSpace(ResultFilter))
        {
            Results.RowFilter = string.Empty;
            return;
        }
        // Escape the user's text for a DataView LIKE pattern. RowFilter treats
        // * % [ ] as wildcards/metacharacters, so raw input like "a*b", "a%b",
        // "[" or "a[b" produces an invalid pattern and throws on every keystroke.
        // Wrap each metachar in brackets; escape ' for the string literal.
        var needle = EscapeLikeValue(ResultFilter.Trim());
        var parts = Results.Table!.Columns
            .Cast<DataColumn>()
            .Select(c => $"[{c.ColumnName.Replace("]", "]]")}] LIKE '%{needle}%'");
        try
        {
            Results.RowFilter = string.Join(" OR ", parts);
        }
        catch (Exception ex)
        {
            // Belt-and-suspenders: never let a filter expression crash the UI.
            Results.RowFilter = string.Empty;
            StatusMessage = $"Filter ignored: {ex.Message}";
        }
    }

    /// <summary>Escape a value for a DataView LIKE pattern (per DataColumn.Expression rules).</summary>
    private static string EscapeLikeValue(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            if (ch is '*' or '%' or '[' or ']')
                sb.Append('[').Append(ch).Append(']');
            else if (ch == '\'')
                sb.Append("''");
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private bool CanScan() => !IsScanning;
    private bool CanCancelScan() => IsScanning;
    private bool CanRunQuery() =>
        !IsScanning && !IsQuerying &&
        (QueryMode != InventoryQueryMode.ReferencesTable || !string.IsNullOrWhiteSpace(TableSearch));

    private void RefreshStats()
    {
        try
        {
            var s = _inv.Stats();
            StatWorkbooks = s.GetValueOrDefault("workbooks");
            StatDatasources = s.GetValueOrDefault("datasources");
            StatConnections = s.GetValueOrDefault("connections");
            StatFields = s.GetValueOrDefault("fields");
            StatWithEmbeddedCreds = s.GetValueOrDefault("with_embedded_creds");
        }
        catch
        {
            StatWorkbooks = StatDatasources = StatConnections = StatFields = StatWithEmbeddedCreds = 0;
        }
    }

    private void Snack(string title, string message, bool danger)
    {
        var appearance = danger ? ControlAppearance.Danger : ControlAppearance.Caution;
        var icon = danger ? SymbolRegular.ErrorCircle24 : SymbolRegular.Info24;
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(6));
    }
}
