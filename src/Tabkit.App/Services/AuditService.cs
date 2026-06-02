using Tabkit.Core.Audit;
using Tabkit.Core.Model;

namespace Tabkit.App.Services;

/// <summary>
/// Wraps <see cref="RuleRegistry"/> with the live user settings applied
/// (GOV002 allowlist, GOV003 disabled patterns). Rebuilds the registry on
/// any settings change so audits always reflect the latest config.
/// </summary>
public sealed class AuditService
{
    private readonly SettingsService _settings;
    private RuleRegistry _registry;

    public AuditService(SettingsService settings)
    {
        _settings = settings;
        _registry = RuleRegistry.Default(_settings.BuildGovernanceConfig());
        _settings.Changed += (_, __) =>
            _registry = RuleRegistry.Default(_settings.BuildGovernanceConfig());
    }

    public IReadOnlyList<IRule> Rules => _registry.Rules;

    public IReadOnlyList<Finding> Run(Workbook wb, string? pack = null)
    {
        // Snapshot the registry reference for symmetry with RunAsync.
        var registry = _registry;
        return registry.Run(wb, pack);
    }

    public Task<IReadOnlyList<Finding>> RunAsync(
        Workbook wb,
        string? pack = null,
        CancellationToken ct = default)
    {
        // Snapshot the registry reference BEFORE Task.Run. The Changed handler
        // swaps the _registry field on a settings save; capturing a local pins
        // the rule set that was current when the audit started, so a mid-run
        // save can't replace it between workbook load and rule execution.
        var registry = _registry;
        return Task.Run(() => registry.Run(wb, pack), ct);
    }
}
