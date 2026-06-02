using System;
using System.Data;
using System.IO;
using ParquetSharp;

namespace Tabkit.Core.Extract.Sinks;

/// <summary>
/// Writes a <see cref="DataTable"/> to a Parquet file via ParquetSharp.
/// Schema is inferred from the DataTable's CLR column types — int / long /
/// float / double / bool / string / DateTime. Every primitive column is
/// declared nullable so DBNull cells round-trip as Parquet nulls rather
/// than collapsing to the type's default value (Codex R1 finding #7).
/// One row group per write, no chunking yet; revisit if we start producing
/// files large enough that columnar compression efficiency matters.
/// </summary>
public sealed class ParquetSink : ISink
{
    public string Path { get; init; } = "";

    public void Write(DataTable table)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");

        var columns = new Column[table.Columns.Count];
        for (var c = 0; c < table.Columns.Count; c++)
            columns[c] = BuildColumn(table.Columns[c]);

        using var writer = new ParquetFileWriter(Path, columns);
        using var rowGroup = writer.AppendRowGroup();
        for (var c = 0; c < table.Columns.Count; c++)
            WriteColumn(rowGroup.NextColumn(), table, c);
        writer.Close();
    }

    private static Column BuildColumn(DataColumn col)
    {
        var t = col.DataType;
        // Nullable<T> for every value-type column so DBNull round-trips as
        // Parquet null instead of T's default. String is a reference type so
        // Column<string> is already nullable at the schema level.
        if (t == typeof(bool))     return new Column<bool?>(col.ColumnName);
        if (t == typeof(int) || t == typeof(short) || t == typeof(sbyte))
            return new Column<int?>(col.ColumnName);
        if (t == typeof(long) || t == typeof(uint) || t == typeof(ushort) || t == typeof(byte))
            return new Column<long?>(col.ColumnName);
        if (t == typeof(float))    return new Column<float?>(col.ColumnName);
        if (t == typeof(double) || t == typeof(decimal))
            return new Column<double?>(col.ColumnName);
        if (t == typeof(DateTime)) return new Column<DateTime?>(col.ColumnName);
        return new Column<string>(col.ColumnName);
    }

    private static void WriteColumn(ColumnWriter columnWriter, DataTable table, int colIndex)
    {
        var dt = table.Columns[colIndex].DataType;
        var rows = table.Rows.Count;

        if (dt == typeof(bool))                                   WriteNullable<bool>(columnWriter, table, colIndex, rows, v => Convert.ToBoolean(v));
        else if (dt == typeof(int) || dt == typeof(short) || dt == typeof(sbyte))
                                                                  WriteNullable<int>(columnWriter, table, colIndex, rows, v => Convert.ToInt32(v));
        else if (dt == typeof(long) || dt == typeof(uint) || dt == typeof(ushort) || dt == typeof(byte))
                                                                  WriteNullable<long>(columnWriter, table, colIndex, rows, v => Convert.ToInt64(v));
        else if (dt == typeof(float))                             WriteNullable<float>(columnWriter, table, colIndex, rows, v => Convert.ToSingle(v));
        else if (dt == typeof(double) || dt == typeof(decimal))   WriteNullable<double>(columnWriter, table, colIndex, rows, v => Convert.ToDouble(v));
        else if (dt == typeof(DateTime))                          WriteNullable<DateTime>(columnWriter, table, colIndex, rows, v => Convert.ToDateTime(v));
        else                                                      WriteStrings(columnWriter, table, colIndex, rows);
    }

    private static void WriteNullable<T>(
        ColumnWriter columnWriter, DataTable table, int colIndex, int rows,
        Func<object, T> convert) where T : struct
    {
        var buffer = new T?[rows];
        for (var r = 0; r < rows; r++)
        {
            var cell = table.Rows[r][colIndex];
            buffer[r] = cell is null || cell is DBNull ? null : convert(cell);
        }
        var logical = columnWriter.LogicalWriter<T?>();
        logical.WriteBatch(buffer);
    }

    private static void WriteStrings(ColumnWriter columnWriter, DataTable table, int colIndex, int rows)
    {
        // ParquetSharp's LogicalWriter<string> signature is non-nullable string[]
        // but the underlying writer treats string-type ByteArray columns as
        // nullable in the Parquet schema. Pass nulls through by re-typing.
        var buffer = new string[rows];
        for (var r = 0; r < rows; r++)
        {
            var cell = table.Rows[r][colIndex];
            buffer[r] = cell is null || cell is DBNull ? null! : (cell.ToString() ?? string.Empty);
        }
        var logical = columnWriter.LogicalWriter<string>();
        logical.WriteBatch(buffer);
    }
}
