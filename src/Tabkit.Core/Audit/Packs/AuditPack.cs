using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tabkit.Core.Model;

namespace Tabkit.Core.Audit.Packs;

/// <summary>
/// Audit pack — workbook health, hygiene, technical debt.
/// Rule IDs use the <c>AUD###</c> prefix.
/// </summary>
public static partial class AuditPack
{
    public static IEnumerable<IRule> Rules() => new IRule[]
    {
        new UnusedWorksheets(),
        new OrphanDataSources(),
        new DeprecatedFunctions(),
        new DuplicateCalculatedFields(),
        new BrokenColumnReferences(),
        new OldWorkbookVersion(),
    };

    /// <summary>Functions Tableau has deprecated or marked legacy. Conservative starter list.</summary>
    public static readonly Dictionary<string, string> DeprecatedFunctionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ATTR"] = "Wrap explicit aggregations (MIN/MAX) when you need a single value per partition.",
        ["WINDOW_VAR"] = "Use VAR with LOD expressions for clarity.",
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\[[^\[\]\r\n]+\]")]
    private static partial Regex ColumnRef();

    internal static string NormalizeFormula(string s)
        => WhitespaceRun().Replace(s.Trim(), " ").ToLowerInvariant();

    internal static HashSet<string> ColumnRefsIn(string formula)
        => new(ColumnRef().Matches(formula).Select(m => m.Value));


    // ── AUD001 ────────────────────────────────────────────────────────────────
    public sealed class UnusedWorksheets : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "AUD001",
            Title: "Unused worksheet",
            Severity: Severity.Warn,
            Description: "Worksheet exists in the workbook but is not placed on any dashboard.",
            Pack: "audit");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            if (wb.Dashboards.Count == 0) yield break;
            foreach (var wsName in wb.UnusedWorksheets.OrderBy(n => n, StringComparer.Ordinal))
            {
                yield return NewFinding(
                    wb,
                    message: $"Worksheet '{wsName}' is not referenced by any dashboard.",
                    location: $"worksheet:{wsName}",
                    remediation: "Delete the worksheet if it's no longer needed, or add it to a dashboard.");
            }
        }
    }

    // ── AUD002 ────────────────────────────────────────────────────────────────
    public sealed class OrphanDataSources : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "AUD002",
            Title: "Orphan data source",
            Severity: Severity.Warn,
            Description: "Data source defined but no worksheet uses it.",
            Pack: "audit");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            var used = new HashSet<string>(wb.Worksheets.SelectMany(ws => ws.DataSourceNames));
            foreach (var ds in wb.DataSources)
            {
                if (!used.Contains(ds.Name))
                {
                    yield return NewFinding(
                        wb,
                        message: $"Data source '{ds.Name}' is defined but no worksheet references it.",
                        location: $"datasource:{ds.Name}",
                        remediation: "Remove the data source, or wire it into a worksheet.");
                }
            }
        }
    }

    // ── AUD003 ────────────────────────────────────────────────────────────────
    public sealed class DeprecatedFunctions : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "AUD003",
            Title: "Deprecated function in calculated field",
            Severity: Severity.Warn,
            Description: "Calculated field uses a function Tableau has marked deprecated.",
            Pack: "audit");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
                foreach (var fld in ds.CalculatedFields)
                {
                    if (fld.Calculation is null) continue;
                    var formula = fld.Calculation.Formula;
                    foreach (var (fn, hint) in DeprecatedFunctionMap)
                    {
                        var pattern = new Regex($@"\b{Regex.Escape(fn)}\s*\(", RegexOptions.IgnoreCase);
                        if (pattern.IsMatch(formula))
                        {
                            yield return NewFinding(
                                wb,
                                message: $"Calculated field '{fld.Name}' on '{ds.Name}' uses deprecated function {fn}().",
                                location: $"datasource:{ds.Name}/field:{fld.Name}",
                                remediation: hint,
                                properties: ("function", fn));
                        }
                    }
                }
        }
    }

    // ── AUD004 ────────────────────────────────────────────────────────────────
    public sealed class DuplicateCalculatedFields : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "AUD004",
            Title: "Duplicate calculated field across data sources",
            Severity: Severity.Info,
            Description: "Same calculation formula appears in multiple data sources.",
            Pack: "audit");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            var seen = new Dictionary<string, List<(string Ds, string Field)>>();
            foreach (var ds in wb.DataSources)
                foreach (var fld in ds.CalculatedFields)
                {
                    if (fld.Calculation is null) continue;
                    var key = NormalizeFormula(fld.Calculation.Formula);
                    if (!seen.TryGetValue(key, out var list))
                        seen[key] = list = new();
                    list.Add((ds.Name, fld.Name));
                }

            foreach (var kvp in seen)
            {
                var occ = kvp.Value;
                if (occ.Count <= 1) continue;
                var pretty = string.Join(", ", occ.Select(x => $"{x.Ds}:{x.Field}"));
                yield return NewFinding(
                    wb,
                    message: $"Identical calculation appears in {occ.Count} places: {pretty}.",
                    location: $"{occ[0].Ds}/{occ[0].Field}",
                    remediation: "Consider promoting to a published data source or a parameter.",
                    properties: ("count", occ.Count.ToString()));
            }
        }
    }

    // ── AUD005 ────────────────────────────────────────────────────────────────
    public sealed class BrokenColumnReferences : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "AUD005",
            Title: "Calculated field references unknown column",
            Severity: Severity.Error,
            Description: "Calculation references a column name that doesn't exist on the same data source.",
            Pack: "audit");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
            {
                var known = new HashSet<string>(ds.Fields.Select(f => f.Name));
                foreach (var fld in ds.CalculatedFields)
                {
                    if (fld.Calculation is null) continue;
                    var refs = ColumnRefsIn(fld.Calculation.Formula);
                    foreach (var r in refs)
                    {
                        if (r == fld.Name) continue;
                        if (known.Contains(r)) continue;
                        yield return NewFinding(
                            wb,
                            message: $"Calc '{fld.Name}' references column '{r}' which does not exist on data source '{ds.Name}'.",
                            location: $"datasource:{ds.Name}/field:{fld.Name}",
                            remediation:
                                "Rename the reference or restore the deleted column. " +
                                "Tableau Desktop will show this as a broken calc.",
                            properties: ("referenced", r));
                    }
                }
            }
        }
    }

    // ── AUD006 ────────────────────────────────────────────────────────────────
    public sealed class OldWorkbookVersion : RuleBase
    {
        public const double MinVersion = 18.0;

        public override RuleMeta Meta { get; } = new(
            Id: "AUD006",
            Title: "Workbook saved by a very old Tableau version",
            Severity: Severity.Info,
            Description: "Workbook XML version predates the supported floor (18.x).",
            Pack: "audit");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            if (wb.Version is null) yield break;
            if (!double.TryParse(wb.Version, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                yield break;
            if (v < MinVersion)
            {
                yield return NewFinding(
                    wb,
                    message: $"Workbook version is '{wb.Version}'; current floor is {MinVersion}.",
                    location: "workbook",
                    remediation: "Open in a recent Tableau Desktop release and re-save.",
                    properties: ("version", wb.Version));
            }
        }
    }
}
