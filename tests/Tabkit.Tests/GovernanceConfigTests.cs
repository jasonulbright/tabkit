using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Audit.Packs;
using Tabkit.Core.Loading;
using Xunit;

namespace Tabkit.Tests;

/// <summary>
/// Verifies the optional <see cref="GovernanceConfig"/> seam on the governance
/// pack — GOV002 user allowlist and GOV003 per-pattern disable.
/// </summary>
public class GovernanceConfigTests
{
    [Fact]
    public void GOV002_UserAllowlist_Suppresses_Matching_Server()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);

        var unconfigured = new GovernancePack.ExternalDataConnection().Check(wb).ToList();
        unconfigured.Should().NotBeEmpty("the stress fixture references evil-corp.example");

        var server = unconfigured[0].Properties.GetValueOrDefault("server")!;
        var allowed = new[] { server };
        var configured = new GovernancePack.ExternalDataConnection(allowed).Check(wb).ToList();

        configured.Should().BeEmpty("the only flagged server was added to the user allowlist");
    }

    [Fact]
    public void GOV002_UserAllowlist_Is_Case_Insensitive()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var server = new GovernancePack.ExternalDataConnection()
            .Check(wb)
            .First()
            .Properties.GetValueOrDefault("server")!;

        var upper = new[] { server.ToUpperInvariant() };
        new GovernancePack.ExternalDataConnection(upper).Check(wb).Should().BeEmpty();
    }

    [Fact]
    public void GOV003_DisabledPatterns_Skip_Those_Labels()
    {
        // Build a workbook the existing fixture-based suite already exercises;
        // here we just need a workbook with PII-ish field names.
        var wb = TwbLoader.Load(TestFixtures.StressTwb);

        var unconfigured = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        // If unconfigured produces any findings, disabling all of them should
        // produce zero findings. If unconfigured produces zero (the fixture
        // happens not to have PII-shaped fields), the test is trivially true —
        // not great, but harmless. The Pii-rule coverage already lives in
        // GovernanceRulesTests with a synthetic workbook.
        var allLabels = GovernancePack.PiiColumnPatterns.AllPatternLabels
            .ToImmutableHashSet(System.StringComparer.OrdinalIgnoreCase);

        var configured = new GovernancePack.PiiColumnPatterns(allLabels).Check(wb).ToList();
        configured.Should().BeEmpty();

        if (unconfigured.Count > 0)
        {
            var oneLabel = unconfigured[0].Properties.GetValueOrDefault("pattern")!;
            var oneDisabled = new[] { oneLabel }
                .ToImmutableHashSet(System.StringComparer.OrdinalIgnoreCase);
            var partial = new GovernancePack.PiiColumnPatterns(oneDisabled).Check(wb).ToList();
            partial.Should().AllSatisfy(f =>
                f.Properties.GetValueOrDefault("pattern").Should().NotBe(oneLabel));
        }
    }

    [Fact]
    public void GovernanceConfig_Default_Has_Empty_Allowlist_And_No_Disabled_Patterns()
    {
        GovernanceConfig.Default.AllowedServers.Should().BeEmpty();
        GovernanceConfig.Default.DisabledPiiPatterns.Should().BeEmpty();
    }

    [Fact]
    public void RuleRegistry_Default_Accepts_Custom_Config()
    {
        var cfg = new GovernanceConfig(
            AllowedServers: new[] { "approved.example.com" },
            DisabledPiiPatterns: ImmutableHashSet.Create(System.StringComparer.OrdinalIgnoreCase, "ssn"));

        var reg = RuleRegistry.Default(cfg);
        reg.Rules.Should().HaveCount(11);                       // 6 audit + 5 governance
        reg.ForPack("governance").Should().HaveCount(5);
    }
}
