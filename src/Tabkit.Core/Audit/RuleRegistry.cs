using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Tabkit.Core.Audit.Packs;
using Tabkit.Core.Model;

namespace Tabkit.Core.Audit;

/// <summary>Holds known rules. Filterable by pack.</summary>
public sealed class RuleRegistry
{
    private readonly List<IRule> _rules = new();

    public IRule Register(IRule rule)
    {
        if (_rules.Any(r => r.Meta.Id == rule.Meta.Id))
            throw new ArgumentException($"rule id '{rule.Meta.Id}' already registered");
        _rules.Add(rule);
        return rule;
    }

    public IReadOnlyList<IRule> Rules => _rules.ToImmutableArray();

    /// <summary>
    /// Distinct pack names from currently-registered rules, plus the synthetic
    /// "all" sentinel. Case-preserving but matched case-insensitively by callers.
    /// </summary>
    public IReadOnlyList<string> KnownPacks =>
        _rules.Select(r => r.Meta.Pack)
              .Where(p => !string.IsNullOrEmpty(p))
              .Select(p => p!)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
              .Prepend("all")
              .ToImmutableArray();

    /// <summary>
    /// Returns the rules belonging to <paramref name="pack"/>. Null or "all"
    /// (case-insensitive) returns every rule. An unknown pack name throws
    /// <see cref="ArgumentException"/> so callers cannot silently run zero rules
    /// against a workbook and report it clean.
    /// </summary>
    public IReadOnlyList<IRule> ForPack(string? pack)
    {
        if (string.IsNullOrEmpty(pack) || string.Equals(pack, "all", StringComparison.OrdinalIgnoreCase))
            return Rules;

        var matched = _rules
            .Where(r => string.Equals(r.Meta.Pack, pack, StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();

        if (matched.IsEmpty)
        {
            var known = string.Join(", ", KnownPacks);
            throw new ArgumentException(
                $"unknown rule pack '{pack}'. Known packs: {known}.",
                nameof(pack));
        }
        return matched;
    }

    public IReadOnlyList<Finding> Run(Workbook wb, string? pack = null)
    {
        var findings = new List<Finding>();
        foreach (var rule in ForPack(pack))
            findings.AddRange(rule.Check(wb));
        return findings;
    }

    /// <summary>Build a registry pre-loaded with the shipped rule packs.</summary>
    /// <param name="governanceConfig">
    /// Optional. Customizes GOV002 (additional allowlisted servers) and GOV003
    /// (disabled PII pattern labels). Null = use defaults.
    /// </param>
    public static RuleRegistry Default(GovernanceConfig? governanceConfig = null)
    {
        var reg = new RuleRegistry();
        foreach (var rule in AuditPack.Rules())
            reg.Register(rule);
        foreach (var rule in GovernancePack.Rules(governanceConfig))
            reg.Register(rule);
        return reg;
    }
}
