using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Model;

/// <summary>A <c>&lt;dashboard&gt;</c> element.</summary>
public sealed record Dashboard(
    string Name,
    IReadOnlyList<Zone>? Zones = null)
{
    public IReadOnlyList<Zone> Zones { get; init; } =
        Zones ?? ImmutableArray<Zone>.Empty;

    public IReadOnlySet<string> ReferencedWorksheets
    {
        get
        {
            var refs = new HashSet<string>();
            foreach (var z in Zones)
                Walk(z, refs);
            return refs;
        }
    }

    private static void Walk(Zone z, HashSet<string> refs)
    {
        if (!string.IsNullOrEmpty(z.WorksheetRef))
            refs.Add(z.WorksheetRef);
        foreach (var child in z.Children)
            Walk(child, refs);
    }
}
