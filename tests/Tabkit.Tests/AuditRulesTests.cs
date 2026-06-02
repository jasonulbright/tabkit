using System.Linq;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Audit.Packs;
using Tabkit.Core.Loading;

namespace Tabkit.Tests;

public class AuditRulesTests
{
    [Fact]
    public void AUD001_UnusedWorksheets_Fires_OnStressFixture()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new AuditPack.UnusedWorksheets().Check(wb).ToList();
        findings.Select(f => f.Message).Should().Contain(m => m.Contains("Never Placed"));
        findings.Select(f => f.Message).Should().Contain(m => m.Contains("Local Scratch"));
        findings.Should().AllSatisfy(f => f.RuleId.Should().Be("AUD001"));
    }

    [Fact]
    public void AUD002_OrphanDataSources_Fires()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new AuditPack.OrphanDataSources().Check(wb).ToList();
        findings.Select(f => f.Message).Should().Contain(m => m.Contains("federated.orphan"));
    }

    [Fact]
    public void AUD003_DeprecatedFunctions_FlagsAttr()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new AuditPack.DeprecatedFunctions().Check(wb).ToList();
        findings.Should().HaveCountGreaterThanOrEqualTo(2);
        findings.Should().AllSatisfy(f =>
            f.Properties.Should().Contain(new System.Collections.Generic.KeyValuePair<string, string>("function", "ATTR")));
    }

    [Fact]
    public void AUD004_DuplicateCalcs_OneFinding_TwoOccurrences()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new AuditPack.DuplicateCalculatedFields().Check(wb).ToList();
        findings.Should().ContainSingle();
        findings[0].Properties["count"].Should().Be("2");
    }

    [Fact]
    public void AUD005_BrokenColumnReference_IsError()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new AuditPack.BrokenColumnReferences().Check(wb).ToList();
        findings.Should().Contain(f => f.Properties.GetValueOrDefault("referenced") == "[nonexistent_column]");
        findings.Should().AllSatisfy(f => f.Severity.Should().Be(Severity.Error));
    }

    [Fact]
    public void AUD006_OldVersion_FiresFor_LegacyVersion()
    {
        var wb = TwbLoader.Load(TestFixtures.Corpus("legacy-version.twb"));
        var findings = new AuditPack.OldWorkbookVersion().Check(wb).ToList();
        findings.Should().ContainSingle();
        findings[0].Properties["version"].Should().Be("9.0");
    }

    [Fact]
    public void AUD006_DoesNotFire_OnCurrentVersion()
    {
        var wb = TwbLoader.Load(TestFixtures.MinimalTwb);
        var findings = new AuditPack.OldWorkbookVersion().Check(wb).ToList();
        findings.Should().BeEmpty();
    }

    [Fact]
    public void RuleRegistry_Default_LoadsAllPacks()
    {
        var reg = RuleRegistry.Default();
        var ids = reg.Rules.Select(r => r.Meta.Id).ToList();
        ids.Should().Contain("AUD001");
        ids.Should().Contain("AUD006");
        ids.Should().Contain("GOV001");
        ids.Should().Contain("GOV005");
    }
}
