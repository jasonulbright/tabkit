using System;
using System.Data;
using System.IO;
using FluentAssertions;
using Tableau.HyperAPI;
using Tabkit.Core.Extract.Sinks;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Roundtrip coverage for the C.2 work: <see cref="HyperSink"/> writes
/// .NET DateTime / DateOnly columns as proper Hyper Timestamp / Date types
/// (not ISO 8601 text), and reading the .hyper back via the HyperAPI returns
/// values equal at microsecond / day precision.
///
/// Each test builds a DataTable in memory, writes a .hyper extract via
/// HyperSink, then opens the same .hyper read-only and asserts column type +
/// per-row equality. .hyper files are cleaned up via try/finally.
/// </summary>
public class HyperTimestampRoundtripTests
{
    private readonly ITestOutputHelper _output;
    public HyperTimestampRoundtripTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DateTime_Column_Roundtrips_At_Microsecond_Precision()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-hyper-ts-");
        var hyperPath = Path.Combine(tmp.FullName, "ts.hyper");

        var table = new DataTable("ts");
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("at", typeof(DateTime));

        var samples = new[]
        {
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 15, 14, 30, 45, 123, DateTimeKind.Utc).AddTicks(4560),
            new DateTime(1999, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc).AddTicks(9990),
        };
        for (var i = 0; i < samples.Length; i++)
            table.Rows.Add(i + 1, samples[i]);

        try
        {
            new HyperSink { Path = hyperPath, Table = "ts", Mode = "replace" }.Write(table);
            File.Exists(hyperPath).Should().BeTrue();

            using var hyper = new HyperProcess(Telemetry.DoNotSendUsageDataToTableau);
            using var conn = new Connection(hyper.Endpoint, hyperPath);

            var query = "SELECT id, at FROM \"ts\" ORDER BY id";
            using var result = conn.ExecuteQuery(query);

            var rows = new System.Collections.Generic.List<(int Id, Timestamp At)>();
            while (result.NextRow())
                rows.Add((result.GetInt(0), result.GetTimestamp(1)));

            rows.Should().HaveCount(samples.Length);
            for (var i = 0; i < samples.Length; i++)
            {
                var expectedMicros = (int)((samples[i].Ticks % TimeSpan.TicksPerSecond) / 10);
                _output.WriteLine($"row {i + 1}: src={samples[i]:O} → hyper Y/M/D={rows[i].At.Year}/{rows[i].At.Month}/{rows[i].At.Day} {rows[i].At.Hour}:{rows[i].At.Minute}:{rows[i].At.Second}.{rows[i].At.Microsecond}");
                rows[i].At.Year.Should().Be(samples[i].Year);
                rows[i].At.Month.Should().Be(samples[i].Month);
                rows[i].At.Day.Should().Be(samples[i].Day);
                rows[i].At.Hour.Should().Be(samples[i].Hour);
                rows[i].At.Minute.Should().Be(samples[i].Minute);
                rows[i].At.Second.Should().Be(samples[i].Second);
                rows[i].At.Microsecond.Should().Be(expectedMicros);
            }
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void DateOnly_Column_Roundtrips_At_Day_Precision()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-hyper-date-");
        var hyperPath = Path.Combine(tmp.FullName, "date.hyper");

        var table = new DataTable("d");
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("on", typeof(DateOnly));

        var samples = new[]
        {
            new DateOnly(2024, 2, 29),   // leap year
            new DateOnly(2000, 1, 1),    // epoch-ish
            new DateOnly(1999, 12, 31),  // y2k boundary
        };
        for (var i = 0; i < samples.Length; i++)
            table.Rows.Add(i + 1, samples[i]);

        try
        {
            new HyperSink { Path = hyperPath, Table = "d", Mode = "replace" }.Write(table);

            using var hyper = new HyperProcess(Telemetry.DoNotSendUsageDataToTableau);
            using var conn = new Connection(hyper.Endpoint, hyperPath);
            using var result = conn.ExecuteQuery("SELECT id, \"on\" FROM \"d\" ORDER BY id");

            var rows = new System.Collections.Generic.List<(int Id, Date On)>();
            while (result.NextRow())
                rows.Add((result.GetInt(0), result.GetDate(1)));

            rows.Should().HaveCount(samples.Length);
            for (var i = 0; i < samples.Length; i++)
            {
                rows[i].On.Year.Should().Be(samples[i].Year);
                rows[i].On.Month.Should().Be(samples[i].Month);
                rows[i].On.Day.Should().Be(samples[i].Day);
            }
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Hyper_Sink_Mode_Append_Adds_To_Existing_Table()
    {
        // Regression for Codex R1 finding #6: mode: append used to accept the
        // value at the spec level but always CreateTable internally, so a
        // second run failed with "table already exists". Two append runs
        // against the same path must succeed and accumulate rows.
        var tmp = Directory.CreateTempSubdirectory("tabkit-hyper-append-");
        var hyperPath = Path.Combine(tmp.FullName, "append.hyper");

        var table = new DataTable("rows");
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("label", typeof(string));
        table.Rows.Add(1, "alpha");
        table.Rows.Add(2, "beta");
        table.Rows.Add(3, "gamma");

        try
        {
            new HyperSink { Path = hyperPath, Table = "rows", Mode = "append" }.Write(table);
            new HyperSink { Path = hyperPath, Table = "rows", Mode = "append" }.Write(table);

            using var hyper = new HyperProcess(Telemetry.DoNotSendUsageDataToTableau);
            using var conn = new Connection(hyper.Endpoint, hyperPath);
            using var result = conn.ExecuteQuery("SELECT COUNT(*) FROM \"rows\"");
            result.NextRow().Should().BeTrue();
            var count = result.GetLong(0);
            count.Should().Be(6, "two append runs of three rows each should yield six rows");
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Hyper_Sink_Unknown_Mode_Throws()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-hyper-badmode-");
        var hyperPath = Path.Combine(tmp.FullName, "x.hyper");
        var table = new DataTable("rows");
        table.Columns.Add("id", typeof(int));
        table.Rows.Add(1);
        try
        {
            Action act = () => new HyperSink { Path = hyperPath, Table = "rows", Mode = "merge" }.Write(table);
            act.Should().Throw<ArgumentException>().WithMessage("*unsupported hyper sink mode*");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void DateTimeOffset_Column_Roundtrips_As_Utc_Timestamp()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-hyper-dto-");
        var hyperPath = Path.Combine(tmp.FullName, "dto.hyper");

        var table = new DataTable("dto");
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("at", typeof(DateTimeOffset));

        // DateTimeOffset writes as the UTC moment — sink converts via UtcDateTime
        // before constructing the Hyper Timestamp.
        var src = new DateTimeOffset(2024, 5, 1, 10, 30, 15, TimeSpan.FromHours(-4));
        var expectedUtc = src.UtcDateTime;
        table.Rows.Add(1, src);

        try
        {
            new HyperSink { Path = hyperPath, Table = "dto", Mode = "replace" }.Write(table);

            using var hyper = new HyperProcess(Telemetry.DoNotSendUsageDataToTableau);
            using var conn = new Connection(hyper.Endpoint, hyperPath);
            using var result = conn.ExecuteQuery("SELECT at FROM \"dto\"");

            result.NextRow().Should().BeTrue();
            var actual = result.GetTimestamp(0);

            actual.Year.Should().Be(expectedUtc.Year);
            actual.Hour.Should().Be(expectedUtc.Hour);    // 10 + 4 = 14 UTC
            actual.Minute.Should().Be(expectedUtc.Minute);
            actual.Second.Should().Be(expectedUtc.Second);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }
}
