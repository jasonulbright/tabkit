using System;
using System.Data;
using System.IO;
using ParquetSharp;

namespace Tabkit.Core.Extract.Sources;

/// <summary>
/// Reads a Parquet file into a <see cref="DataTable"/>. Backed by ParquetSharp
/// (LSE-owned, wraps Apache Arrow's parquet-cpp). One row-group / one column
/// chunk at a time so memory pressure scales with the widest column, not the
/// whole file.
/// <para>
/// Supported physical types: bool, int32, int64, float, double, string. Other
/// logical types (decimal, timestamp, date) fall through as the underlying
/// physical representation; transform downstream if interpretation matters.
/// </para>
/// </summary>
public sealed class ParquetSource : ISource
{
    public string Path { get; init; } = "";

    public DataTable Read()
    {
        if (!File.Exists(Path))
            throw new FileNotFoundException("parquet source not found", Path);

        using var reader = new ParquetFileReader(Path);
        var fileMeta = reader.FileMetaData;
        var schema = fileMeta.Schema;
        var numCols = schema.NumColumns;

        var table = new DataTable();
        for (var c = 0; c < numCols; c++)
        {
            var descriptor = schema.Column(c);
            table.Columns.Add(descriptor.Name, MapPhysicalType(descriptor.PhysicalType));
        }

        for (var rg = 0; rg < fileMeta.NumRowGroups; rg++)
        {
            using var rowGroup = reader.RowGroup(rg);
            var rowCount = (int)rowGroup.MetaData.NumRows;
            var columns = new object?[numCols][];

            for (var c = 0; c < numCols; c++)
                columns[c] = ReadColumn(rowGroup.Column(c), rowCount);

            for (var r = 0; r < rowCount; r++)
            {
                var row = table.NewRow();
                for (var c = 0; c < numCols; c++)
                    row[c] = columns[c][r] ?? DBNull.Value;
                table.Rows.Add(row);
            }
        }

        return table;
    }

    private static object?[] ReadColumn(ColumnReader columnReader, int rowCount)
    {
        var result = new object?[rowCount];
        var descriptor = columnReader.ColumnDescriptor;
        var nullable = descriptor.MaxDefinitionLevel > 0;

        switch (descriptor.PhysicalType)
        {
            case PhysicalType.Boolean:
                if (nullable) CopyNullable<bool>(columnReader, result, rowCount);
                else          CopyTyped<bool>(columnReader, result, rowCount);
                break;
            case PhysicalType.Int32:
                if (nullable) CopyNullable<int>(columnReader, result, rowCount);
                else          CopyTyped<int>(columnReader, result, rowCount);
                break;
            case PhysicalType.Int64:
                if (nullable) CopyNullable<long>(columnReader, result, rowCount);
                else          CopyTyped<long>(columnReader, result, rowCount);
                break;
            case PhysicalType.Float:
                if (nullable) CopyNullable<float>(columnReader, result, rowCount);
                else          CopyTyped<float>(columnReader, result, rowCount);
                break;
            case PhysicalType.Double:
                if (nullable) CopyNullable<double>(columnReader, result, rowCount);
                else          CopyTyped<double>(columnReader, result, rowCount);
                break;
            case PhysicalType.ByteArray:
                // String columns are reference-type nullable at the schema level
                // regardless of MaxDefinitionLevel — LogicalReader<string> already
                // returns null for absent values.
                CopyTyped<string>(columnReader, result, rowCount);
                break;
            default:
                // For unsupported physical types, leave the row as null;
                // downstream transforms can choose how to handle.
                break;
        }
        return result;
    }

    private static void CopyTyped<T>(ColumnReader columnReader, object?[] dest, int rowCount)
    {
        var logical = columnReader.LogicalReader<T>();
        var buffer = new T[rowCount];
        var read = logical.ReadBatch(buffer, 0, buffer.Length);
        for (var i = 0; i < read; i++)
            dest[i] = buffer[i];
    }

    private static void CopyNullable<T>(ColumnReader columnReader, object?[] dest, int rowCount)
        where T : struct
    {
        // Nullable read so the source preserves Parquet nulls instead of
        // collapsing them to T's default — symmetric with ParquetSink's
        // nullable-column write path (Codex R1 finding #7).
        var logical = columnReader.LogicalReader<T?>();
        var buffer = new T?[rowCount];
        var read = logical.ReadBatch(buffer, 0, buffer.Length);
        for (var i = 0; i < read; i++)
            dest[i] = buffer[i].HasValue ? (object)buffer[i]!.Value : null;
    }

    private static Type MapPhysicalType(PhysicalType physicalType) => physicalType switch
    {
        PhysicalType.Boolean   => typeof(bool),
        PhysicalType.Int32     => typeof(int),
        PhysicalType.Int64     => typeof(long),
        PhysicalType.Float     => typeof(float),
        PhysicalType.Double    => typeof(double),
        PhysicalType.ByteArray => typeof(string),
        _                      => typeof(string),
    };
}
