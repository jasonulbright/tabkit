using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Tabkit.Core.Model;

/// <summary>A <c>&lt;datasource&gt;</c> element.</summary>
public sealed record DataSource(
    string Name,
    string? Caption = null,
    string? Version = null,
    IReadOnlyList<Connection>? Connections = null,
    IReadOnlyList<Field>? Fields = null)
{
    public IReadOnlyList<Connection> Connections { get; init; } =
        Connections ?? ImmutableArray<Connection>.Empty;

    public IReadOnlyList<Field> Fields { get; init; } =
        Fields ?? ImmutableArray<Field>.Empty;

    public IReadOnlyList<Field> CalculatedFields =>
        Fields.Where(f => f.IsCalculated).ToImmutableArray();
}
