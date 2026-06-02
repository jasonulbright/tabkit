namespace Tabkit.Core.Audit;

/// <summary>Three-level severity. Maps cleanly to SARIF (note/warning/error).</summary>
public enum Severity
{
    Info,
    Warn,
    Error,
}

public static class SeverityExtensions
{
    /// <summary>Lowercased value (matches the Python <c>severity.value</c>).</summary>
    public static string AsString(this Severity s) => s switch
    {
        Severity.Info => "info",
        Severity.Warn => "warn",
        Severity.Error => "error",
        _ => "info",
    };

    /// <summary>SARIF 2.1.0 level mapping.</summary>
    public static string SarifLevel(this Severity s) => s switch
    {
        Severity.Info => "note",
        Severity.Warn => "warning",
        Severity.Error => "error",
        _ => "note",
    };
}
