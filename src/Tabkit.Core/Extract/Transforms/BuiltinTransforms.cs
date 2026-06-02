using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace Tabkit.Core.Extract.Transforms;

internal static class DtypeMap
{
    public static Type Resolve(string name) => name.ToLowerInvariant() switch
    {
        "int8"    => typeof(sbyte),
        "int16"   => typeof(short),
        "int32"   => typeof(int),
        "int64"   => typeof(long),
        "uint8"   => typeof(byte),
        "uint16"  => typeof(ushort),
        "uint32"  => typeof(uint),
        "uint64"  => typeof(ulong),
        "float32" => typeof(float),
        "float64" => typeof(double),
        "bool" or "boolean" => typeof(bool),
        "string" or "str" or "utf8" => typeof(string),
        "date" or "datetime" => typeof(DateTime),
        _ => throw new ArgumentException($"unknown dtype '{name}'"),
    };
}

public sealed class CastTransform : ITransform
{
    public Dictionary<string, string> Columns { get; init; } = new();

    public DataTable Apply(DataTable table)
    {
        // DataTable doesn't allow changing column type with data present —
        // build a sibling table, copy with conversion.
        var dst = table.Clone();
        foreach (var (col, dtype) in Columns)
        {
            var idx = dst.Columns.IndexOf(col);
            if (idx < 0) continue;
            dst.Columns[idx]!.DataType = DtypeMap.Resolve(dtype);
        }
        foreach (DataRow src in table.Rows)
        {
            var newRow = dst.NewRow();
            foreach (DataColumn c in dst.Columns)
            {
                var srcVal = src[c.ColumnName];
                if (srcVal is null || srcVal is DBNull)
                {
                    newRow[c.ColumnName] = DBNull.Value;
                    continue;
                }
                newRow[c.ColumnName] = Convert.ChangeType(srcVal, c.DataType, CultureInfo.InvariantCulture);
            }
            dst.Rows.Add(newRow);
        }
        return dst;
    }
}

public sealed class RenameTransform : ITransform
{
    public Dictionary<string, string> Columns { get; init; } = new();

    public DataTable Apply(DataTable table)
    {
        foreach (var (oldName, newName) in Columns)
        {
            if (table.Columns.Contains(oldName))
                table.Columns[oldName]!.ColumnName = newName;
        }
        return table;
    }
}

public sealed class SelectTransform : ITransform
{
    public List<string> Columns { get; init; } = new();

    public DataTable Apply(DataTable table)
    {
        var keep = new HashSet<string>(Columns, StringComparer.Ordinal);
        for (var i = table.Columns.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(table.Columns[i].ColumnName))
                table.Columns.RemoveAt(i);
        }
        return table;
    }
}

public sealed class DropTransform : ITransform
{
    public List<string> Columns { get; init; } = new();

    public DataTable Apply(DataTable table)
    {
        foreach (var c in Columns)
        {
            if (table.Columns.Contains(c))
                table.Columns.Remove(c);
        }
        return table;
    }
}

public sealed class FilterClause
{
    public string Col { get; set; } = "";
    public string Op { get; set; } = "";
    public object? Value { get; set; }
}

public sealed class FilterTransform : ITransform
{
    public List<FilterClause> Where { get; init; } = new();

    private static readonly HashSet<string> Ops = new(StringComparer.Ordinal)
    {
        "==", "!=", ">", ">=", "<", "<=", "in", "not_in",
    };

    /// <summary>Allowed operator strings, exposed so <c>Pipeline.Validate</c> can pre-check.</summary>
    public static IReadOnlyCollection<string> AllowedOps => Ops;

    public static bool IsValidOp(string op) => Ops.Contains(op);

    /// <summary>
    /// Canonical shape rule for a membership (<c>in</c> / <c>not_in</c>) value:
    /// it must be a non-string YAML list whose every element is a scalar.
    /// Throws <see cref="ArgumentException"/> on any other shape (null, scalar
    /// string, map, nested list, or a list containing maps/lists). Returns the
    /// validated elements so callers don't re-enumerate. Used by both
    /// <c>Pipeline.Validate</c> (validate time) and <see cref="Compare"/>
    /// (runtime, defense-in-depth) so the two never drift.
    /// </summary>
    public static IReadOnlyList<object?> ValidateMembershipValue(string op, string col, object? value)
    {
        var where = string.IsNullOrEmpty(col) ? $"'{op}'" : $"filter clause '{col} {op}'";

        if (value is null)
            throw new ArgumentException($"{where} value must be a list");
        // string is IEnumerable<char> — iterating it matches individual letters.
        if (value is string s)
            throw new ArgumentException(
                $"{where} value must be a list, not a scalar string '{s}'. Use [a, b] instead.");
        // Dictionary<,> satisfies IEnumerable, so guard maps explicitly.
        if (value is System.Collections.IDictionary)
            throw new ArgumentException($"{where} value must be a list, not a map.");
        if (value is not System.Collections.IEnumerable seq)
            throw new ArgumentException($"{where} value must be a list");

        var elements = new List<object?>();
        foreach (var e in seq)
        {
            // Each element must be a scalar — reject nested lists/maps like [[alice]].
            if (e is not null && e is not string &&
                (e is System.Collections.IDictionary || e is System.Collections.IEnumerable))
                throw new ArgumentException(
                    $"{where} list elements must be scalars, not nested lists or maps.");
            elements.Add(e);
        }
        if (elements.Count == 0)
            throw new ArgumentException($"{where} list must contain at least one element");
        return elements;
    }

    public DataTable Apply(DataTable table)
    {
        if (Where.Count == 0) return table;

        var filtered = table.Clone();
        foreach (DataRow row in table.Rows)
        {
            if (MatchesAll(row)) filtered.ImportRow(row);
        }
        return filtered;
    }

    private bool MatchesAll(DataRow row)
    {
        foreach (var clause in Where)
        {
            if (string.IsNullOrEmpty(clause.Col))
                throw new ArgumentException("filter clause needs 'col'");
            if (string.IsNullOrEmpty(clause.Op))
                throw new ArgumentException("filter clause needs 'op'");
            if (!Ops.Contains(clause.Op))
                throw new ArgumentException(
                    $"unsupported op '{clause.Op}' (allowed: {string.Join(", ", Ops)})");

            var cell = row[clause.Col];
            if (!Compare(cell, clause.Op, clause.Value, clause.Col))
                return false;
        }
        return true;
    }

    private static bool Compare(object cell, string op, object? value, string col = "")
    {
        if (op is "in" or "not_in")
        {
            // Single source of truth for the membership shape rule — rejects
            // scalar strings, maps, nested lists, and non-scalar elements
            // (Codex R1 findings #3 + round-4). Same helper runs at validate
            // time so the two paths never disagree.
            var elements = FilterTransform.ValidateMembershipValue(op, col, value);
            var contains = false;
            foreach (var v in elements)
                if (Equal(cell, v)) { contains = true; break; }
            return op == "in" ? contains : !contains;
        }

        // Defense-in-depth for `==`/`!=`/`>`/`>=`/`<`/`<=`: a list/map value
        // would stringify via CompareValues and silently filter every row
        // out. Pipeline.Validate rejects this at validate time too.
        if (value is not null
            && value is not string
            && (value is System.Collections.IDictionary
                || value is System.Collections.IEnumerable))
            throw new ArgumentException(
                $"'{op}' value must be a scalar, not a list or map. " +
                "Use 'in' / 'not_in' for list values.");

        var cmp = CompareValues(cell, value);
        return op switch
        {
            "==" => cmp == 0,
            "!=" => cmp != 0,
            ">"  => cmp > 0,
            ">=" => cmp >= 0,
            "<"  => cmp < 0,
            "<=" => cmp <= 0,
            _ => throw new InvalidOperationException("unreachable"),
        };
    }

    private static bool Equal(object cell, object? v) => CompareValues(cell, v) == 0;

    private static int CompareValues(object cell, object? value)
    {
        if (cell is DBNull && value is null) return 0;
        if (cell is DBNull) return -1;
        if (value is null) return 1;

        // 1. Try numeric first — handles the common CSV-strings-with-numeric-meaning case.
        if (double.TryParse(cell.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ld) &&
            double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rd))
            return ld.CompareTo(rd);

        // 2. Try date.
        if (DateTime.TryParse(cell.ToString(), CultureInfo.InvariantCulture,
                              DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                              out var ldt) &&
            DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture,
                              DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                              out var rdt))
            return ldt.CompareTo(rdt);

        // 3. Native CompareTo when the types match.
        if (cell is IComparable c && c.GetType() == value.GetType())
            return c.CompareTo(value);

        // 4. Last-resort string compare.
        return string.Compare(cell.ToString(), value.ToString(), StringComparison.Ordinal);
    }
}
