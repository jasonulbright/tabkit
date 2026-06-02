using System.IO;
using System.Linq;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Loading;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// One-rule-each corpus: each fixture is constructed to trip exactly one
/// rule. These are the canonical regression-test inputs — if a rule's
/// behavior changes, the corresponding fixture's finding count must change
/// with intent, never accidentally. Cross-fixture contamination (e.g., GOV001
/// fixture also triggering AUD005) is the failure mode this catches.
/// </summary>
public class OneRuleEachFixtureTests
{
    private readonly ITestOutputHelper _output;
    public OneRuleEachFixtureTests(ITestOutputHelper output) => _output = output;

    private static string FixturePath(string name)
        => Path.Combine(TestFixtures.Root, "corpus", "one-rule-each", name);

    [Theory]
    [InlineData("aud001_unused_worksheet.twb",  "AUD001")]
    [InlineData("aud002_orphan_datasource.twb", "AUD002")]
    [InlineData("aud003_deprecated_function.twb", "AUD003")]
    [InlineData("aud004_duplicate_calc.twb",   "AUD004")]
    [InlineData("aud005_broken_column_ref.twb", "AUD005")]
    [InlineData("aud006_old_version.twb",       "AUD006")]
    [InlineData("gov001_embedded_password.twb", "GOV001")]
    [InlineData("gov002_external_server.twb",   "GOV002")]
    [InlineData("gov003_pii_pattern.twb",       "GOV003")]
    [InlineData("gov004_username_rls.twb",      "GOV004")]
    [InlineData("gov005_user_profile_path.twb", "GOV005")]
    public void Fixture_Trips_Exactly_Its_Target_Rule(string fixture, string expectedRuleId)
    {
        var path = FixturePath(fixture);
        File.Exists(path).Should().BeTrue($"missing one-rule-each fixture at {path}");

        var wb = TwbLoader.Load(path);
        var findings = RuleRegistry.Default().Run(wb);

        var byRule = findings.GroupBy(f => f.RuleId).ToDictionary(g => g.Key, g => g.Count());
        _output.WriteLine($"{fixture}: {string.Join(", ", byRule.Select(kv => kv.Key + ":" + kv.Value))}");

        findings.Should().NotBeEmpty($"{expectedRuleId} should fire on its own fixture");
        findings.Should().OnlyContain(f => f.RuleId == expectedRuleId,
            $"only {expectedRuleId} should fire — any other RuleId is cross-contamination");
    }
}
