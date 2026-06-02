using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Tabkit.Core.Model;

namespace Tabkit.Core.Loading;

/// <summary>
/// Load <c>.twb</c> / <c>.twbx</c> files into the tabkit model.
/// <c>.twb</c> is plain XML. <c>.twbx</c> is a zip containing exactly one
/// <c>.twb</c> plus optional bundled data.
/// </summary>
public static class TwbLoader
{
    private static readonly HashSet<string> ConnectionKnownAttrs = new(StringComparer.Ordinal)
    {
        "class", "server", "dbname", "username", "password",
        "filename", "port", "authentication",
    };

    public static Workbook Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(path);

        var suffix = Path.GetExtension(path).ToLowerInvariant();

        XDocument doc;
        if (suffix == ".twb")
        {
            using var stream = File.OpenRead(path);
            doc = ParseXml(stream);
        }
        else if (suffix == ".twbx")
        {
            using var inner = OpenTwbInsideTwbx(path);
            doc = ParseXml(inner);
        }
        else
        {
            throw new TwbParseException(
                $"unsupported extension: '{suffix}' (expected .twb or .twbx)");
        }

        return BuildWorkbook(path, doc);
    }

    private static Stream OpenTwbInsideTwbx(string path)
    {
        // TwbxSafety enforces size + compression-ratio caps on the inner .twb
        // member so a malicious / corrupted .twbx can't trigger a memory-DoS
        // (Codex R1 finding #5). Wrap its InvalidOperationException as
        // TwbParseException so callers see the loader's consistent failure type.
        try
        {
            return TwbxSafety.OpenTwbEntry(path);
        }
        catch (InvalidOperationException ex)
        {
            throw new TwbParseException(ex.Message, ex);
        }
    }

    private static XDocument ParseXml(Stream stream)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = false,
                IgnoreWhitespace = false,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new TwbParseException(ex.Message, ex);
        }
    }

    private static Workbook BuildWorkbook(string sourcePath, XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "workbook")
            throw new TwbParseException(
                $"root element is '{root?.Name.LocalName}', expected 'workbook'");

        return new Workbook(
            SourcePath: sourcePath,
            Version: root.Attribute("version")?.Value,
            SourceBuild: root.Attribute("source-build")?.Value,
            SourcePlatform: root.Attribute("source-platform")?.Value,
            DataSources: root.ElementsLN("datasources").ElementsLN("datasource")
                .Select(ParseDataSource).ToImmutableArray(),
            Worksheets: root.ElementsLN("worksheets").ElementsLN("worksheet")
                .Select(ParseWorksheet).ToImmutableArray(),
            Dashboards: root.ElementsLN("dashboards").ElementsLN("dashboard")
                .Select(ParseDashboard).ToImmutableArray());
    }

    private static DataSource ParseDataSource(XElement el)
    {
        return new DataSource(
            Name: el.Attribute("name")?.Value ?? string.Empty,
            Caption: el.Attribute("caption")?.Value,
            Version: el.Attribute("version")?.Value,
            // DescendantsLN matches the Python `iter("connection")` semantics and
            // tolerates namespace-prefixed elements (e.g. <t:connection .../>).
            Connections: el.DescendantsLN("connection").Select(ParseConnection).ToImmutableArray(),
            Fields: el.ElementsLN("column").Select(ParseField).ToImmutableArray());
    }

    private static Connection ParseConnection(XElement el)
    {
        var extras = el.Attributes()
            .Where(a => !ConnectionKnownAttrs.Contains(a.Name.LocalName))
            .ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal);

        return new Connection(
            ConnectionClass: el.Attribute("class")?.Value,
            Server: el.Attribute("server")?.Value,
            Dbname: el.Attribute("dbname")?.Value,
            Username: el.Attribute("username")?.Value,
            Password: el.Attribute("password")?.Value,
            Filename: el.Attribute("filename")?.Value,
            Port: el.Attribute("port")?.Value,
            Authentication: el.Attribute("authentication")?.Value,
            Attrs: extras);
    }

    private static Field ParseField(XElement el)
    {
        var calcEl = el.ElementLN("calculation");
        Calculation? calc = null;
        if (calcEl is not null)
        {
            var formula = calcEl.Attribute("formula")?.Value;
            if (formula is not null)
            {
                calc = new Calculation(
                    Formula: formula,
                    CalcClass: calcEl.Attribute("class")?.Value ?? "tableau");
            }
        }

        var hiddenAttr = el.Attribute("hidden")?.Value?.ToLowerInvariant();

        return new Field(
            Name: el.Attribute("name")?.Value ?? string.Empty,
            Datatype: el.Attribute("datatype")?.Value,
            Role: el.Attribute("role")?.Value,
            Type: el.Attribute("type")?.Value,
            Caption: el.Attribute("caption")?.Value,
            Aggregation: el.Attribute("aggregation")?.Value,
            Hidden: hiddenAttr == "true",
            Calculation: calc);
    }

    private static Worksheet ParseWorksheet(XElement el)
    {
        // table/view/datasources/datasource — Python: el.findall("table/view/datasources/datasource")
        var dsNames = el.ElementsLN("table")
            .ElementsLN("view")
            .ElementsLN("datasources")
            .ElementsLN("datasource")
            .Select(d => d.Attribute("name")?.Value ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToImmutableArray();
        return new Worksheet(
            Name: el.Attribute("name")?.Value ?? string.Empty,
            DataSourceNames: dsNames);
    }

    private static Dashboard ParseDashboard(XElement el)
    {
        return new Dashboard(
            Name: el.Attribute("name")?.Value ?? string.Empty,
            Zones: el.ElementsLN("zones").ElementsLN("zone").Select(ParseZone).ToImmutableArray());
    }

    private static Zone ParseZone(XElement el)
    {
        var zoneType = el.Attribute("type")?.Value;
        var name = el.Attribute("name")?.Value;
        var worksheetRef = zoneType == "worksheet" ? name : null;
        return new Zone(
            Id: el.Attribute("id")?.Value,
            ZoneType: zoneType,
            Name: name,
            WorksheetRef: worksheetRef,
            Children: el.ElementsLN("zone").Select(ParseZone).ToImmutableArray());
    }
}

/// <summary>
/// LocalName-based XLINQ helpers. Default <see cref="XElement.Elements(XName)"/>
/// matches by full XName, so namespace-prefixed elements (e.g. <c>&lt;t:datasources&gt;</c>)
/// are skipped when querying with bare strings. These helpers compare on
/// <see cref="XName.LocalName"/> only, tolerating arbitrary namespace prefixes.
/// </summary>
internal static class XElementLocalNameExtensions
{
    public static IEnumerable<XElement> ElementsLN(this XElement el, string localName)
        => el.Elements().Where(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> ElementsLN(this IEnumerable<XElement> els, string localName)
        => els.SelectMany(e => e.ElementsLN(localName));

    public static XElement? ElementLN(this XElement el, string localName)
        => el.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> DescendantsLN(this XElement el, string localName)
        => el.Descendants().Where(d => d.Name.LocalName == localName);
}
