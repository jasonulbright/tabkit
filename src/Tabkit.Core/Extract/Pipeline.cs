using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Tabkit.Core.Extract.Sinks;
using Tabkit.Core.Extract.Sources;
using Tabkit.Core.Extract.Transforms;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tabkit.Core.Extract;

/// <summary>
/// A pipeline is one source → N transforms → one sink. YAML in, run, exit.
/// No scheduling. No daemon. No state.
/// </summary>
public sealed class Pipeline
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";
    [YamlMember(Alias = "source")]
    public Dictionary<string, object?> Source { get; set; } = new();
    [YamlMember(Alias = "transforms")]
    public List<Dictionary<string, object?>> Transforms { get; set; } = new();
    [YamlMember(Alias = "sink")]
    public Dictionary<string, object?> Sink { get; set; } = new();

    public static Pipeline FromYaml(string text)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        var raw = deserializer.Deserialize<Pipeline>(text)
                  ?? throw new ArgumentException("pipeline YAML deserialized to null");
        if (string.IsNullOrEmpty(raw.Name))
            throw new ArgumentException("pipeline needs 'name'");
        if (raw.Source is null || raw.Source.Count == 0)
            throw new ArgumentException("pipeline needs 'source'");
        if (raw.Sink is null || raw.Sink.Count == 0)
            throw new ArgumentException("pipeline needs 'sink'");
        return raw;
    }

    public static Pipeline FromFile(string path) => FromYaml(File.ReadAllText(path));

    /// <summary>
    /// Full semantic validation. Beyond <see cref="FromYaml"/>'s top-level
    /// shape check, this constructs every source / transform / sink the
    /// pipeline names (without reading or writing any data) so unknown
    /// types, missing required keys, unsupported ops, and obvious value-shape
    /// mistakes (e.g. scalar string for a filter `in` clause) are caught at
    /// `extract validate` time instead of surfacing as wrong-looking output
    /// at run time.
    /// </summary>
    public static Pipeline Validate(string yamlText)
    {
        var p = FromYaml(yamlText);
        BuildSource(p.Source);
        foreach (var spec in p.Transforms)
        {
            var transform = BuildTransform(spec);
            if (transform is FilterTransform ft) ValidateFilterClauses(ft);
        }
        BuildSink(p.Sink);
        return p;
    }

    public static Pipeline ValidateFile(string path) => Validate(File.ReadAllText(path));

    private static void ValidateFilterClauses(FilterTransform ft)
    {
        foreach (var clause in ft.Where)
        {
            if (string.IsNullOrEmpty(clause.Col))
                throw new ArgumentException("filter clause needs 'col'");
            if (string.IsNullOrEmpty(clause.Op))
                throw new ArgumentException("filter clause needs 'op'");
            if (!FilterTransform.IsValidOp(clause.Op))
                throw new ArgumentException(
                    $"unsupported filter op '{clause.Op}' " +
                    $"(allowed: {string.Join(", ", FilterTransform.AllowedOps)})");

            if (clause.Op is "in" or "not_in")
            {
                // Delegate to the canonical membership-shape rule so validate
                // time and runtime never drift (Codex R1 round-4: maps and
                // nested lists used to satisfy the bare IEnumerable check).
                FilterTransform.ValidateMembershipValue(clause.Op, clause.Col, clause.Value);
            }
            else
            {
                // Round-2 left list-shape checks only on in/not_in, so
                // `op: == value: [alice]` validated OK and filtered to 0 rows.
                if (!IsScalar(clause.Value))
                    throw new ArgumentException(
                        $"filter clause '{clause.Col} {clause.Op}' value must be a scalar " +
                        $"(got {DescribeShape(clause.Value)}). " +
                        "Use 'in' / 'not_in' for list values.");
            }
        }
    }

    public PipelineResult Run()
    {
        var sw = Stopwatch.StartNew();

        var source = BuildSource(Source);
        var table = source.Read();
        var rowsIn = table.Rows.Count;

        foreach (var spec in Transforms)
            table = BuildTransform(spec).Apply(table);

        BuildSink(Sink).Write(table);

        sw.Stop();
        var cols = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        return new PipelineResult(
            Name: Name,
            RowsIn: rowsIn,
            RowsOut: table.Rows.Count,
            DurationSeconds: sw.Elapsed.TotalSeconds,
            Columns: cols);
    }

    private static ISource BuildSource(Dictionary<string, object?> spec)
    {
        var kind = spec.TryGetValue("type", out var t) ? t?.ToString() : null;
        return kind switch
        {
            "csv"     => new CsvSource     { Path = Required(spec, "path"), HasHeader = OptBool(spec, "has_header", true), Separator = OptStr(spec, "separator", ","), Encoding = OptStr(spec, "encoding", "utf8") },
            "parquet" => new ParquetSource { Path = Required(spec, "path") },
            "mssql"   => BuildMssqlSource(spec),
            _ => throw new ArgumentException($"unknown source type: '{kind}' (supported: csv, parquet, mssql)"),
        };
    }

    private static MssqlSource BuildMssqlSource(Dictionary<string, object?> spec)
    {
        var src = new MssqlSource
        {
            Query = Required(spec, "query"),
            ConnectionString = OptStr(spec, "connection_string"),
            ConnectionStringEnv = OptStr(spec, "connection_string_env"),
        };
        // Catch this at validate time, not at Read time (Codex R1 round-2 finding).
        if (string.IsNullOrEmpty(src.ConnectionString) && string.IsNullOrEmpty(src.ConnectionStringEnv))
            throw new ArgumentException(
                "mssql source needs either 'connection_string' or 'connection_string_env'");
        return src;
    }

    private static ITransform BuildTransform(Dictionary<string, object?> spec)
    {
        var kind = spec.TryGetValue("type", out var t) ? t?.ToString() : null;
        return kind switch
        {
            "cast"   => new CastTransform   { Columns = ToStringMap (kind, "columns", spec.GetValueOrDefault("columns")) },
            "rename" => new RenameTransform { Columns = ToStringMap (kind, "columns", spec.GetValueOrDefault("columns")) },
            "select" => new SelectTransform { Columns = ToStringList(kind, "columns", spec.GetValueOrDefault("columns")) },
            "drop"   => new DropTransform   { Columns = ToStringList(kind, "columns", spec.GetValueOrDefault("columns")) },
            "filter" => new FilterTransform { Where   = ToFilterClauses(spec.GetValueOrDefault("where")) },
            _ => throw new ArgumentException($"unknown transform type: '{kind}'"),
        };
    }

    private static ISink BuildSink(Dictionary<string, object?> spec)
    {
        var kind = spec.TryGetValue("type", out var t) ? t?.ToString() : null;
        return kind switch
        {
            "csv"     => new CsvSink     { Path = Required(spec, "path"),  Separator = OptStr(spec, "separator", ",") },
            "parquet" => new ParquetSink { Path = Required(spec, "path") },
            "hyper"   => new HyperSink
            {
                Path  = Required(spec, "path"),
                Table = OptStr(spec, "table", "Extract"),
                Mode  = ValidateHyperMode(OptStr(spec, "mode", "replace")),
            },
            _ => throw new ArgumentException($"unknown sink type: '{kind}' (supported: csv, parquet, hyper)"),
        };
    }

    private static string ValidateHyperMode(string mode)
    {
        if (HyperSink.AllowedModes.Any(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase)))
            return mode;
        throw new ArgumentException(
            $"unsupported hyper sink mode '{mode}' (allowed: {string.Join(", ", HyperSink.AllowedModes)})");
    }

    private static string Required(Dictionary<string, object?> spec, string key)
    {
        if (!spec.TryGetValue(key, out var v) || v is null)
            throw new ArgumentException($"missing required '{key}'");
        if (!IsScalar(v))
            throw new ArgumentException(
                $"'{key}' must be a scalar value (got {DescribeShape(v)})");
        var s = v.ToString();
        if (string.IsNullOrEmpty(s))
            throw new ArgumentException($"'{key}' must not be empty");
        return s;
    }

    private static string OptStr(Dictionary<string, object?> spec, string key, string @default = "")
    {
        if (!spec.TryGetValue(key, out var v) || v is null) return @default;
        if (!IsScalar(v))
            throw new ArgumentException(
                $"'{key}' must be a scalar value (got {DescribeShape(v)})");
        return v.ToString() ?? @default;
    }

    private static bool OptBool(Dictionary<string, object?> spec, string key, bool @default)
    {
        if (!spec.TryGetValue(key, out var v) || v is null) return @default;
        if (v is bool b) return b;
        if (!IsScalar(v))
            throw new ArgumentException(
                $"'{key}' must be a boolean (got {DescribeShape(v)})");
        // Strict parse — round-2 left a silent fallback that turned `has_header: [false]`
        // into the default of `true`.
        if (bool.TryParse(v.ToString(), out var parsed)) return parsed;
        throw new ArgumentException(
            $"'{key}' must be true / false (got '{v}')");
    }

    private static bool IsScalar(object? v)
    {
        if (v is null) return true;
        if (v is string) return true;
        if (v is IDictionary<object, object>) return false;
        if (v is IEnumerable<object>) return false;
        return true; // bool, int, double, DateTime, etc.
    }

    // Shape coercers — strict. Earlier versions silently returned empty
    // collections when the YAML was the wrong shape; that left Validate
    // with nothing to reject, so a malformed pipeline could pass validate
    // and silently misbehave at run time (Codex R1 round-2 finding).
    private static Dictionary<string, string> ToStringMap(
        string? transformKind, string key, object? raw)
    {
        var ctx = $"{transformKind ?? "transform"}.{key}";
        if (raw is null)
            throw new ArgumentException($"'{ctx}' is required");
        if (raw is not IDictionary<object, object> d)
            throw new ArgumentException(
                $"'{ctx}' must be a map of name → value (got {DescribeShape(raw)})");
        if (d.Count == 0)
            throw new ArgumentException($"'{ctx}' must contain at least one entry");

        var dict = new Dictionary<string, string>();
        foreach (var kv in d)
        {
            // Keys and values must be scalars — round-2 stringified nested
            // YAML lists/maps so `rename.columns.name: [full_name]` produced
            // a column named "System.Collections.Generic.List`1[System.Object]".
            if (!IsScalar(kv.Key))
                throw new ArgumentException(
                    $"'{ctx}' keys must be scalar strings (got {DescribeShape(kv.Key)})");
            if (!IsScalar(kv.Value))
                throw new ArgumentException(
                    $"'{ctx}' value for '{kv.Key}' must be a scalar (got {DescribeShape(kv.Value)})");
            dict[kv.Key.ToString() ?? ""] = kv.Value?.ToString() ?? "";
        }
        return dict;
    }

    private static List<string> ToStringList(
        string? transformKind, string key, object? raw)
    {
        var ctx = $"{transformKind ?? "transform"}.{key}";
        if (raw is null)
            throw new ArgumentException($"'{ctx}' is required");
        if (raw is string)
            throw new ArgumentException(
                $"'{ctx}' must be a list, not a scalar string. " +
                "Use [name] for a single-entry list.");
        if (raw is IDictionary<object, object>)
            throw new ArgumentException(
                $"'{ctx}' must be a list, not a map.");
        if (raw is not IEnumerable<object> seq)
            throw new ArgumentException(
                $"'{ctx}' must be a list (got {DescribeShape(raw)})");

        var list = new List<string>();
        foreach (var v in seq)
        {
            if (!IsScalar(v))
                throw new ArgumentException(
                    $"'{ctx}' entries must be scalar strings (got {DescribeShape(v)})");
            list.Add(v?.ToString() ?? "");
        }
        if (list.Count == 0)
            throw new ArgumentException($"'{ctx}' must contain at least one entry");
        return list;
    }

    private static List<FilterClause> ToFilterClauses(object? raw)
    {
        if (raw is null)
            throw new ArgumentException("filter.where is required");
        if (raw is IDictionary<object, object>)
            throw new ArgumentException(
                "filter.where must be a list of clauses, not a single map. " +
                "Wrap clauses in [ ]: where: [{col: a, op: '==', value: 1}]");
        if (raw is string)
            throw new ArgumentException(
                "filter.where must be a list of clauses, not a scalar string.");
        if (raw is not IEnumerable<object> seq)
            throw new ArgumentException(
                $"filter.where must be a list of clauses (got {DescribeShape(raw)})");

        var list = new List<FilterClause>();
        foreach (var item in seq)
        {
            if (item is not IDictionary<object, object> dict)
                throw new ArgumentException(
                    $"filter.where[] entries must be maps (got {DescribeShape(item)})");
            list.Add(new FilterClause
            {
                Col   = dict.TryGetValue("col", out var c)   ? c?.ToString() ?? "" : "",
                Op    = dict.TryGetValue("op", out var op)   ? op?.ToString() ?? "" : "",
                Value = dict.TryGetValue("value", out var v) ? v : null,
            });
        }
        if (list.Count == 0)
            throw new ArgumentException("filter.where must contain at least one clause");
        return list;
    }

    private static string DescribeShape(object? raw) => raw switch
    {
        null                        => "null",
        string                      => "scalar string",
        IDictionary<object, object> => "map",
        IEnumerable<object>         => "list",
        _                           => raw.GetType().Name,
    };
}
