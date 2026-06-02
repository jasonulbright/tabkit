using Microsoft.Win32;
using Tabkit.App.Services;
using Tabkit.App.Settings;
using Tabkit.Core.Audit.Packs;
using Wpf.Ui.Appearance;

namespace Tabkit.App.ViewModels.Pages;

public partial class PiiPatternToggle : ObservableObject
{
    public string Label { get; }

    public PiiPatternToggle(string label, bool isEnabled)
    {
        Label = label;
        _isEnabled = isEnabled;
    }

    [ObservableProperty] private bool _isEnabled;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;

    public SettingsViewModel(SettingsService settings, ISnackbarService snackbar)
    {
        _settings = settings;
        _snackbar = snackbar;
        SettingsPath = settings.SettingsPath;
        PiiPatterns = new ObservableCollection<PiiPatternToggle>();
        // Populate from what the service already loaded at startup — don't force
        // another disk read here.
        PopulateFromCurrent();
    }

    [ObservableProperty] private string _settingsPath;
    [ObservableProperty] private string _theme = "Dark";
    [ObservableProperty] private string _auditOutputDir = "";
    [ObservableProperty] private string _extractOutputDir = "";
    [ObservableProperty] private string _inventoryDbPath = "";
    [ObservableProperty] private string _gov002AllowedServersText = "";
    [ObservableProperty] private string _statusMessage = "Loaded.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleSave))]
    private bool _isDirty;

    // Visibility mirror for hide-don't-disable. Save is collapsed when nothing
    // has changed since the last persist.
    public bool IsVisibleSave => CanSave();
    private bool CanSave() => IsDirty;

    public IReadOnlyList<string> Themes { get; } = new[] { "Dark", "Light" };
    public ObservableCollection<PiiPatternToggle> PiiPatterns { get; }

    partial void OnThemeChanged(string value)
    {
        ApplyTheme(value);
        IsDirty = true;
    }
    partial void OnAuditOutputDirChanged(string value) => IsDirty = true;
    partial void OnExtractOutputDirChanged(string value) => IsDirty = true;
    partial void OnInventoryDbPathChanged(string value) => IsDirty = true;
    partial void OnGov002AllowedServersTextChanged(string value) => IsDirty = true;
    partial void OnIsDirtyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Reload()
    {
        // Reload re-reads from disk, discarding unsaved edits. A missing file
        // resets to defaults (SettingsService.Load handles that), so this is a
        // true reload, not just a re-read of the in-memory copy.
        _settings.Load();
        PopulateFromCurrent();
        StatusMessage = $"Reloaded from {SettingsPath}.";
    }

    /// <summary>Populate the editable fields from <c>_settings.Current</c> (no disk I/O).</summary>
    private void PopulateFromCurrent()
    {
        var s = _settings.Current;
        Theme = string.IsNullOrEmpty(s.Theme) ? "Dark" : s.Theme;
        AuditOutputDir = s.AuditOutputDir;
        ExtractOutputDir = s.ExtractOutputDir;
        InventoryDbPath = s.InventoryDbPath;
        Gov002AllowedServersText = string.Join(Environment.NewLine, s.Gov002AllowedServers);

        var disabled = new HashSet<string>(s.Gov003DisabledPiiPatterns, StringComparer.OrdinalIgnoreCase);
        PiiPatterns.Clear();
        foreach (var label in GovernancePack.PiiColumnPatterns.AllPatternLabels)
        {
            var toggle = new PiiPatternToggle(label, !disabled.Contains(label));
            toggle.PropertyChanged += (_, __) => IsDirty = true;
            PiiPatterns.Add(toggle);
        }
        IsDirty = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var settings = new TabkitSettings
        {
            Theme = Theme,
            AuditOutputDir = AuditOutputDir,
            ExtractOutputDir = ExtractOutputDir,
            InventoryDbPath = InventoryDbPath,
            Gov002AllowedServers = SplitLines(Gov002AllowedServersText),
            Gov003DisabledPiiPatterns = PiiPatterns
                .Where(p => !p.IsEnabled)
                .Select(p => p.Label)
                .ToList(),
            // The Settings page doesn't manage MRU lists (Audit/Extract pages do),
            // so carry the existing recents forward — otherwise Save wipes them.
            RecentWorkbooks = new List<string>(_settings.Current.RecentWorkbooks),
            RecentPipelines = new List<string>(_settings.Current.RecentPipelines),
        };

        try
        {
            _settings.Save(settings);
            IsDirty = false;
            StatusMessage = $"Saved to {SettingsPath}.";
            Snack("Settings saved", "Audit re-runs will use the new config.", danger: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            Snack("Save failed", ex.Message, danger: true);
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        Theme = "Dark";
        AuditOutputDir = "";
        ExtractOutputDir = "";
        InventoryDbPath = "";
        Gov002AllowedServersText = "";
        foreach (var p in PiiPatterns) p.IsEnabled = true;
        IsDirty = true;
        StatusMessage = "Reset to defaults — click Save to persist.";
    }

    [RelayCommand]
    private void PickAuditOutputDir() => AuditOutputDir = PickFolder("Audit export default dir", AuditOutputDir) ?? AuditOutputDir;

    [RelayCommand]
    private void PickExtractOutputDir() => ExtractOutputDir = PickFolder("Extract sink default dir", ExtractOutputDir) ?? ExtractOutputDir;

    [RelayCommand]
    private void PickInventoryDb()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Inventory database",
            Filter = "SQLite (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            FileName = string.IsNullOrEmpty(InventoryDbPath) ? "inventory.sqlite" : System.IO.Path.GetFileName(InventoryDbPath),
            InitialDirectory = string.IsNullOrEmpty(InventoryDbPath) ? null : System.IO.Path.GetDirectoryName(InventoryDbPath),
            OverwritePrompt = false,
            CheckFileExists = false,
        };
        if (dlg.ShowDialog() == true) InventoryDbPath = dlg.FileName;
    }

    private static string? PickFolder(string title, string initial)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = string.IsNullOrEmpty(initial) ? null : initial,
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private static void ApplyTheme(string theme)
    {
        var target = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(target);
    }

    private static List<string> SplitLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Snack(string title, string message, bool danger)
    {
        var appearance = danger ? ControlAppearance.Danger : ControlAppearance.Success;
        var icon = danger ? SymbolRegular.ErrorCircle24 : SymbolRegular.Checkmark24;
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(4));
    }
}
