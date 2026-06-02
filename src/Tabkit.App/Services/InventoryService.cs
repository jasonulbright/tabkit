using System.Data;
using System.IO;
using Tabkit.Core.Inventory;

namespace Tabkit.App.Services;

/// <summary>
/// UI-layer wrapper around <see cref="InventoryStore"/>, <see cref="InventoryScanner"/>,
/// and <see cref="InventoryQueries"/>. Owns the store lifetime, exposes async scan with
/// progress reporting, returns <see cref="DataTable"/>s so the UI can bind directly
/// to <c>DataView</c> for free sort/filter.
/// </summary>
public sealed class InventoryService : IDisposable
{
    public const string DefaultDirName = "Tabkit";
    public const string DefaultDbFileName = "inventory.sqlite";

    private InventoryStore? _store;

    public string DatabasePath { get; private set; } = DefaultDbPath();

    public static string DefaultDbPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DefaultDirName,
        DefaultDbFileName);

    public bool IsOpen => _store is not null;

    public void Open(string? path = null)
    {
        var target = path ?? DefaultDbPath();
        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _store?.Dispose();
        _store = new InventoryStore(target);
        DatabasePath = target;
    }

    public void Close()
    {
        _store?.Dispose();
        _store = null;
    }

    public void Dispose() => Close();

    private InventoryStore Require() =>
        _store ?? throw new InvalidOperationException("Inventory store is not open. Call Open() first.");

    public Task<ScanResult> ScanAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var store = Require();
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return InventoryScanner.ScanTree(
                root,
                store,
                onProgress: path =>
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(path);
                });
        }, ct);
    }

    public IReadOnlyDictionary<string, long> Stats() => Require().Stats();

    public DataTable QueryEmbeddedCredentials()
        => ToTable(InventoryQueries.WorkbooksWithEmbeddedCredentials(Require()));

    public DataTable QueryUsernameUsers()
        => ToTable(InventoryQueries.WorkbooksUsingUsername(Require()));

    public DataTable QueryServerSummary()
        => ToTable(InventoryQueries.ServerSummary(Require()));

    public DataTable QueryReferencesTable(string table)
        => ToTable(InventoryQueries.WorkbooksReferencingTable(Require(), table));

    public DataTable QueryAllWorkbooks()
        => ToTable(InventoryQueries.ListWorkbooks(Require()));

    private static DataTable ToTable(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        if (rows.Count == 0) return table;
        foreach (var key in rows[0].Keys)
            table.Columns.Add(key, typeof(string));
        foreach (var row in rows)
        {
            var dr = table.NewRow();
            foreach (var (k, v) in row)
                dr[k] = v?.ToString() ?? string.Empty;
            table.Rows.Add(dr);
        }
        return table;
    }

    /// <summary>Write a DataTable to a CSV file (RFC 4180-ish: comma sep, quoted on demand).</summary>
    public static void ExportCsv(DataTable table, string path)
    {
        using var writer = new StreamWriter(path);
        var cols = table.Columns.Cast<DataColumn>().ToArray();
        writer.WriteLine(string.Join(",", cols.Select(c => CsvQuote(c.ColumnName))));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", cols.Select(c => CsvQuote(row[c]?.ToString() ?? ""))));
    }

    /// <summary>
    /// Write only the rows currently visible in a <see cref="DataView"/> — honors
    /// its RowFilter and Sort. The UI binds the grid to a filtered DataView, so
    /// export must follow the filter, not dump the underlying table.
    /// </summary>
    public static void ExportCsv(DataView view, string path)
    {
        using var writer = new StreamWriter(path);
        var cols = view.Table!.Columns.Cast<DataColumn>().ToArray();
        writer.WriteLine(string.Join(",", cols.Select(c => CsvQuote(c.ColumnName))));
        foreach (DataRowView drv in view)
            writer.WriteLine(string.Join(",", cols.Select(c => CsvQuote(drv[c.ColumnName]?.ToString() ?? ""))));
    }

    private static string CsvQuote(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
