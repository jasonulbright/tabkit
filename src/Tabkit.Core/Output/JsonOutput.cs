using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tabkit.Core.Audit;

namespace Tabkit.Core.Output;

public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string FindingsToJson(IEnumerable<Finding> findings)
    {
        var payload = new
        {
            Tool = "tabkit",
            Version = ThisAssembly.Version,
            Findings = findings.Select(f => new
            {
                f.RuleId,
                Severity = f.Severity.AsString(),
                f.Title,
                f.Message,
                f.WorkbookPath,
                f.Location,
                f.Remediation,
                Properties = f.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
            }).ToList(),
        };
        return JsonSerializer.Serialize(payload, Options);
    }
}

internal static class ThisAssembly
{
    public const string Version = "0.2.0";
}
