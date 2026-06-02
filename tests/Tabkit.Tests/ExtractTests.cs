using System.Data;
using System.IO;
using FluentAssertions;
using Tabkit.Core.Extract;

namespace Tabkit.Tests;

public class ExtractTests
{
    private static string MakeSampleCsv(string dir)
    {
        var p = Path.Combine(dir, "orders.csv");
        File.WriteAllText(p,
            "order_id,total,order_date,customer\n" +
            "1,10.50,2024-01-01,alice\n" +
            "2,0.00,2024-02-15,bob\n" +
            "3,99.99,2024-03-20,alice\n" +
            "4,-5.00,2024-04-10,charlie\n");
        return p;
    }

    [Fact]
    public void Csv_To_Csv_Passthrough()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-pass-");
        try
        {
            var src = MakeSampleCsv(tmp.FullName);
            var dst = Path.Combine(tmp.FullName, "out.csv");
            var p = new Pipeline
            {
                Name = "pass",
                Source = new() { ["type"] = "csv", ["path"] = src },
                Sink = new() { ["type"] = "csv", ["path"] = dst },
            };
            var result = p.Run();
            result.RowsIn.Should().Be(4);
            result.RowsOut.Should().Be(4);
            File.Exists(dst).Should().BeTrue();
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Filter_Transform_DropsRows()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-filt-");
        try
        {
            var src = MakeSampleCsv(tmp.FullName);
            var dst = Path.Combine(tmp.FullName, "out.csv");
            var p = new Pipeline
            {
                Name = "filter",
                Source = new() { ["type"] = "csv", ["path"] = src },
                Transforms = new()
                {
                    new()
                    {
                        ["type"] = "filter",
                        ["where"] = new System.Collections.Generic.List<object>
                        {
                            new System.Collections.Generic.Dictionary<object, object>
                            {
                                ["col"] = "total", ["op"] = ">", ["value"] = "0"
                            },
                        },
                    },
                },
                Sink = new() { ["type"] = "csv", ["path"] = dst },
            };
            var result = p.Run();
            result.RowsIn.Should().Be(4);
            result.RowsOut.Should().Be(2); // 0.00 and -5.00 dropped
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Yaml_RoundTrip()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-yaml-");
        try
        {
            var src = MakeSampleCsv(tmp.FullName);
            var dst = Path.Combine(tmp.FullName, "yaml-out.csv");
            var yml = $@"
name: from_yaml
source:
  type: csv
  path: {src.Replace("\\", "/")}
transforms:
  - type: filter
    where:
      - {{ col: total, op: '>=', value: 10 }}
sink:
  type: csv
  path: {dst.Replace("\\", "/")}
";
            var p = Pipeline.FromYaml(yml);
            var result = p.Run();
            result.RowsOut.Should().Be(2); // 10.50 and 99.99
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Unknown_Source_Throws()
    {
        var p = new Pipeline
        {
            Name = "bad",
            Source = new() { ["type"] = "nonexistent" },
            Sink = new() { ["type"] = "csv", ["path"] = "x.csv" },
        };
        var act = () => p.Run();
        act.Should().Throw<System.ArgumentException>().WithMessage("*unknown source type*");
    }

    [Fact]
    public void Filter_In_With_Map_Value_Throws_At_Runtime()
    {
        // Defense-in-depth for Codex R1 round-4: even if validate is bypassed
        // (Run does not call Validate), a membership `in` with a map value must
        // throw at Apply time, not silently filter every row out.
        var tmp = Directory.CreateTempSubdirectory("tabkit-inmap-");
        try
        {
            var src = MakeSampleCsv(tmp.FullName);
            var p = new Pipeline
            {
                Name = "in_map",
                Source = new() { ["type"] = "csv", ["path"] = src },
                Transforms = new()
                {
                    new()
                    {
                        ["type"] = "filter",
                        ["where"] = new System.Collections.Generic.List<object>
                        {
                            new System.Collections.Generic.Dictionary<object, object>
                            {
                                ["col"] = "customer", ["op"] = "in",
                                ["value"] = new System.Collections.Generic.Dictionary<object, object>
                                {
                                    ["alice"] = true,
                                },
                            },
                        },
                    },
                },
                Sink = new() { ["type"] = "csv", ["path"] = Path.Combine(tmp.FullName, "out.csv") },
            };
            var act = () => p.Run();
            act.Should().Throw<System.ArgumentException>().WithMessage("*not a map*");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Filter_In_With_Nested_List_Throws_At_Runtime()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-innested-");
        try
        {
            var src = MakeSampleCsv(tmp.FullName);
            var p = new Pipeline
            {
                Name = "in_nested",
                Source = new() { ["type"] = "csv", ["path"] = src },
                Transforms = new()
                {
                    new()
                    {
                        ["type"] = "filter",
                        ["where"] = new System.Collections.Generic.List<object>
                        {
                            new System.Collections.Generic.Dictionary<object, object>
                            {
                                ["col"] = "customer", ["op"] = "in",
                                ["value"] = new System.Collections.Generic.List<object>
                                {
                                    new System.Collections.Generic.List<object> { "alice" },
                                },
                            },
                        },
                    },
                },
                Sink = new() { ["type"] = "csv", ["path"] = Path.Combine(tmp.FullName, "out.csv") },
            };
            var act = () => p.Run();
            act.Should().Throw<System.ArgumentException>().WithMessage("*nested lists or maps*");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Filter_In_With_Proper_Scalar_List_Filters_At_Runtime()
    {
        // Positive control: in: [alice] keeps the two alice rows out of four.
        var tmp = Directory.CreateTempSubdirectory("tabkit-ingood-");
        try
        {
            var src = MakeSampleCsv(tmp.FullName);
            var p = new Pipeline
            {
                Name = "in_good",
                Source = new() { ["type"] = "csv", ["path"] = src },
                Transforms = new()
                {
                    new()
                    {
                        ["type"] = "filter",
                        ["where"] = new System.Collections.Generic.List<object>
                        {
                            new System.Collections.Generic.Dictionary<object, object>
                            {
                                ["col"] = "customer", ["op"] = "in",
                                ["value"] = new System.Collections.Generic.List<object> { "alice" },
                            },
                        },
                    },
                },
                Sink = new() { ["type"] = "csv", ["path"] = Path.Combine(tmp.FullName, "out.csv") },
            };
            var result = p.Run();
            result.RowsOut.Should().Be(2, "two of the four sample rows have customer == alice");
        }
        finally { tmp.Delete(recursive: true); }
    }
}
