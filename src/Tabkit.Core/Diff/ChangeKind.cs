namespace Tabkit.Core.Diff;

public enum ChangeKind { Added, Removed, Modified }

public static class ChangeKindExtensions
{
    public static string AsString(this ChangeKind k) => k switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Removed => "removed",
        ChangeKind.Modified => "modified",
        _ => "?",
    };
}
