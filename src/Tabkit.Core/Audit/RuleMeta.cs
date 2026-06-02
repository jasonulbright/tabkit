namespace Tabkit.Core.Audit;

/// <summary>Static metadata about a rule, independent of any workbook.</summary>
public sealed record RuleMeta(
    string Id,
    string Title,
    Severity Severity,
    string Description,
    string Pack);
