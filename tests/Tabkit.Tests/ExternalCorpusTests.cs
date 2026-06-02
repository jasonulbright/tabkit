using System.IO;
using Tabkit.Core.Audit;
using Tabkit.Core.Loading;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Smoke tests against the downloaded external corpus. Skipped silently when
/// the corpus hasn't been populated yet (run <c>tests/fixtures/external/fetch.ps1</c>
/// to enable). These are belt-and-suspenders gates against parser regressions on
/// real-world workbooks; the handcrafted fixtures stay the canonical specs.
/// </summary>
public class ExternalCorpusTests
{
    private readonly ITestOutputHelper _output;
    public ExternalCorpusTests(ITestOutputHelper output) => _output = output;

    private static string ExternalRoot => Path.Combine(TestFixtures.Root, "external");

    public static TheoryData<string> BookWorkbooks() => Discover("book", new[] { ".twbx", ".twb" });
    public static TheoryData<string> ServerClientWorkbooks() => Discover("server-client", new[] { ".twbx", ".twb" });

    private static TheoryData<string> Discover(string sub, string[] exts)
    {
        var data = new TheoryData<string>();
        var dir = Path.Combine(ExternalRoot, sub);
        if (!Directory.Exists(dir)) return data;
        foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (exts.Contains(ext)) data.Add(f);
        }
        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(BookWorkbooks))]
    public void BookCorpus_ParsesWithoutThrowing(string path)
    {
        Skip.IfNot(File.Exists(path), $"Corpus not fetched: {path}");
        var wb = TwbLoader.Load(path);
        Assert.NotNull(wb);
        var findings = RuleRegistry.Default().Run(wb);
        _output.WriteLine($"{Path.GetFileName(path)}: {wb.DataSources.Count} ds, {wb.Worksheets.Count} ws, {wb.Dashboards.Count} db, {findings.Count} findings");
    }

    [SkippableTheory]
    [MemberData(nameof(ServerClientWorkbooks))]
    public void ServerClientCorpus_ParsesWithoutThrowing(string path)
    {
        Skip.IfNot(File.Exists(path), $"Corpus not fetched: {path}");
        var wb = TwbLoader.Load(path);
        Assert.NotNull(wb);
        var findings = RuleRegistry.Default().Run(wb);
        _output.WriteLine($"{Path.GetFileName(path)}: {wb.DataSources.Count} ds, {wb.Worksheets.Count} ws, {wb.Dashboards.Count} db, {findings.Count} findings");
    }
}
