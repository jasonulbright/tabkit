using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Tabkit.Core.Diff;
using Tabkit.Core.Loading;
using Tabkit.Core.Model;

namespace Tabkit.Tests;

public class DiffTests
{
    [Fact]
    public void Diff_Identical_HasNoChanges()
    {
        var wb = TwbLoader.Load(TestFixtures.MinimalTwb);
        var d = DiffEngine.DiffWorkbooks(wb, wb);
        d.Changes.Should().BeEmpty();
        d.Summary().Should().Be("no semantic differences");
    }

    [Fact]
    public void Diff_AddedDataSource_DetectedAsAdded()
    {
        var a = TwbLoader.Load(TestFixtures.MinimalTwb);
        var newDs = new DataSource("federated.new", Caption: "New");
        var b = a with { DataSources = a.DataSources.Concat(new[] { newDs }).ToImmutableArray() };
        var d = DiffEngine.DiffWorkbooks(a, b);
        d.Added.Should().Contain(c => c.Category == "datasource" && c.Identifier == "federated.new");
    }

    [Fact]
    public void Diff_ModifiedCalcFormula_IsDetected()
    {
        var a = TwbLoader.Load(TestFixtures.MinimalTwb);
        var ds = a.DataSources[0];
        var updated = ds.Fields.Select(f =>
            f.Name == "[total_with_tax]"
                ? f with { Calculation = new Calculation("[total] * 1.10") }
                : f).ToImmutableArray();
        var b = a with { DataSources = ImmutableArray.Create(ds with { Fields = updated }) };
        var d = DiffEngine.DiffWorkbooks(a, b);
        d.Modified.Should().Contain(c => (c.Detail ?? "").Contains("calculation formula changed"));
    }

    [Fact]
    public void CanonicalUnifiedDiff_IdenticalIsEmpty()
    {
        var diff = DiffEngine.CanonicalUnifiedDiff(TestFixtures.MinimalTwb, TestFixtures.MinimalTwb);
        diff.Should().BeEmpty();
    }
}
