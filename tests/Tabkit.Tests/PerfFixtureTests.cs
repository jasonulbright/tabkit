using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Loading;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Generates a synthetic large workbook (N datasources × M fields each)
/// on the fly and asserts the parse + audit pipeline runs in bounded time.
/// Not a regression-of-correctness test — a regression-of-timing test.
/// The bound is generous on purpose; it catches O(N²) regressions, not
/// individual microbench wobble.
/// </summary>
public class PerfFixtureTests
{
    private readonly ITestOutputHelper _output;
    public PerfFixtureTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Large_Workbook_Parses_And_Audits_Under_Five_Seconds()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-perf-");
        try
        {
            var path = Path.Combine(tmp.FullName, "large.twb");
            var xml = GenerateLargeWorkbook(datasourceCount: 50, fieldsPerDatasource: 100);
            File.WriteAllText(path, xml);

            var sw = Stopwatch.StartNew();
            var wb = TwbLoader.Load(path);
            var loadMs = sw.ElapsedMilliseconds;

            sw.Restart();
            var findings = RuleRegistry.Default().Run(wb);
            var auditMs = sw.ElapsedMilliseconds;

            _output.WriteLine($"perf fixture: {wb.DataSources.Count} ds × {wb.DataSources[0].Fields.Count} fields");
            _output.WriteLine($"  load:  {loadMs} ms");
            _output.WriteLine($"  audit: {auditMs} ms ({findings.Count} findings)");

            (loadMs + auditMs).Should().BeLessThan(5000,
                "load + audit on 50×100 should finish well under 5s");
        }
        finally { tmp.Delete(recursive: true); }
    }

    /// <summary>
    /// Emit a synthetic workbook with <paramref name="datasourceCount"/>
    /// datasources, each with <paramref name="fieldsPerDatasource"/> fields
    /// (half plain, half calculated to exercise the calc-scanning paths).
    /// Every datasource is referenced by a worksheet and every worksheet is
    /// on the same dashboard so AUD001 / AUD002 don't fire on the perf input.
    /// </summary>
    public static string GenerateLargeWorkbook(int datasourceCount, int fieldsPerDatasource)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append("<?xml version='1.0' encoding='utf-8' ?>\n");
        sb.Append("<workbook source-build='2024.3.0' source-platform='win' version='18.1'>\n");
        sb.Append("  <datasources>\n");
        for (var d = 0; d < datasourceCount; d++)
        {
            sb.Append($"    <datasource caption='DS {d}' name='federated.ds{d}' version='18.1'>\n");
            sb.Append($"      <connection class='excel-direct' filename='ds{d}.xlsx'/>\n");
            for (var f = 0; f < fieldsPerDatasource; f++)
            {
                if (f % 2 == 0)
                {
                    sb.Append($"      <column datatype='real' name='[m{f}]' role='measure' type='quantitative'/>\n");
                }
                else
                {
                    sb.Append($"      <column datatype='real' name='[calc{f}]' role='measure' type='quantitative'>\n");
                    sb.Append($"        <calculation class='tableau' formula='[m{f - 1}] * 2'/>\n");
                    sb.Append($"      </column>\n");
                }
            }
            sb.Append("    </datasource>\n");
        }
        sb.Append("  </datasources>\n  <worksheets>\n");
        for (var d = 0; d < datasourceCount; d++)
        {
            sb.Append($"    <worksheet name='Sheet {d}'>\n");
            sb.Append($"      <table><view><datasources><datasource name='federated.ds{d}'/></datasources></view></table>\n");
            sb.Append("    </worksheet>\n");
        }
        sb.Append("  </worksheets>\n  <dashboards>\n");
        sb.Append("    <dashboard name='Main'>\n      <zones>\n        <zone id='1' type='layout-basic'>\n");
        for (var d = 0; d < datasourceCount; d++)
            sb.Append($"          <zone id='{d + 2}' name='Sheet {d}' type='worksheet'/>\n");
        sb.Append("        </zone>\n      </zones>\n    </dashboard>\n  </dashboards>\n</workbook>\n");
        return sb.ToString();
    }
}
