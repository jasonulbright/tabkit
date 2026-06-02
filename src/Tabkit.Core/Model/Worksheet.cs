using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Model;

/// <summary>A <c>&lt;worksheet&gt;</c> element.</summary>
public sealed record Worksheet(
    string Name,
    IReadOnlyList<string>? DataSourceNames = null)
{
    public IReadOnlyList<string> DataSourceNames { get; init; } =
        DataSourceNames ?? ImmutableArray<string>.Empty;
}
