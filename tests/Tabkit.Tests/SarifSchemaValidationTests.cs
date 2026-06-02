using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NJsonSchema;
using NJsonSchema.Validation;
using Tabkit.Core.Audit;
using Tabkit.Core.Audit.Packs;
using Tabkit.Core.Loading;
using Tabkit.Core.Output;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Regression tests proving Tabkit's <see cref="SarifOutput"/> emits SARIF
/// 2.1.0 that validates clean against the official OASIS schema. The schema
/// is vendored at <c>tests/fixtures/schemas/sarif-2.1.0.json</c>.
/// </summary>
public class SarifSchemaValidationTests
{
    private readonly ITestOutputHelper _output;
    public SarifSchemaValidationTests(ITestOutputHelper output) => _output = output;

    private static string SarifSchemaPath => Path.Combine(TestFixtures.Root, "schemas", "sarif-2.1.0.json");

    private static async Task<JsonSchema> LoadSchemaAsync()
    {
        File.Exists(SarifSchemaPath).Should().BeTrue($"vendored SARIF schema must exist at {SarifSchemaPath}");
        return await JsonSchema.FromFileAsync(SarifSchemaPath);
    }

    [Fact]
    public async Task Sarif_Schema_Loads_Without_Throwing()
    {
        var schema = await LoadSchemaAsync();
        schema.Should().NotBeNull();
        schema.Title.Should().Contain("SARIF");
    }

    [Fact]
    public async Task Empty_Finding_Set_Validates_Clean()
    {
        var schema = await LoadSchemaAsync();
        var json = SarifOutput.FindingsToSarif(System.Array.Empty<Finding>(),
            RuleRegistry.Default().Rules);
        var errors = schema.Validate(json);
        DumpErrors(errors, "empty");
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Real_Workbook_Output_Validates_Clean()
    {
        var schema = await LoadSchemaAsync();
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var registry = RuleRegistry.Default();
        var findings = registry.Run(wb);
        var json = SarifOutput.FindingsToSarif(findings, registry.Rules);

        var errors = schema.Validate(json);
        DumpErrors(errors, $"stress.twb ({findings.Count} findings)");
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_Severity_Validates_Clean()
    {
        var schema = await LoadSchemaAsync();
        var rules = RuleRegistry.Default().Rules;

        var findings = new List<Finding>
        {
            new(RuleId: "AUD001", Severity: Severity.Info,
                Title: "Info one", Message: "Info-severity sample",
                WorkbookPath: "C:/sample/info.twb"),
            new(RuleId: "GOV002", Severity: Severity.Warn,
                Title: "Warn one", Message: "Warn-severity sample",
                WorkbookPath: "C:/sample/warn.twb",
                Properties: new Dictionary<string, string> { ["server"] = "sql.example.com" }),
            new(RuleId: "GOV001", Severity: Severity.Error,
                Title: "Error one", Message: "Error-severity sample with remediation",
                WorkbookPath: "C:/sample/error.twb",
                Remediation: "Strip the embedded credential."),
        };

        var json = SarifOutput.FindingsToSarif(findings, rules);
        var errors = schema.Validate(json);
        DumpErrors(errors, "per-severity sample");
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Output_Without_Rule_Descriptors_Still_Validates()
    {
        // SarifOutput's rules arg is optional; verify the schema accepts a
        // run with implicit rule descriptors (built from finding RuleIds only).
        var schema = await LoadSchemaAsync();
        var findings = new[]
        {
            new Finding(
                RuleId: "AUD002",
                Severity: Severity.Warn,
                Title: "No-rules path",
                Message: "Rules arg omitted",
                WorkbookPath: "C:/sample/rulesless.twb"),
        };
        var json = SarifOutput.FindingsToSarif(findings);
        var errors = schema.Validate(json);
        DumpErrors(errors, "rules-omitted");
        errors.Should().BeEmpty();
    }

    private void DumpErrors(ICollection<ValidationError> errors, string label)
    {
        _output.WriteLine($"{label}: {errors.Count} validation error(s)");
        foreach (var e in errors.Take(8))
            _output.WriteLine($"  [{e.Kind}] {e.Path}: {e.Property}");
    }
}
