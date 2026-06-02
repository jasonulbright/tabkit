using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Loading;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Pathological edge cases — unicode field names, deeply nested calcs.
/// These are inputs the parser+audit pipeline must handle without crashing
/// or mangling, even if the underlying workbook is unusual.
/// </summary>
public class PathologicalFixtureTests
{
    private readonly ITestOutputHelper _output;
    public PathologicalFixtureTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Unicode_Field_Names_Roundtrip_Without_Mangling()
    {
        var wb = TwbLoader.Load(TestFixtures.Corpus("pathological_unicode.twb"));
        wb.DataSources.Should().HaveCount(1);
        var fields = wb.DataSources[0].Fields.Select(f => f.Name).ToList();

        fields.Should().Contain("[🎯 emoji column]");
        fields.Should().Contain("[العنوان]");
        fields.Should().Contain("[café]");
        fields.Should().Contain("[销售额度]");
        fields.Should().Contain(f => f.Length > 100, "long-name field should round-trip in full");

        // Audit shouldn't throw on unicode field names.
        var findings = RuleRegistry.Default().Run(wb);
        _output.WriteLine($"unicode fixture findings: {findings.Count}");
    }

    [Fact]
    public void Deeply_Nested_Calc_Parses_And_Audits_Without_Stack_Issues()
    {
        var wb = TwbLoader.Load(TestFixtures.Corpus("pathological_deeply_nested_calc.twb"));
        wb.DataSources.Should().HaveCount(1);
        var calcs = wb.DataSources[0].CalculatedFields.ToList();
        calcs.Should().HaveCount(1);
        calcs[0].Calculation!.Formula.Should().Contain("IIF").And.Contain("256");

        var findings = RuleRegistry.Default().Run(wb);
        // Should not include AUD005 (broken column ref) — the only column ref
        // is [x] which is defined.
        findings.Should().NotContain(f => f.RuleId == "AUD005");
        _output.WriteLine($"nested-calc fixture findings: {string.Join(", ", findings.Select(f => f.RuleId))}");
    }

    [Fact]
    public void Circular_Calc_Refs_Parse_Without_Throwing_And_AUD005_Does_Not_Fire()
    {
        var wb = TwbLoader.Load(TestFixtures.Corpus("pathological_circular_calc_refs.twb"));
        wb.DataSources.Should().HaveCount(1);
        var fields = wb.DataSources[0].Fields.Select(f => f.Name).ToList();
        fields.Should().Contain(new[] { "[a]", "[b]" });

        var calcs = wb.DataSources[0].CalculatedFields.ToList();
        calcs.Should().HaveCount(2);
        // Each calc references the other field, both of which exist.
        calcs.Should().Contain(c => c.Name == "[a]" && c.Calculation!.Formula == "[b] + 1");
        calcs.Should().Contain(c => c.Name == "[b]" && c.Calculation!.Formula == "[a] - 1");

        var findings = RuleRegistry.Default().Run(wb);
        // AUD005 (broken column ref) must NOT fire — both [a] and [b] are defined
        // even though the references form a cycle. Cycle detection is out of scope.
        findings.Should().NotContain(f => f.RuleId == "AUD005");
        _output.WriteLine($"circular-refs fixture findings: {string.Join(", ", findings.Select(f => f.RuleId))}");
    }

    [Fact]
    public void Namespace_Prefixed_Workbook_Loads_All_Children()
    {
        // Loader must use LocalName-based matching so prefixed children
        // (e.g. <t:datasources>) are discovered.
        var wb = TwbLoader.Load(TestFixtures.Corpus("pathological_namespace_prefix.twb"));
        wb.DataSources.Should().HaveCount(1, "loader should find <t:datasource> via LocalName match");
        wb.DataSources[0].Name.Should().Be("federated.prefixed");
        wb.DataSources[0].Fields.Should().HaveCount(2);
        wb.DataSources[0].Connections.Should().HaveCount(1);
        wb.Worksheets.Should().HaveCount(1);
        wb.Worksheets[0].DataSourceNames.Should().Contain("federated.prefixed");
        wb.Dashboards.Should().HaveCount(1);
        wb.Dashboards[0].Zones.Should().NotBeEmpty();

        // Audit pipeline must not throw on prefixed workbooks.
        var findings = RuleRegistry.Default().Run(wb);
        _output.WriteLine($"namespace-prefix fixture findings: {findings.Count}");
    }

    [Fact]
    public void Three_Byte_Encodings_Load_To_Equivalent_Workbook_Records()
    {
        // The BOM-UTF8 and UTF-16 LE variants are materialized from the canonical
        // no-BOM UTF-8 fixture at test time. Shipping them as static files would
        // require .gitattributes to mark them binary, which violates the
        // public-repo "no .gitignore / .gitattributes" convention; generating
        // here keeps the bytes deterministic across clones.
        var canonicalPath = TestFixtures.Corpus("pathological_no_bom_utf8.twb");
        var canonicalText = File.ReadAllText(canonicalPath, Encoding.UTF8);

        var bomPath   = MaterializeBytes("bom_utf8.twb",   new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(canonicalText));
        var u16Text   = canonicalText.Replace("encoding='utf-8'", "encoding='utf-16'");
        var u16Bytes  = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
                            .Concat(new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetBytes(u16Text))
                            .ToArray();
        var u16Path   = MaterializeBytes("utf16le.twb", u16Bytes);

        try
        {
            var noBom = TwbLoader.Load(canonicalPath);
            var bom   = TwbLoader.Load(bomPath);
            var utf16 = TwbLoader.Load(u16Path);

            foreach (var wb in new[] { noBom, bom, utf16 })
            {
                wb.DataSources.Should().HaveCount(1);
                wb.DataSources[0].Name.Should().Be("federated.encoding");
                wb.DataSources[0].Caption.Should().Be("Encoding probe");
                wb.DataSources[0].Fields.Should().HaveCount(1);
                wb.DataSources[0].Fields[0].Name.Should().Be("[label]");
                wb.Worksheets.Should().HaveCount(1);
                wb.Worksheets[0].Name.Should().Be("Sheet");
                wb.Dashboards.Should().HaveCount(1);
                wb.Dashboards[0].Name.Should().Be("Main");
            }
            _output.WriteLine("encoding probe: no-BOM UTF-8 (committed) + BOM UTF-8 + UTF-16 LE (derived in-test) all loaded to equivalent records");
        }
        finally
        {
            TryDelete(bomPath);
            TryDelete(u16Path);
        }
    }

    [Fact]
    public void Mixed_Line_Endings_Load_Uniformly()
    {
        // Derived at test time from the canonical no-BOM UTF-8 fixture by
        // rotating CRLF / LF / CR endings across lines. Shipping a static
        // file would risk git's autocrlf normalization (no .gitattributes
        // per the public-repo convention).
        var canonicalText = File.ReadAllText(TestFixtures.Corpus("pathological_no_bom_utf8.twb"), Encoding.UTF8);
        var lines = canonicalText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var endings = new[] { "\r\n", "\n", "\r" };
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Length - 1) sb.Append(endings[i % 3]);
        }
        var path = MaterializeBytes("mixed_line_endings.twb", new UTF8Encoding(false).GetBytes(sb.ToString()));
        try
        {
            var wb = TwbLoader.Load(path);
            wb.DataSources.Should().HaveCount(1);
            wb.DataSources[0].Name.Should().Be("federated.encoding");
            wb.DataSources[0].Fields.Should().ContainSingle(f => f.Name == "[label]");
            wb.Worksheets.Should().HaveCount(1);
            wb.Dashboards.Should().HaveCount(1);

            // Sanity: confirm the file actually contained mixed endings on disk.
            var bytes = File.ReadAllBytes(path);
            var crCount = bytes.Count(b => b == 0x0D);
            var lfCount = bytes.Count(b => b == 0x0A);
            crCount.Should().BeGreaterThan(0, "fixture must contain CR bytes to be a meaningful test");
            lfCount.Should().BeGreaterThan(0, "fixture must contain LF bytes to be a meaningful test");
            _output.WriteLine($"mixed line endings: CR={crCount} LF={lfCount} (parser normalized correctly)");
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string MaterializeBytes(string suffix, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"tabkit-pathological-{Guid.NewGuid():N}-{suffix}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Comments_In_Calculation_Element_Do_Not_Affect_Formula_Extraction()
    {
        var wb = TwbLoader.Load(TestFixtures.Corpus("pathological_comments_in_calc.twb"));
        wb.DataSources.Should().HaveCount(1);
        var calc = wb.DataSources[0].CalculatedFields.Single();
        calc.Name.Should().Be("[doubled]");
        calc.Calculation!.Formula.Should().Be("[x] * 2",
            "formula attribute must be read intact even when the calculation element has a child XML comment");

        var findings = RuleRegistry.Default().Run(wb);
        findings.Should().NotContain(f => f.RuleId == "AUD005",
            "[x] is defined, so the column ref resolves cleanly");
        _output.WriteLine($"comments-in-calc fixture findings: {string.Join(", ", findings.Select(f => f.RuleId))}");
    }

    [Fact]
    public void Very_Long_Field_Names_Survive_Loader_And_Calc_Reference_Regex()
    {
        var wb = TwbLoader.Load(TestFixtures.Corpus("pathological_very_long_names.twb"));
        wb.DataSources.Should().HaveCount(1);
        var fields = wb.DataSources[0].Fields.ToList();
        fields.Should().HaveCount(2);

        var longField = fields.Single(f => f.Name.StartsWith("[a"));
        longField.Name.Length.Should().BeGreaterThan(1000,
            "the 1000+ char bracketed name must round-trip without truncation");

        var calc = fields.Single(f => f.Name == "[ref_long]");
        calc.Calculation!.Formula.Should().Contain(longField.Name,
            "the column-ref regex must match the full bracketed name inside the calc");

        var findings = RuleRegistry.Default().Run(wb);
        // The long-name field IS defined, so AUD005 must not fire on [ref_long].
        findings.Should().NotContain(f => f.RuleId == "AUD005");
        _output.WriteLine($"very-long-names fixture findings: {string.Join(", ", findings.Select(f => f.RuleId))}");
    }

    [Fact]
    public void Zip_Bomb_Twbx_Is_Rejected_Before_Allocating_Memory()
    {
        // Regression for Codex R1 finding #5: a .twbx whose inner .twb member
        // compresses far better than real Tableau XML (e.g. all-zero payload)
        // must be rejected by TwbxSafety's ratio cap before the loader
        // allocates the uncompressed bytes.
        var zipPath = MaterializeZipBomb(uncompressedBytes: 1024 * 1024); // 1 MB of zeros → ~1000:1
        try
        {
            Action act = () => TwbLoader.Load(zipPath);
            act.Should().Throw<TwbParseException>()
                .WithMessage("*compression ratio*",
                    "TwbxSafety must reject zip-bomb shaped .twbx before extraction");
        }
        finally { TryDelete(zipPath); }
    }

    private static string MaterializeZipBomb(int uncompressedBytes)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"tabkit-pathological-{Guid.NewGuid():N}-zipbomb.twbx");
        using var fs = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(
            fs, System.IO.Compression.ZipArchiveMode.Create);
        var entry = zip.CreateEntry("workbook.twb", System.IO.Compression.CompressionLevel.SmallestSize);
        using var es = entry.Open();
        var zeros = new byte[uncompressedBytes];
        es.Write(zeros, 0, zeros.Length);
        return path;
    }
}
