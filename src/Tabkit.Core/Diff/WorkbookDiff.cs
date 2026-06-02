using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Tabkit.Core.Diff;

public sealed record WorkbookDiff(
    string APath,
    string BPath,
    IReadOnlyList<Change>? Changes = null)
{
    public IReadOnlyList<Change> Changes { get; init; } =
        Changes ?? ImmutableArray<Change>.Empty;

    public IEnumerable<Change> Added   => Changes.Where(c => c.Kind == ChangeKind.Added);
    public IEnumerable<Change> Removed => Changes.Where(c => c.Kind == ChangeKind.Removed);
    public IEnumerable<Change> Modified => Changes.Where(c => c.Kind == ChangeKind.Modified);

    public string Summary()
    {
        if (Changes.Count == 0) return "no semantic differences";
        var bits = new List<string>();
        foreach (var kind in new[] { ChangeKind.Added, ChangeKind.Removed, ChangeKind.Modified })
        {
            var n = Changes.Count(c => c.Kind == kind);
            if (n > 0) bits.Add($"{n} {kind.AsString()}");
        }
        return string.Join(", ", bits);
    }
}
