using System.Data;
using System.IO;
using FluentAssertions;
using Tabkit.Core.Extract;
using Tabkit.Core.Extract.Sinks;
using Tabkit.Core.Extract.Sources;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Roundtrip coverage for the C.1 work: <see cref="ParquetSink"/> writes a
/// DataTable to .parquet via ParquetSharp; <see cref="ParquetSource"/> reads
/// it back into an equivalent DataTable. The two halves are exercised
/// individually and together via a Pipeline whose source is CSV and sink is
/// Parquet (then Parquet → CSV in the reverse direction).
/// </summary>
public class ParquetRoundtripTests
{
    private readonly ITestOutputHelper _output;
    public ParquetRoundtripTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Mixed_Types_Roundtrip_Preserves_Values()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-parq-mixed-");
        try
        {
            var src = new DataTable("rows");
            src.Columns.Add("id",     typeof(int));
            src.Columns.Add("amount", typeof(double));
            src.Columns.Add("active", typeof(bool));
            src.Columns.Add("name",   typeof(string));

            src.Rows.Add(1, 12.5, true,  "alice");
            src.Rows.Add(2, -7.0, false, "bob");
            src.Rows.Add(3, 0.0,  true,  "carol");

            var parquetPath = Path.Combine(tmp.FullName, "mixed.parquet");
            new ParquetSink { Path = parquetPath }.Write(src);
            File.Exists(parquetPath).Should().BeTrue();

            var dst = new ParquetSource { Path = parquetPath }.Read();
            dst.Rows.Count.Should().Be(src.Rows.Count);
            dst.Columns.Count.Should().Be(src.Columns.Count);

            for (var r = 0; r < src.Rows.Count; r++)
            {
                dst.Rows[r]["id"].Should().Be(src.Rows[r]["id"]);
                dst.Rows[r]["amount"].Should().Be(src.Rows[r]["amount"]);
                dst.Rows[r]["active"].Should().Be(src.Rows[r]["active"]);
                dst.Rows[r]["name"].Should().Be(src.Rows[r]["name"]);
            }
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Pipeline_Csv_To_Parquet_To_Csv_Preserves_Row_Count()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-parq-pipe-");
        try
        {
            var csvSrc = Path.Combine(tmp.FullName, "in.csv");
            File.WriteAllText(csvSrc,
                "id,total\n1,10.5\n2,99.99\n3,0\n");

            var parquetMid = Path.Combine(tmp.FullName, "mid.parquet");
            var csvDst     = Path.Combine(tmp.FullName, "out.csv");

            var toParquet = new Pipeline
            {
                Name   = "csv_to_parquet",
                Source = new() { ["type"] = "csv",     ["path"] = csvSrc },
                Sink   = new() { ["type"] = "parquet", ["path"] = parquetMid },
            };
            var r1 = toParquet.Run();
            r1.RowsIn.Should().Be(3);
            r1.RowsOut.Should().Be(3);
            File.Exists(parquetMid).Should().BeTrue();

            var fromParquet = new Pipeline
            {
                Name   = "parquet_to_csv",
                Source = new() { ["type"] = "parquet", ["path"] = parquetMid },
                Sink   = new() { ["type"] = "csv",     ["path"] = csvDst },
            };
            var r2 = fromParquet.Run();
            r2.RowsIn.Should().Be(3);
            r2.RowsOut.Should().Be(3);
            File.Exists(csvDst).Should().BeTrue();

            File.ReadAllText(csvDst).Should().Contain("10.5").And.Contain("99.99");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Null_Cells_Roundtrip_As_DBNull_Not_Default_Values()
    {
        // Regression for Codex R1 finding #7: ParquetSink used to substitute
        // T's default (0 for int, false for bool, 0.0 for double) on DBNull
        // cells. Both halves are now nullable so DBNull round-trips as null.
        var tmp = Directory.CreateTempSubdirectory("tabkit-parq-null-");
        try
        {
            var src = new DataTable("rows");
            src.Columns.Add("id",     typeof(int));
            src.Columns.Add("amount", typeof(double));
            src.Columns.Add("active", typeof(bool));
            src.Columns.Add("name",   typeof(string));

            src.Rows.Add(1,        12.5,         true,         "alice");
            src.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
            src.Rows.Add(3,        DBNull.Value, false,        "carol");

            var parquetPath = Path.Combine(tmp.FullName, "nulls.parquet");
            new ParquetSink { Path = parquetPath }.Write(src);

            var dst = new ParquetSource { Path = parquetPath }.Read();
            dst.Rows.Count.Should().Be(3);

            // Row 0: all values present
            dst.Rows[0]["id"].Should().Be(1);
            dst.Rows[0]["amount"].Should().Be(12.5);
            dst.Rows[0]["active"].Should().Be(true);
            dst.Rows[0]["name"].Should().Be("alice");

            // Row 1: every column null
            dst.Rows[1]["id"].Should().Be(DBNull.Value, "int null must not become 0");
            dst.Rows[1]["amount"].Should().Be(DBNull.Value, "double null must not become 0.0");
            dst.Rows[1]["active"].Should().Be(DBNull.Value, "bool null must not become false");
            dst.Rows[1]["name"].Should().Be(DBNull.Value, "string null must not become empty");

            // Row 2: mixed
            dst.Rows[2]["id"].Should().Be(3);
            dst.Rows[2]["amount"].Should().Be(DBNull.Value);
            dst.Rows[2]["active"].Should().Be(false);
            dst.Rows[2]["name"].Should().Be("carol");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Pipeline_Rejects_Unknown_Source_Type_Mentioning_Parquet()
    {
        var p = new Pipeline
        {
            Name   = "bad",
            Source = new() { ["type"] = "magic", ["path"] = "irrelevant" },
            Sink   = new() { ["type"] = "csv",   ["path"] = "irrelevant" },
        };
        var act = () => p.Run();
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*parquet*");
    }
}
