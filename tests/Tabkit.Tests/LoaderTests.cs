using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Tabkit.Core.Loading;

namespace Tabkit.Tests;

public class LoaderTests
{
    [Fact]
    public void MinimalTwb_Parses()
    {
        var wb = TwbLoader.Load(TestFixtures.MinimalTwb);
        wb.Version.Should().Be("18.1");
        wb.SourcePlatform.Should().Be("win");
        wb.SourceBuild.Should().Be("2024.3.0");
    }

    [Fact]
    public void DataSource_Shape_IsCorrect()
    {
        var wb = TwbLoader.Load(TestFixtures.MinimalTwb);
        wb.DataSources.Should().HaveCount(1);
        var ds = wb.DataSources[0];
        ds.Name.Should().Be("federated.sample_orders");
        ds.Caption.Should().Be("Sample Orders");
        ds.Connections.Should().HaveCount(1);
        ds.Connections[0].ConnectionClass.Should().Be("excel-direct");
        ds.Connections[0].Filename.Should().Be("sample_orders.xlsx");
        ds.Fields.Should().HaveCount(5);
        ds.CalculatedFields.Should().HaveCount(2);
    }

    [Fact]
    public void CalculationFormula_IsPreserved()
    {
        var wb = TwbLoader.Load(TestFixtures.MinimalTwb);
        var ds = wb.DataSources[0];

        var tax = System.Linq.Enumerable.Single(ds.Fields, f => f.Name == "[total_with_tax]");
        tax.IsCalculated.Should().BeTrue();
        tax.Calculation!.Formula.Should().Be("[total] * 1.08");

        var locked = System.Linq.Enumerable.Single(ds.Fields, f => f.Name == "[locked_to_user]");
        locked.IsCalculated.Should().BeTrue();
        locked.Calculation!.Formula.Should().Contain("USERNAME()");
    }

    [Fact]
    public void UnusedWorksheet_Detection_Works()
    {
        var wb = TwbLoader.Load(TestFixtures.MinimalTwb);
        wb.WorksheetNames.Should().BeEquivalentTo(new[] { "Sales by Order", "Unused Scratch Sheet" });
        wb.ReferencedWorksheets.Should().BeEquivalentTo(new[] { "Sales by Order" });
        wb.UnusedWorksheets.Should().BeEquivalentTo(new[] { "Unused Scratch Sheet" });
    }

    [Fact]
    public void TwbxRoundTrip()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tabkit-test-{System.Guid.NewGuid()}.twbx");
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(TestFixtures.MinimalTwb, "synthetic.twb");
                var dummy = zip.CreateEntry("Data/sample.xlsx");
                using var es = dummy.Open();
                es.WriteByte((byte)'P'); es.WriteByte((byte)'K');
            }
            var wb = TwbLoader.Load(tmp);
            wb.Version.Should().Be("18.1");
            wb.DataSources.Should().HaveCount(1);
            wb.UnusedWorksheets.Should().BeEquivalentTo(new[] { "Unused Scratch Sheet" });
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void UnsupportedExtension_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tabkit-test-{System.Guid.NewGuid()}.txt");
        File.WriteAllText(tmp, "hello");
        try
        {
            var act = () => TwbLoader.Load(tmp);
            act.Should().Throw<TwbParseException>()
                .WithMessage("*unsupported extension*");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void MissingFile_Throws()
    {
        var act = () => TwbLoader.Load(Path.Combine(Path.GetTempPath(), "definitely-not-here.twb"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void TwbxWithoutTwbMember_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tabkit-test-{System.Guid.NewGuid()}.twbx");
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Data/something.csv");
                using var es = entry.Open();
                es.WriteByte((byte)'a');
            }
            var act = () => TwbLoader.Load(tmp);
            act.Should().Throw<TwbParseException>()
                .WithMessage("*no .twb member*");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void MalformedXml_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tabkit-test-{System.Guid.NewGuid()}.twb");
        File.WriteAllBytes(tmp, System.Text.Encoding.UTF8.GetBytes("<workbook><datasources></workbook>"));
        try
        {
            var act = () => TwbLoader.Load(tmp);
            act.Should().Throw<TwbParseException>();
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
