using System.Collections.Generic;
using System.Text.RegularExpressions;
using Tabkit.Core.Model;

namespace Tabkit.Core.Audit.Packs;

/// <summary>
/// Governance pack — policy-as-code rules for compliance and security review.
/// Rule IDs use the <c>GOV###</c> prefix.
/// </summary>
public static partial class GovernancePack
{
    public static IEnumerable<IRule> Rules(GovernanceConfig? config = null)
    {
        config ??= GovernanceConfig.Default;
        return new IRule[]
        {
            new EmbeddedCredentials(),
            new ExternalDataConnection(config.AllowedServers),
            new PiiColumnPatterns(config.DisabledPiiPatterns),
            new UsernameBasedRowLevelSecurity(),
            new HardcodedUserProfilePath(),
        };
    }

    // ── GOV001 ────────────────────────────────────────────────────────────────
    public sealed class EmbeddedCredentials : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "GOV001",
            Title: "Connection has embedded password",
            Severity: Severity.Error,
            Description: "Data source connection stores a password in the workbook XML.",
            Pack: "governance");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
                foreach (var conn in ds.Connections)
                {
                    // Presence of password is the signal — value never leaks.
                    if (!string.IsNullOrEmpty(conn.Password))
                    {
                        yield return NewFinding(
                            wb,
                            message: $"Connection on data source '{ds.Name}' contains an embedded password.",
                            location: $"datasource:{ds.Name}/connection:{conn.ConnectionClass}",
                            remediation:
                                "Strip the embedded credential. Use a published data source with " +
                                "Tableau Server credentials, OS authentication, or a runtime prompt.");
                    }
                }
        }
    }

    // ── GOV002 ────────────────────────────────────────────────────────────────
    public sealed partial class ExternalDataConnection : RuleBase
    {
        private readonly HashSet<string> _userAllowlist;

        public ExternalDataConnection()
            : this(System.Array.Empty<string>()) { }

        public ExternalDataConnection(IReadOnlyList<string> userAllowlist)
        {
            _userAllowlist = new HashSet<string>(userAllowlist, System.StringComparer.OrdinalIgnoreCase);
        }

        public override RuleMeta Meta { get; } = new(
            Id: "GOV002",
            Title: "Connection targets a non-approved server",
            Severity: Severity.Warn,
            Description: "Connection server hostname is not on the configured allowlist.",
            Pack: "governance");

        [GeneratedRegex(@"^(.+\.)?internal\.example\.com$", RegexOptions.IgnoreCase)]
        private static partial Regex InternalExample();
        [GeneratedRegex(@"^localhost$", RegexOptions.IgnoreCase)]
        private static partial Regex Localhost();
        [GeneratedRegex(@"^127\.0\.0\.1$")]
        private static partial Regex Loopback();

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
                foreach (var conn in ds.Connections)
                {
                    if (string.IsNullOrEmpty(conn.Server)) continue;
                    if (InternalExample().IsMatch(conn.Server)) continue;
                    if (Localhost().IsMatch(conn.Server)) continue;
                    if (Loopback().IsMatch(conn.Server)) continue;
                    if (_userAllowlist.Contains(conn.Server)) continue;

                    yield return NewFinding(
                        wb,
                        message: $"Connection on '{ds.Name}' targets external server '{conn.Server}'.",
                        location: $"datasource:{ds.Name}/connection:{conn.ConnectionClass}",
                        remediation:
                            "Reconnect to an approved internal data source, or add the host to the " +
                            "allowlist if intentional.",
                        properties: ("server", conn.Server));
                }
        }
    }

    // ── GOV003 ────────────────────────────────────────────────────────────────
    public sealed partial class PiiColumnPatterns : RuleBase
    {
        private readonly IReadOnlySet<string> _disabled;

        public PiiColumnPatterns()
            : this(new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)) { }

        public PiiColumnPatterns(IReadOnlySet<string> disabledPatterns)
        {
            _disabled = disabledPatterns;
        }

        /// <summary>The pattern labels GOV003 ships with — exposed so callers can
        /// build a UI checkbox list, opt-out toggles, etc.</summary>
        public static IReadOnlyList<string> AllPatternLabels { get; } = new[]
        {
            "ssn", "dob", "email", "phone", "address",
            "credit_card", "medical", "personal_name", "ip_address",
        };

        public override RuleMeta Meta { get; } = new(
            Id: "GOV003",
            Title: "Field name matches a PII pattern",
            Severity: Severity.Warn,
            Description: "Field name suggests it carries personally identifiable information.",
            Pack: "governance");

        [GeneratedRegex(@"\bssn\b|\bsocial[\s_-]?security(?:[\s_-]?number)?\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternSsn();
        [GeneratedRegex(@"\bdob\b|\bdate[\s_-]?of[\s_-]?birth\b|\bbirth[\s_-]?date\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternDob();
        [GeneratedRegex(@"\bemail(?:[\s_-]?address)?\b|\be[\s_-]?mail\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternEmail();
        [GeneratedRegex(@"\bphone(?:[\s_-]?number)?\b|\bmobile(?:[\s_-]?number)?\b|\btelephone\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternPhone();
        [GeneratedRegex(@"\b(?:street|home|mailing|residential)[\s_-]?address\b|\b(?:postal|zip)[\s_-]?code\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternAddress();
        [GeneratedRegex(@"\b(?:credit[\s_-]?card|cc[\s_-]?num(?:ber)?|primary[\s_-]?account[\s_-]?number|(?:pan[\s_-]?number)|(?:card[\s_-]?pan))\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternCreditCard();
        [GeneratedRegex(@"\bmrn\b|\bmedical[\s_-]?record(?:[\s_-]?number)?\b|\bdiagnosis(?:[\s_-]?code)?\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternMedical();
        [GeneratedRegex(@"\b(?:first|last|middle|maiden|given|family|sur)[\s_-]?name\b|\bfull[\s_-]?name\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternPersonalName();
        // R1's widening to include bare \bip\b matched [client_ip] but over-flagged
        // business "intellectual property" fields like [ip_owner] and
        // [ip_license_status] (Codex R1 round-2 finding). Reverted to address-form
        // variants only — bare "IP" is too ambiguous outside a network context.
        [GeneratedRegex(@"\bip[\s_-]?address\b|\bip[\s_-]?addr\b|\bipv?[46][\s_-]?address\b", RegexOptions.IgnoreCase)]
        private static partial Regex PatternIpAddress();

        private static readonly (string Label, Regex Pattern)[] Patterns =
        {
            ("ssn",            PatternSsn()),
            ("dob",            PatternDob()),
            ("email",          PatternEmail()),
            ("phone",          PatternPhone()),
            ("address",        PatternAddress()),
            ("credit_card",    PatternCreditCard()),
            ("medical",        PatternMedical()),
            ("personal_name",  PatternPersonalName()),
            ("ip_address",     PatternIpAddress()),
        };

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
                foreach (var fld in ds.Fields)
                {
                    var candidates = new List<string>(2);
                    candidates.Add(fld.Name);
                    if (!string.IsNullOrEmpty(fld.Caption))
                        candidates.Add(fld.Caption);
                    // Strip brackets AND normalize underscore/hyphen to spaces.
                    // Regex `\b` doesn't fire on `_` (it's a word char), so
                    // prefixed identifiers like [customer_email] or
                    // [customer_credit_card_number] used to slip past every
                    // pattern. Normalizing here lets the existing `\b...\b`
                    // patterns fire correctly without each one having to opt
                    // into `[\W_]` boundaries (Codex R1 finding #4).
                    var blob = string.Join(" | ", candidates)
                        .Replace("[", "")
                        .Replace("]", "")
                        .Replace("_", " ")
                        .Replace("-", " ");

                    foreach (var (label, pattern) in Patterns)
                    {
                        if (_disabled.Contains(label)) continue;
                        if (pattern.IsMatch(blob))
                        {
                            yield return NewFinding(
                                wb,
                                message: $"Field '{fld.Name}' on '{ds.Name}' matches PII pattern '{label}'.",
                                location: $"datasource:{ds.Name}/field:{fld.Name}",
                                remediation:
                                    "Confirm the field is intended to ship to consumers. If not, " +
                                    "remove it from the workbook or move governance to the data source.",
                                properties: ("pattern", label));
                            break; // one finding per field
                        }
                    }
                }
        }
    }

    // ── GOV004 ────────────────────────────────────────────────────────────────
    public sealed class UsernameBasedRowLevelSecurity : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "GOV004",
            Title: "Calculated field implements ad-hoc row-level security",
            Severity: Severity.Warn,
            Description: "Calc uses USERNAME() / ISUSERNAME() / ISMEMBEROF() / FULLNAME() for access control.",
            Pack: "governance");

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
                foreach (var fld in ds.CalculatedFields)
                {
                    if (fld.Calculation is null) continue;
                    if (FormulaDetectors.UsernameFnPattern().IsMatch(fld.Calculation.Formula))
                    {
                        yield return NewFinding(
                            wb,
                            message: $"Calc '{fld.Name}' on '{ds.Name}' uses USERNAME()-style access control.",
                            location: $"datasource:{ds.Name}/field:{fld.Name}",
                            remediation:
                                "Real row-level security belongs in the data source (Tableau Server " +
                                "data source filters, RLS at the warehouse, or virtual connection " +
                                "policies). Workbook-level access checks can be bypassed.");
                    }
                }
        }
    }

    // ── GOV005 ────────────────────────────────────────────────────────────────
    public sealed partial class HardcodedUserProfilePath : RuleBase
    {
        public override RuleMeta Meta { get; } = new(
            Id: "GOV005",
            Title: "Connection points to a user-profile filesystem path",
            Severity: Severity.Warn,
            Description: "Local file connection references C:\\Users\\..., /home/..., or /Users/...",
            Pack: "governance");

        [GeneratedRegex(@"^[A-Za-z]:\\Users\\", RegexOptions.IgnoreCase)]
        private static partial Regex WindowsProfile();
        [GeneratedRegex(@"^/home/[^/]+/")]
        private static partial Regex LinuxHome();
        [GeneratedRegex(@"^/Users/[^/]+/")]
        private static partial Regex MacUsers();

        public override IEnumerable<Finding> Check(Workbook wb)
        {
            foreach (var ds in wb.DataSources)
                foreach (var conn in ds.Connections)
                {
                    var fn = conn.Filename;
                    if (string.IsNullOrEmpty(fn)) continue;
                    if (!(WindowsProfile().IsMatch(fn) || LinuxHome().IsMatch(fn) || MacUsers().IsMatch(fn)))
                        continue;
                    yield return NewFinding(
                        wb,
                        message: $"Connection on '{ds.Name}' references local user-profile path: '{fn}'.",
                        location: $"datasource:{ds.Name}/connection:{conn.ConnectionClass}",
                        remediation:
                            "Move the source file to a shared/server location, or convert to an " +
                            "extract so the workbook is portable.",
                        properties: ("path", fn));
                }
        }
    }
}
