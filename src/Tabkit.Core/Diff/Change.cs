namespace Tabkit.Core.Diff;

/// <summary>
/// One semantic change between two workbooks.
/// <c>Category</c> is one of: datasource, field, worksheet, dashboard,
/// connection, calculation.
/// </summary>
public sealed record Change(
    ChangeKind Kind,
    string Category,
    string Identifier,
    string? Detail = null);
