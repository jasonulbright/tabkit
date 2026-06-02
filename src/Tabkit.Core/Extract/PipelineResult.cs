using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Extract;

public sealed record PipelineResult(
    string Name,
    int RowsIn,
    int RowsOut,
    double DurationSeconds,
    IReadOnlyList<string> Columns);
