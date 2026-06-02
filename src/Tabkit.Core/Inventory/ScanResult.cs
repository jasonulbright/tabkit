using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Inventory;

public sealed record ScanResult(
    int Scanned,
    int Indexed,
    int Skipped,
    IReadOnlyList<(string Path, string Message)> Errors)
{
    public static ScanResult Empty { get; } =
        new(0, 0, 0, ImmutableArray<(string, string)>.Empty);
}
