namespace Tabkit.Core.Model;

/// <summary>A calculated field's formula.</summary>
public sealed record Calculation(string Formula, string CalcClass = "tableau");
