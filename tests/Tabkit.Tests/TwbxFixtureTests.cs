using System.IO;
using FluentAssertions;
using Tabkit.Core.Loading;
using Xunit;

namespace Tabkit.Tests;

/// <summary>
/// .twbx is .twb wrapped in a zip. Verifies the loader handles the zip
/// path (open archive → locate single .twb entry → parse) without losing
/// any of the underlying structure.
/// </summary>
public class TwbxFixtureTests
{
    [Fact]
    public void Clean_Twbx_Parses_To_Same_Shape_As_Clean_Twb()
    {
        var twb = TwbLoader.Load(TestFixtures.Corpus("clean.twb"));
        var twbx = TwbLoader.Load(TestFixtures.Corpus("clean.twbx"));

        twbx.Version.Should().Be(twb.Version);
        twbx.DataSources.Count.Should().Be(twb.DataSources.Count);
        twbx.Worksheets.Count.Should().Be(twb.Worksheets.Count);
        twbx.Dashboards.Count.Should().Be(twb.Dashboards.Count);
        twbx.DataSources[0].Name.Should().Be(twb.DataSources[0].Name);
        twbx.DataSources[0].Fields.Count.Should().Be(twb.DataSources[0].Fields.Count);
    }
}
