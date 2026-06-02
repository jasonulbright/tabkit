using System;
using System.IO;
using FluentAssertions;
using Tabkit.Core.Loading;
using Xunit;

namespace Tabkit.Tests;

/// <summary>
/// Malformed inputs must surface as <see cref="TwbParseException"/> — never
/// as an opaque XML / IO error from inside lxml-equivalent layers. Callers
/// (UI, CLI, scanner) catch one exception type and decide how to surface
/// the problem.
/// </summary>
public class MalformedFixtureTests
{
    [Fact]
    public void Truncated_Xml_Throws_TwbParseException()
    {
        var act = () => TwbLoader.Load(TestFixtures.Corpus("malformed_truncated_xml.twb"));
        act.Should().Throw<TwbParseException>();
    }

    [Fact]
    public void Empty_File_Throws_TwbParseException()
    {
        var act = () => TwbLoader.Load(TestFixtures.Corpus("malformed_empty.twb"));
        act.Should().Throw<TwbParseException>();
    }

    [Fact]
    public void Wrong_Root_Element_Throws_TwbParseException()
    {
        var act = () => TwbLoader.Load(TestFixtures.Corpus("malformed_wrong_root.twb"));
        act.Should().Throw<TwbParseException>();
    }

    [Fact]
    public void Truncated_Twbx_Throws_Either_Parse_Or_IO_Exception()
    {
        // Build a truncated .twbx on disk by writing partial zip header bytes.
        // Either TwbParseException OR IOException (zip-layer) is acceptable
        // here; what matters is no leaked stream / unhandled NullReference.
        var tmp = Directory.CreateTempSubdirectory("tabkit-malformed-twbx-");
        try
        {
            var path = Path.Combine(tmp.FullName, "broken.twbx");
            // ZIP magic bytes then garbage — opens as a zip but has no
            // valid central directory.
            File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
            var act = () => TwbLoader.Load(path);
            act.Should().Throw<Exception>()
               .Which.Should().Match(e =>
                    e is TwbParseException ||
                    e is IOException ||
                    e is InvalidDataException);
        }
        finally { tmp.Delete(recursive: true); }
    }
}
