using System.Text.RegularExpressions;

namespace Tabkit.Core.Audit;

/// <summary>
/// Shared formula-pattern detectors. Single source of truth so the inventory
/// store and the governance pack agree on what counts as "this calc uses a
/// user/identity function."
/// </summary>
public static partial class FormulaDetectors
{
    /// <summary>
    /// Functions used to drive ad-hoc row-level security in calculated fields.
    /// Anything that exposes the running user's identity counts.
    /// </summary>
    public static readonly string[] UsernameFnNames =
    {
        "USERNAME", "ISUSERNAME", "ISMEMBEROF", "FULLNAME", "USERDOMAIN",
    };

    [GeneratedRegex(@"\b(USERNAME|ISUSERNAME|ISMEMBEROF|FULLNAME|USERDOMAIN)\s*\(", RegexOptions.IgnoreCase)]
    public static partial Regex UsernameFnPattern();

    public static bool FormulaReferencesUserIdentity(string formula)
        => !string.IsNullOrEmpty(formula) && UsernameFnPattern().IsMatch(formula);
}
