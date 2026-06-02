using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tabkit.Core.Audit;

namespace Tabkit.Core.Output;

/// <summary>
/// SARIF 2.1.0 output. Spec-compliant — validated against
/// <c>json.schemastore.org/sarif-2.1.0.json</c> in the Python port.
/// </summary>
public static partial class SarifOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string FindingsToSarif(IEnumerable<Finding> findings, IEnumerable<IRule>? rules = null)
    {
        var findingList = findings.ToList();
        var ruleMap = new Dictionary<string, object>();

        if (rules is not null)
        {
            foreach (var r in rules)
                ruleMap[r.Meta.Id] = BuildRuleDescriptor(r.Meta.Id, r.Meta.Title, r.Meta.Description, r.Meta.Severity.SarifLevel(), r.Meta.Pack);
        }
        // Ensure every finding's rule is declared
        foreach (var f in findingList)
            ruleMap.TryAdd(f.RuleId, BuildRuleDescriptor(f.RuleId, f.Title, f.Title, f.Severity.SarifLevel(), null));

        var results = new List<object>();
        foreach (var f in findingList)
        {
            var physLoc = new Dictionary<string, object>
            {
                ["physicalLocation"] = new
                {
                    artifactLocation = new { uri = AsUri(f.WorkbookPath) }
                }
            };
            if (!string.IsNullOrEmpty(f.Location))
                physLoc["logicalLocations"] = new[] { new { fullyQualifiedName = f.Location } };

            var message = new Dictionary<string, string> { ["text"] = f.Message };
            if (!string.IsNullOrEmpty(f.Remediation))
                message["markdown"] = $"{f.Message}\n\n**Remediation:** {f.Remediation}";

            var properties = new Dictionary<string, string>(f.Properties);
            if (!string.IsNullOrEmpty(f.Remediation))
                properties["remediation"] = f.Remediation;

            var result = new Dictionary<string, object>
            {
                ["ruleId"] = f.RuleId,
                ["level"] = f.Severity.SarifLevel(),
                ["message"] = message,
                ["locations"] = new object[] { physLoc },
            };
            if (properties.Count > 0)
                result["properties"] = properties;
            results.Add(result);
        }

        var sarif = new
        {
            schema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            version = "2.1.0",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "tabkit",
                            version = ThisAssembly.Version,
                            informationUri = "https://github.com/jasonulbright/tabkit",
                            rules = ruleMap.Values.ToList(),
                        }
                    },
                    results,
                }
            }
        };
        // Rename "schema" → "$schema" via post-process (System.Text.Json doesn't let us name $ directly via anonymous types)
        var raw = JsonSerializer.Serialize(sarif, Options);
        return raw.Replace("\"schema\":", "\"$schema\":");
    }

    private static object BuildRuleDescriptor(string id, string title, string description, string level, string? pack)
    {
        var desc = new Dictionary<string, object>
        {
            ["id"] = id,
            ["name"] = SlugName(id, title),
            ["shortDescription"] = new { text = title },
            ["fullDescription"] = new { text = description },
            ["defaultConfiguration"] = new { level },
        };
        if (!string.IsNullOrEmpty(pack))
            desc["properties"] = new { pack };
        return desc;
    }

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonIdentRun();

    private static string SlugName(string ruleId, string title)
    {
        var parts = NonIdentRun().Split($"{ruleId} {title}");
        return string.Concat(parts
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    /// <summary>
    /// Convert a filesystem path to an artifact URI for SARIF.
    /// Absolute paths become real <c>file:///</c> URIs (so Windows
    /// <c>C:\projects\x.twb</c> serializes as <c>file:///C:/projects/x.twb</c>
    /// instead of an invalid scheme-<c>C:</c> URI). Relative paths pass through
    /// with forward-slash normalization. Closes Codex R1 finding #9, aligning
    /// with GitHub's SARIF artifact-URI guidance.
    /// </summary>
    private static string AsUri(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        if (System.IO.Path.IsPathRooted(path))
        {
            try { return new System.Uri(path).AbsoluteUri; }
            catch { /* fall through to slash-normalized form */ }
        }

        return path.Replace('\\', '/');
    }
}
