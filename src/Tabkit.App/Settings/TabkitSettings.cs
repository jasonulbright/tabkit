namespace Tabkit.App.Settings;

/// <summary>
/// Persisted user settings. Lives at <c>%APPDATA%\Tabkit\settings.json</c>.
/// Plain POCO so System.Text.Json can round-trip it without a converter.
/// </summary>
public sealed class TabkitSettings
{
    // All properties null-coalesce on set so a settings.json with explicit
    // nulls (e.g. "Gov002AllowedServers": null) can't leave a null behind for
    // downstream code to dereference. System.Text.Json calls the setters.
    private string _theme = "Dark";
    private string _auditOutputDir = "";
    private string _extractOutputDir = "";
    private string _inventoryDbPath = "";
    private List<string> _gov002AllowedServers = new();
    private List<string> _gov003DisabledPiiPatterns = new();
    private List<string> _recentWorkbooks = new();
    private List<string> _recentPipelines = new();

    /// <summary>"Dark" or "Light". Anything else falls back to Dark.</summary>
    public string Theme { get => _theme; set => _theme = value ?? "Dark"; }

    /// <summary>Default dir for Audit export (JSON / SARIF / HTML). Empty = prompt the user each time.</summary>
    public string AuditOutputDir { get => _auditOutputDir; set => _auditOutputDir = value ?? ""; }

    /// <summary>Default dir for Extract sink output if the YAML uses a relative path. Empty = use YAML's parent dir.</summary>
    public string ExtractOutputDir { get => _extractOutputDir; set => _extractOutputDir = value ?? ""; }

    /// <summary>Path to the inventory SQLite. Empty = use <c>%LOCALAPPDATA%\Tabkit\inventory.sqlite</c>.</summary>
    public string InventoryDbPath { get => _inventoryDbPath; set => _inventoryDbPath = value ?? ""; }

    /// <summary>Additional servers GOV002 should treat as approved (case-insensitive exact match).</summary>
    public List<string> Gov002AllowedServers { get => _gov002AllowedServers; set => _gov002AllowedServers = value ?? new(); }

    /// <summary>GOV003 PII pattern labels to skip (e.g., "ssn", "personal_name").</summary>
    public List<string> Gov003DisabledPiiPatterns { get => _gov003DisabledPiiPatterns; set => _gov003DisabledPiiPatterns = value ?? new(); }

    /// <summary>MRU list of workbook paths opened by the Audit page. Max 8 retained.</summary>
    public List<string> RecentWorkbooks { get => _recentWorkbooks; set => _recentWorkbooks = value ?? new(); }

    /// <summary>MRU list of YAML pipeline paths opened by the Extract page. Max 8 retained.</summary>
    public List<string> RecentPipelines { get => _recentPipelines; set => _recentPipelines = value ?? new(); }
}
