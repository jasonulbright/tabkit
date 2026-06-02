using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Tabkit.Core.Audit;
using Tabkit.Core.Audit.Packs;
using Tabkit.Core.Loading;
using Tabkit.Core.Model;

namespace Tabkit.Tests;

public class GovernanceRulesTests
{
    [Fact]
    public void GOV001_EmbeddedCredentials_FiresAsError()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new GovernancePack.EmbeddedCredentials().Check(wb).ToList();
        findings.Should().ContainSingle();
        findings[0].Severity.Should().Be(Severity.Error);
        findings[0].Message.Should().NotContain("hunter2"); // value must never leak
    }

    [Fact]
    public void GOV002_ExternalServer_Fires()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new GovernancePack.ExternalDataConnection().Check(wb).ToList();
        findings.Should().HaveCount(2);
        findings.Should().AllSatisfy(f =>
            f.Properties.GetValueOrDefault("server").Should().Contain("evil-corp.example"));
    }

    [Fact]
    public void GOV003_PiiPatterns_Catches_FirstName_LastName_Ip()
    {
        var wb = BuildSyntheticWorkbook(
            ("[first_name]", "First Name"),
            ("[last_name]", "Last Name"),
            ("[client_ip]", "Client IP Address"));
        var findings = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        var patterns = findings.Select(f => f.Properties["pattern"]).ToHashSet();
        patterns.Should().Contain("personal_name");
        patterns.Should().Contain("ip_address");
    }

    [Fact]
    public void GOV003_DoesNotFalsePositive_OnCostCenter()
    {
        var wb = BuildSyntheticWorkbook(
            ("[cost_center]", "Cost Center"),
            ("[cost_center_number]", "Cost Center Number"));
        var findings = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        findings.Should().BeEmpty();
    }

    [Fact]
    public void GOV003_StillCatches_RealCreditCardFields()
    {
        var wb = BuildSyntheticWorkbook(
            ("[credit_card]",            "Credit Card"),
            ("[cc_num]",                 "CC Num"),
            ("[primary_account_number]", "Primary Account Number"),
            ("[pan_number]",             "PAN Number"));
        var findings = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        findings.Should().HaveCount(4);
        findings.Should().AllSatisfy(f => f.Properties["pattern"].Should().Be("credit_card"));
    }

    [Fact]
    public void GOV003_Catches_PrefixedPii_With_Underscore_Boundary()
    {
        // Regression for Codex R1 finding #4: regex `\b` doesn't fire across
        // underscores (underscore is a word char). Prefixed identifiers used
        // to slip past every pattern. Captions are intentionally empty so the
        // identifier alone has to carry the match.
        // NB: [client_ip] is intentionally not in this set — see
        // GOV003_DoesNotFalsePositive_OnIntellectualPropertyFields for why
        // bare "ip" is no longer matched.
        var wb = BuildSyntheticWorkbook(
            ("[customer_email]",               ""),
            ("[user_phone]",                   ""),
            ("[customer_credit_card_number]",  ""));
        var findings = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        var patterns = findings.Select(f => f.Properties["pattern"]).ToHashSet();
        patterns.Should().Contain("email");
        patterns.Should().Contain("phone");
        patterns.Should().Contain("credit_card");
        findings.Should().HaveCount(3);
    }

    [Fact]
    public void GOV003_DoesNotFalsePositive_OnIntellectualPropertyFields()
    {
        // Regression for Codex R1 round-2 finding: R1's widening of the IP
        // regex to bare `\bip\b` over-flagged business intellectual-property
        // fields. With the regex narrowed back to address-form variants
        // (ip[\s_-]?address, ipv?[46][\s_-]?address), these stay clean.
        var wb = BuildSyntheticWorkbook(
            ("[ip_owner]",          "IP Owner"),
            ("[ip_license_status]", "IP License Status"),
            ("[shipping_id]",       "Shipping ID"));
        var findings = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        findings.Should().BeEmpty();
    }

    [Fact]
    public void GOV003_StillCatches_Explicit_IpAddress_Fields()
    {
        var wb = BuildSyntheticWorkbook(
            ("[ip_address]", "IP Address"),
            ("[ip_addr]",    "IP Addr"),
            ("[ipv4_address]", "IPv4 Address"));
        var findings = new GovernancePack.PiiColumnPatterns().Check(wb).ToList();
        findings.Should().AllSatisfy(f => f.Properties["pattern"].Should().Be("ip_address"));
        findings.Should().HaveCount(3);
    }

    [Fact]
    public void GOV004_UsernameRls_Fires()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new GovernancePack.UsernameBasedRowLevelSecurity().Check(wb).ToList();
        findings.Should().ContainSingle();
        findings[0].Message.Should().Contain("[me_only]");
    }

    [Fact]
    public void GOV005_UserProfilePath_Fires()
    {
        var wb = TwbLoader.Load(TestFixtures.StressTwb);
        var findings = new GovernancePack.HardcodedUserProfilePath().Check(wb).ToList();
        findings.Should().ContainSingle();
        findings[0].Properties.GetValueOrDefault("path").Should().Contain("jane.doe");
    }

    private static Workbook BuildSyntheticWorkbook(params (string Name, string Caption)[] fields)
    {
        var flds = fields.Select(t => new Field(t.Name, Caption: t.Caption, Datatype: "string", Role: "dimension"))
            .ToImmutableArray();
        return new Workbook(
            SourcePath: "synthetic.twb",
            Version: "18.1",
            SourceBuild: null,
            SourcePlatform: null,
            DataSources: ImmutableArray.Create(new DataSource("ds", Fields: flds)));
    }
}
