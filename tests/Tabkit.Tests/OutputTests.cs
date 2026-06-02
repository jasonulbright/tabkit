using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Loading;
using Tabkit.Core.Output;

namespace Tabkit.Tests;

public class OutputTests
{
    private static (System.Collections.Generic.IReadOnlyList<Finding> findings, System.Collections.Generic.IReadOnlyList<IRule> rules) RunStress()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var reg = RuleRegistry.Default();
        return (reg.Run(wb), reg.Rules);
    }

    [Fact]
    public void Json_NeverContainsPassword()
    {
        var (findings, _) = RunStress();
        var blob = JsonOutput.FindingsToJson(findings);
        blob.Should().NotContain("hunter2");
    }

    [Fact]
    public void Sarif_DoesNotEmit_InvalidFixesBlock()
    {
        var (findings, rules) = RunStress();
        var sarif = SarifOutput.FindingsToSarif(findings, rules);
        var doc = JsonDocument.Parse(sarif);
        var results = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results");
        foreach (var r in results.EnumerateArray())
            r.TryGetProperty("fixes", out _).Should().BeFalse(
                "Codex flagged the missing-artifactChanges schema break");
    }

    [Fact]
    public void Sarif_RuleNames_AreIdentifierLike()
    {
        var (findings, rules) = RunStress();
        var sarif = SarifOutput.FindingsToSarif(findings, rules);
        var doc = JsonDocument.Parse(sarif);
        var ruleArr = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules");
        var name = new Regex(@"^[A-Za-z][A-Za-z0-9_-]*$");
        foreach (var r in ruleArr.EnumerateArray())
            name.IsMatch(r.GetProperty("name").GetString()!).Should().BeTrue();
    }

    [Fact]
    public void Sarif_NeverContainsPassword()
    {
        var (findings, rules) = RunStress();
        var blob = SarifOutput.FindingsToSarif(findings, rules);
        blob.Should().NotContain("hunter2");
    }

    [Fact]
    public void Sarif_AbsolutePath_Emits_File_Uri_Not_Scheme_C()
    {
        // Regression for Codex R1 finding #9: an absolute Windows path used to
        // emit as "C:/projects/foo.twb" — schema-valid but semantically a URI
        // with scheme "C:", not a file URI. Must now emit a real file:/// URI.
        var (findings, rules) = RunStress();
        var sarif = SarifOutput.FindingsToSarif(findings, rules);
        var doc = JsonDocument.Parse(sarif);
        var results = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results");
        foreach (var r in results.EnumerateArray())
        {
            var uri = r.GetProperty("locations")[0]
                .GetProperty("physicalLocation")
                .GetProperty("artifactLocation")
                .GetProperty("uri").GetString();
            // Stress workbook path is rooted in the test working tree on every
            // platform; the URI must be absolute and use file:// scheme.
            uri.Should().NotBeNullOrEmpty();
            uri!.Should().StartWith("file:///",
                "absolute paths must serialize as proper file URIs per GitHub SARIF guidance");
            uri.Should().NotMatch("^[A-Za-z]:/.*",
                "must not emit bare drive-letter as URI scheme");
        }
    }

    [Fact]
    public void Html_Renders_WithFindings()
    {
        var (findings, rules) = RunStress();
        var html = HtmlOutput.FindingsToHtml(findings, rules, "Stress test report");
        html.Should().Contain("<!doctype html>");
        html.Should().Contain("Stress test report");
        html.Should().NotContain("hunter2");
    }

    [Fact]
    public void Html_EmptyState_Renders()
    {
        var html = HtmlOutput.FindingsToHtml(System.Array.Empty<Finding>(), null, "Clean report");
        html.Should().Contain("All rules passed");
    }
}
