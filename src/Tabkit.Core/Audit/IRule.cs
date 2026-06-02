using System.Collections.Generic;
using Tabkit.Core.Model;

namespace Tabkit.Core.Audit;

/// <summary>A static-analysis rule.</summary>
public interface IRule
{
    RuleMeta Meta { get; }
    IEnumerable<Finding> Check(Workbook wb);
}

/// <summary>Convenience base class with a <c>BuildFinding</c> helper.</summary>
public abstract class RuleBase : IRule
{
    public abstract RuleMeta Meta { get; }
    public abstract IEnumerable<Finding> Check(Workbook wb);

    protected Finding NewFinding(
        Workbook wb,
        string message,
        string? location = null,
        string? remediation = null,
        params (string Key, string Value)[] properties)
    {
        var props = new Dictionary<string, string>();
        foreach (var (k, v) in properties)
            props[k] = v;

        return new Finding(
            RuleId: Meta.Id,
            Severity: Meta.Severity,
            Title: Meta.Title,
            Message: message,
            WorkbookPath: wb.SourcePath,
            Location: location,
            Remediation: remediation,
            Properties: props);
    }
}
