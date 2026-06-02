using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Model;

/// <summary>
/// A dashboard layout zone. Zones nest recursively — <c>Children</c> holds
/// child zones for layout containers.
/// </summary>
public sealed record Zone(
    string? Id,
    string? ZoneType,
    string? Name = null,
    string? WorksheetRef = null,
    IReadOnlyList<Zone>? Children = null)
{
    public IReadOnlyList<Zone> Children { get; init; } =
        Children ?? ImmutableArray<Zone>.Empty;
}
