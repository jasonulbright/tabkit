using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Audit;

/// <summary>
/// Runtime configuration for governance-pack rules.
/// <para>
/// <c>AllowedServers</c> — hostnames (case-insensitive, exact match) that GOV002
/// should treat as approved in addition to its built-in localhost / loopback /
/// internal.example.com defaults.
/// </para>
/// <para>
/// <c>DisabledPiiPatterns</c> — pattern labels (e.g., <c>"ssn"</c>, <c>"email"</c>,
/// <c>"personal_name"</c>) that GOV003 should skip. Use to opt out of patterns
/// that produce noise in a given org.
/// </para>
/// </summary>
public sealed record GovernanceConfig(
    IReadOnlyList<string> AllowedServers,
    IReadOnlySet<string> DisabledPiiPatterns)
{
    public static GovernanceConfig Default { get; } = new(
        AllowedServers: ImmutableArray<string>.Empty,
        DisabledPiiPatterns: ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase));
}
