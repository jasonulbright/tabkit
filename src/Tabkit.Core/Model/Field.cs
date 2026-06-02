namespace Tabkit.Core.Model;

/// <summary>
/// A column / field on a data source.
/// <c>Name</c> is the internal bracketed identifier (e.g. <c>[Order ID]</c>);
/// <c>Caption</c> is the friendly name shown in Tableau Desktop;
/// <c>Calculation</c> is non-null only for calculated fields.
/// </summary>
public sealed record Field(
    string Name,
    string? Datatype = null,
    string? Role = null,
    string? Type = null,
    string? Caption = null,
    string? Aggregation = null,
    bool Hidden = false,
    Calculation? Calculation = null)
{
    public bool IsCalculated => Calculation is not null;
}
