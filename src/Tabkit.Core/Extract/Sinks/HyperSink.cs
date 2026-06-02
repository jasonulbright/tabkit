using System;
using System.Data;
using System.IO;
using Tableau.HyperAPI;

namespace Tabkit.Core.Extract.Sinks;

/// <summary>
/// Hyper sink — writes a <see cref="DataTable"/> to a <c>.hyper</c> extract
/// via the official Tableau.HyperAPI.NET package.
/// </summary>
public sealed class HyperSink : ISink
{
    public string Path { get; init; } = "";
    public string Table { get; init; } = "Extract";
    public string Mode { get; init; } = "replace";

    /// <summary>Accepted values for <see cref="Mode"/>: "replace" (default) or "append".</summary>
    public static IReadOnlyCollection<string> AllowedModes { get; } = new[] { "replace", "append" };

    public void Write(DataTable table)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");

        var isAppend = string.Equals(Mode, "append", StringComparison.OrdinalIgnoreCase);
        var isReplace = string.Equals(Mode, "replace", StringComparison.OrdinalIgnoreCase);
        if (!isAppend && !isReplace)
            throw new ArgumentException(
                $"unsupported hyper sink mode '{Mode}' (allowed: {string.Join(", ", AllowedModes)})");

        // Replace = drop the file before opening so the new run starts clean.
        // Append = keep the file (and any existing table) and add this run's rows.
        if (isReplace && File.Exists(Path))
            File.Delete(Path);

        using var hyper = new HyperProcess(Telemetry.DoNotSendUsageDataToTableau);
        using var conn = new Connection(hyper.Endpoint, Path, CreateMode.CreateIfNotExists);

        var tableDef = BuildTableDef(table, Table);

        // In replace mode the file was just removed (or never existed), so the
        // table cannot exist yet — plain CreateTable is correct. In append mode
        // we must tolerate the table already existing from a prior run.
        if (isAppend)
        {
            if (!conn.Catalog.HasTable(tableDef.TableName))
                conn.Catalog.CreateTable(tableDef);
        }
        else
        {
            conn.Catalog.CreateTable(tableDef);
        }

        using var inserter = new Inserter(conn, tableDef);
        foreach (DataRow row in table.Rows)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var val = row[c];
                if (val is null || val is DBNull)
                {
                    inserter.AddNull();
                    continue;
                }
                InsertValue(inserter, table.Columns[c].DataType, val);
            }
            inserter.EndRow();
        }
        inserter.Execute();
    }

    private static TableDefinition BuildTableDef(DataTable table, string tableName)
    {
        var def = new TableDefinition(tableName);
        foreach (DataColumn col in table.Columns)
            def.AddColumn(col.ColumnName, MapType(col.DataType), Nullability.Nullable);
        return def;
    }

    private static SqlType MapType(Type clrType)
    {
        if (clrType == typeof(int) || clrType == typeof(short) || clrType == typeof(sbyte))
            return SqlType.Int();
        if (clrType == typeof(long) || clrType == typeof(uint) || clrType == typeof(ushort) || clrType == typeof(byte))
            return SqlType.BigInt();
        if (clrType == typeof(float) || clrType == typeof(double) || clrType == typeof(decimal))
            return SqlType.Double();
        if (clrType == typeof(bool))
            return SqlType.Bool();
        if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset))
            return SqlType.Timestamp();
        if (clrType == typeof(DateOnly))
            return SqlType.Date();
        return SqlType.Text();
    }

    private static void InsertValue(Inserter inserter, Type clrType, object val)
    {
        if (clrType == typeof(int) || clrType == typeof(short) || clrType == typeof(sbyte))
            inserter.Add(Convert.ToInt32(val));
        else if (clrType == typeof(long) || clrType == typeof(uint) || clrType == typeof(ushort) || clrType == typeof(byte))
            inserter.Add(Convert.ToInt64(val));
        else if (clrType == typeof(float) || clrType == typeof(double) || clrType == typeof(decimal))
            inserter.Add(Convert.ToDouble(val));
        else if (clrType == typeof(bool))
            inserter.Add(Convert.ToBoolean(val));
        else if (clrType == typeof(DateTime))
            inserter.Add(ToHyperTimestamp((DateTime)val));
        else if (clrType == typeof(DateTimeOffset))
            inserter.Add(ToHyperTimestamp(((DateTimeOffset)val).UtcDateTime));
        else if (clrType == typeof(DateOnly))
        {
            var d = (DateOnly)val;
            inserter.Add(new Date(d.Year, d.Month, d.Day));
        }
        else
            inserter.Add(val.ToString() ?? string.Empty);
    }

    /// <summary>
    /// Convert a .NET <see cref="DateTime"/> to a Hyper <see cref="Timestamp"/>
    /// with microsecond precision. DateTime.Ticks are 100-ns, so dividing
    /// the sub-second remainder by 10 yields microseconds. Hyper's Timestamp
    /// supports a wider range of dates than DateTime, but the reverse — going
    /// from DateTime — is loss-free at microsecond precision.
    /// </summary>
    private static Timestamp ToHyperTimestamp(DateTime dt)
    {
        var subSecondTicks = dt.Ticks % TimeSpan.TicksPerSecond;
        var microseconds = (int)(subSecondTicks / 10);
        return new Timestamp(
            dt.Year, dt.Month, dt.Day,
            dt.Hour, dt.Minute, dt.Second,
            microseconds,
            dt.Kind);
    }
}
