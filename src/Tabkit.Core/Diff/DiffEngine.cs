using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Tabkit.Core.Model;
using Tabkit.Core.Normalization;

namespace Tabkit.Core.Diff;

/// <summary>
/// Semantic + canonical-XML diff of two workbooks.
///
/// Tableau .twb XML reorders elements on save, making git diff unreadable.
/// This engine compares the parsed model AND emits a unified diff over the
/// canonicalized XML so users get both signals.
/// </summary>
public static class DiffEngine
{
    public static WorkbookDiff DiffWorkbooks(Workbook a, Workbook b)
    {
        var changes = new List<Change>();
        changes.AddRange(DiffDataSources(a, b));
        changes.AddRange(DiffWorksheets(a, b));
        changes.AddRange(DiffDashboards(a, b));
        return new WorkbookDiff(a.SourcePath, b.SourcePath, changes);
    }

    public static string CanonicalUnifiedDiff(string aPath, string bPath, int contextLines = 3)
    {
        var aText = XmlNormalizer.CanonicalizeToText(ExtractTwbBytes(aPath));
        var bText = XmlNormalizer.CanonicalizeToText(ExtractTwbBytes(bPath));
        if (aText == bText) return string.Empty;

        var builder = new InlineDiffBuilder(new Differ());
        var model = builder.BuildDiffModel(aText, bText, ignoreWhitespace: false);

        var sb = new System.Text.StringBuilder();
        sb.Append("--- ").AppendLine(aPath);
        sb.Append("+++ ").AppendLine(bPath);

        foreach (var line in model.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted: sb.Append('+').AppendLine(line.Text); break;
                case ChangeType.Deleted:  sb.Append('-').AppendLine(line.Text); break;
                case ChangeType.Modified: sb.Append('~').AppendLine(line.Text); break;
                default: sb.Append(' ').AppendLine(line.Text); break;
            }
            _ = contextLines; // currently unused — DiffPlex InlineDiffBuilder emits the full diff
        }
        return sb.ToString();
    }

    private static byte[] ExtractTwbBytes(string path)
    {
        var suffix = Path.GetExtension(path).ToLowerInvariant();
        if (suffix == ".twb")
            return File.ReadAllBytes(path);
        if (suffix == ".twbx")
        {
            using var ms = Tabkit.Core.Loading.TwbxSafety.OpenTwbEntry(path);
            return ms.ToArray();
        }
        throw new System.InvalidOperationException($"unsupported extension: {suffix}");
    }

    // ─── semantic diff helpers ────────────────────────────────────────────────

    private static IEnumerable<Change> DiffDataSources(Workbook a, Workbook b)
    {
        var aMap = a.DataSources.ToDictionary(ds => ds.Name);
        var bMap = b.DataSources.ToDictionary(ds => ds.Name);

        foreach (var name in bMap.Keys.Except(aMap.Keys))
            yield return new Change(ChangeKind.Added, "datasource", name);
        foreach (var name in aMap.Keys.Except(bMap.Keys))
            yield return new Change(ChangeKind.Removed, "datasource", name);

        foreach (var name in aMap.Keys.Intersect(bMap.Keys))
            foreach (var c in DiffOneDataSource(aMap[name], bMap[name]))
                yield return c;
    }

    private static IEnumerable<Change> DiffOneDataSource(DataSource a, DataSource b)
    {
        // Connections matched by (class, server, dbname, filename)
        var aConns = a.Connections.ToDictionary(ConnKey);
        var bConns = b.Connections.ToDictionary(ConnKey);
        foreach (var key in bConns.Keys.Except(aConns.Keys))
            yield return new Change(ChangeKind.Added, "connection", $"{a.Name}::{key}");
        foreach (var key in aConns.Keys.Except(bConns.Keys))
            yield return new Change(ChangeKind.Removed, "connection", $"{a.Name}::{key}");

        // Fields matched by name
        var aFields = a.Fields.ToDictionary(f => f.Name);
        var bFields = b.Fields.ToDictionary(f => f.Name);
        foreach (var name in bFields.Keys.Except(aFields.Keys))
            yield return new Change(ChangeKind.Added, "field", $"{a.Name}/{name}");
        foreach (var name in aFields.Keys.Except(bFields.Keys))
            yield return new Change(ChangeKind.Removed, "field", $"{a.Name}/{name}");
        foreach (var name in aFields.Keys.Intersect(bFields.Keys))
        {
            var fa = aFields[name];
            var fb = bFields[name];
            if (fa != fb)
                yield return new Change(ChangeKind.Modified, "field", $"{a.Name}/{name}", FieldChangeDetail(fa, fb));
        }
    }

    private static IEnumerable<Change> DiffWorksheets(Workbook a, Workbook b)
    {
        var aSet = a.Worksheets.Select(w => w.Name).ToHashSet();
        var bSet = b.Worksheets.Select(w => w.Name).ToHashSet();
        foreach (var name in bSet.Except(aSet))
            yield return new Change(ChangeKind.Added, "worksheet", name);
        foreach (var name in aSet.Except(bSet))
            yield return new Change(ChangeKind.Removed, "worksheet", name);
    }

    private static IEnumerable<Change> DiffDashboards(Workbook a, Workbook b)
    {
        var aMap = a.Dashboards.ToDictionary(d => d.Name);
        var bMap = b.Dashboards.ToDictionary(d => d.Name);
        foreach (var name in bMap.Keys.Except(aMap.Keys))
            yield return new Change(ChangeKind.Added, "dashboard", name);
        foreach (var name in aMap.Keys.Except(bMap.Keys))
            yield return new Change(ChangeKind.Removed, "dashboard", name);
        foreach (var name in aMap.Keys.Intersect(bMap.Keys))
        {
            var oldRef = aMap[name].ReferencedWorksheets.OrderBy(x => x).ToList();
            var newRef = bMap[name].ReferencedWorksheets.OrderBy(x => x).ToList();
            if (!oldRef.SequenceEqual(newRef))
            {
                var detail = $"worksheet set changed: [{string.Join(", ", oldRef)}] → [{string.Join(", ", newRef)}]";
                yield return new Change(ChangeKind.Modified, "dashboard", name, detail);
            }
        }
    }

    private static string ConnKey(Tabkit.Core.Model.Connection c)
        => string.Join("|", new[] { c.ConnectionClass, c.Server, c.Dbname, c.Filename });

    private static string FieldChangeDetail(Field a, Field b)
    {
        var bits = new List<string>();
        if (a.Datatype != b.Datatype) bits.Add($"datatype '{a.Datatype}'→'{b.Datatype}'");
        if (a.Role     != b.Role)     bits.Add($"role '{a.Role}'→'{b.Role}'");
        if ((a.Calculation is null) != (b.Calculation is null))
            bits.Add(b.Calculation is not null ? "calculation added" : "calculation removed");
        else if (a.Calculation is not null && b.Calculation is not null &&
                 a.Calculation.Formula != b.Calculation.Formula)
            bits.Add("calculation formula changed");
        return bits.Count == 0 ? "fields differ" : string.Join("; ", bits);
    }
}
