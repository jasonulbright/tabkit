using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Audit;

/// <summary>One match from one rule against one workbook.</summary>
public sealed record Finding(
    string RuleId,
    Severity Severity,
    string Title,
    string Message,
    string WorkbookPath,
    string? Location = null,
    string? Remediation = null,
    IReadOnlyDictionary<string, string>? Properties = null)
{
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        Properties ?? ImmutableDictionary<string, string>.Empty;
}
