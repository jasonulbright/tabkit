using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using Tabkit.App.Settings;
using Tabkit.Core.Audit;

namespace Tabkit.App.Services;

/// <summary>
/// Loads, saves, and broadcasts <see cref="TabkitSettings"/>. Anything that
/// depends on user settings should subscribe to <see cref="Changed"/> and
/// re-read on each event, OR call <see cref="BuildGovernanceConfig"/> on every
/// audit run to pick up the latest values.
/// </summary>
public sealed class SettingsService
{
    public const string DirName = "Tabkit";
    public const string FileName = "settings.json";

    public string SettingsPath { get; }

    public TabkitSettings Current { get; private set; } = new();

    /// <summary>Raised after <see cref="Save"/> or <see cref="Load"/>.</summary>
    public event EventHandler? Changed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DirName);
        SettingsPath = Path.Combine(dir, FileName);
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<TabkitSettings>(json, JsonOpts);
                Current = loaded ?? new TabkitSettings();
            }
            else
            {
                // No file on disk → defaults. Previously this left whatever was
                // already in memory, so a deleted settings file left stale values.
                Current = new TabkitSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings should never crash the app — fall back
            // to defaults and let the user re-save from the UI.
            Current = new TabkitSettings();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save(TabkitSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(SettingsPath, json);
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Project the current settings into a Core <see cref="GovernanceConfig"/>.</summary>
    public GovernanceConfig BuildGovernanceConfig()
    {
        var allowed = Current.Gov002AllowedServers
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToImmutableArray();
        var disabled = Current.Gov003DisabledPiiPatterns
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        return new GovernanceConfig(allowed, disabled);
    }
}
