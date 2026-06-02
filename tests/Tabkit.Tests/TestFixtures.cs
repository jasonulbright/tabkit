using System;
using System.IO;

namespace Tabkit.Tests;

/// <summary>Resolves paths to the shipped <c>tests/fixtures/</c> corpus.</summary>
public static class TestFixtures
{
    private static readonly Lazy<string> _root = new(LocateFixtureRoot);

    public static string Root => _root.Value;
    public static string MinimalTwb => Path.Combine(Root, "minimal.twb");
    public static string StressTwb  => Path.Combine(Root, "stress.twb");
    public static string Corpus(string name) => Path.Combine(Root, "corpus", name);

    private static string LocateFixtureRoot()
    {
        // Walk up from the test assembly looking for tests/fixtures/
        var cursor = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(cursor, "tests", "fixtures");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(cursor);
            if (parent is null) break;
            cursor = parent.FullName;
        }
        throw new DirectoryNotFoundException(
            "tests/fixtures not found — run from inside the repo tree");
    }
}
