using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Tabkit.Core.Model;

/// <summary>A parsed Tableau workbook.</summary>
public sealed record Workbook(
    string SourcePath,
    string? Version,
    string? SourceBuild,
    string? SourcePlatform,
    IReadOnlyList<DataSource>? DataSources = null,
    IReadOnlyList<Worksheet>? Worksheets = null,
    IReadOnlyList<Dashboard>? Dashboards = null)
{
    public IReadOnlyList<DataSource> DataSources { get; init; } =
        DataSources ?? ImmutableArray<DataSource>.Empty;

    public IReadOnlyList<Worksheet> Worksheets { get; init; } =
        Worksheets ?? ImmutableArray<Worksheet>.Empty;

    public IReadOnlyList<Dashboard> Dashboards { get; init; } =
        Dashboards ?? ImmutableArray<Dashboard>.Empty;

    public IReadOnlySet<string> WorksheetNames =>
        Worksheets.Select(w => w.Name).ToImmutableHashSet();

    public IReadOnlySet<string> ReferencedWorksheets
    {
        get
        {
            var union = new HashSet<string>();
            foreach (var d in Dashboards)
                foreach (var name in d.ReferencedWorksheets)
                    union.Add(name);
            return union;
        }
    }

    public IReadOnlySet<string> UnusedWorksheets
    {
        get
        {
            if (Dashboards.Count == 0)
                return ImmutableHashSet<string>.Empty;
            var refs = ReferencedWorksheets;
            return WorksheetNames.Except(refs).ToImmutableHashSet();
        }
    }
}
