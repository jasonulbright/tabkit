using System.Text;
using FluentAssertions;
using Tabkit.Core.Normalization;

namespace Tabkit.Tests;

public class NormalizerTests
{
    [Fact]
    public void Canonicalize_Is_Idempotent()
    {
        var once = XmlNormalizer.CanonicalizeBytes(TestFixtures.MinimalTwb);
        var twice = XmlNormalizer.CanonicalizeBytes(once);
        twice.Should().Equal(once);
    }

    [Fact]
    public void Canonicalize_AttributeOrder_DoesNotMatter()
    {
        var a = Encoding.UTF8.GetBytes(@"<?xml version='1.0' encoding='utf-8'?>
<workbook source-platform='win' version='18.1'>
  <datasources>
    <datasource name='ds1' caption='DS One' version='18.1'/>
  </datasources>
</workbook>");
        var b = Encoding.UTF8.GetBytes(@"<?xml version='1.0' encoding='utf-8'?>
<workbook version='18.1' source-platform='win'>
  <datasources>
    <datasource caption='DS One' version='18.1' name='ds1'/>
  </datasources>
</workbook>");
        XmlNormalizer.CanonicalizeBytes(a).Should().Equal(XmlNormalizer.CanonicalizeBytes(b));
    }

    [Fact]
    public void Canonicalize_StripsVolatileBuildAttr()
    {
        var a = Encoding.UTF8.GetBytes(@"<?xml version='1.0'?><workbook version='18.1' source-build='2024.3.1'><datasources/></workbook>");
        var b = Encoding.UTF8.GetBytes(@"<?xml version='1.0'?><workbook version='18.1' source-build='2024.3.99999'><datasources/></workbook>");
        XmlNormalizer.CanonicalizeBytes(a).Should().Equal(XmlNormalizer.CanonicalizeBytes(b));
    }

    [Fact]
    public void Canonicalize_StripsVolatileWindowState()
    {
        var a = Encoding.UTF8.GetBytes(@"<?xml version='1.0'?>
<workbook version='18.1'>
  <datasources/>
  <windows><window class='dashboard' name='Sales' visible='true'/></windows>
</workbook>");
        var b = Encoding.UTF8.GetBytes(@"<?xml version='1.0'?>
<workbook version='18.1'>
  <datasources/>
</workbook>");
        XmlNormalizer.CanonicalizeBytes(a).Should().Equal(XmlNormalizer.CanonicalizeBytes(b));
    }

    [Fact]
    public void Canonicalize_StripsComments()
    {
        var a = Encoding.UTF8.GetBytes(@"<?xml version='1.0'?>
<workbook version='18.1'>
  <!-- volatile comment -->
  <datasources/>
</workbook>");
        var b = Encoding.UTF8.GetBytes(@"<?xml version='1.0'?>
<workbook version='18.1'>
  <datasources/>
</workbook>");
        XmlNormalizer.CanonicalizeBytes(a).Should().Equal(XmlNormalizer.CanonicalizeBytes(b));
    }
}
