using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Tabkit.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Regression tests for the TWB XSD validator. The vendored schema is
/// Tableau's official 2026.1 release; our handcrafted corpus targets older
/// Tableau versions (~18.1 / 2024.3), so we expect those to surface
/// validation warnings/errors — that's information, not a failure. The
/// hard assertions are:
///   - validator loads + compiles the XSD without throwing
///   - validator returns a structured report (never null, never throws on a
///     well-formed XML input)
///   - a known-syntactically-broken input is flagged as IsValid=false
/// </summary>
public class TwbXsdValidationTests
{
    private readonly ITestOutputHelper _output;
    public TwbXsdValidationTests(ITestOutputHelper output) => _output = output;

    private static string XsdPath => Path.Combine(TestFixtures.Root, "schemas", "twb_2026.1.0.xsd");

    [Fact]
    public void Validator_Loads_The_Vendored_XSD_Without_Throwing()
    {
        File.Exists(XsdPath).Should().BeTrue($"vendored XSD must exist at {XsdPath}");
        var validator = TwbXsdValidator.FromFile(XsdPath);
        validator.Should().NotBeNull();
    }

    [Theory]
    [InlineData("clean.twb")]
    [InlineData("deprecated.twb")]
    [InlineData("broken-refs.twb")]
    [InlineData("legacy-version.twb")]
    [InlineData("pii-heavy.twb")]
    [InlineData("nested-dashboards.twb")]
    public void Validator_Produces_A_Report_For_Each_Corpus_Fixture(string filename)
    {
        var path = TestFixtures.Corpus(filename);
        if (!File.Exists(path))
        {
            _output.WriteLine($"skip: {filename} not found");
            return;
        }
        var validator = TwbXsdValidator.FromFile(XsdPath);
        var report = validator.Validate(path);

        report.Should().NotBeNull();
        report.Source.Should().Be(path);
        _output.WriteLine($"{filename}: IsValid={report.IsValid}, " +
                          $"errors={report.ErrorCount}, warnings={report.WarningCount}");
        foreach (var f in report.Findings.Take(3))
            _output.WriteLine($"  [{f.Severity}] line {f.LineNumber}: {f.Message}");
    }

    [Fact]
    public void Validator_Flags_Malformed_Xml_As_Invalid()
    {
        var validator = TwbXsdValidator.FromFile(XsdPath);
        var report = validator.ValidateXml("<workbook><<<not valid xml", source: "test-malformed");
        report.IsValid.Should().BeFalse();
        report.ErrorCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Validator_Validates_External_Corpus_If_Present()
    {
        var externalDir = Path.Combine(TestFixtures.Root, "external", "server-client");
        if (!Directory.Exists(externalDir))
        {
            _output.WriteLine("external corpus not fetched — skipping");
            return;
        }

        var validator = TwbXsdValidator.FromFile(XsdPath);
        foreach (var path in Directory.EnumerateFiles(externalDir, "*.twb")
                                       .Concat(Directory.EnumerateFiles(externalDir, "*.twbx")))
        {
            var report = validator.Validate(path);
            _output.WriteLine($"{Path.GetFileName(path)}: IsValid={report.IsValid}, " +
                              $"errors={report.ErrorCount}, warnings={report.WarningCount}");
        }
    }
}
