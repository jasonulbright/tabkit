using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Tabkit.Core.Normalization;

/// <summary>
/// Canonicalize a Tableau workbook XML payload.
///
/// Tableau Desktop rewrites the .twb XML on every save: it reorders attributes,
/// adds/removes auto-generated feature flags, bumps the source-build counter,
/// and rewrites <c>&lt;windows&gt;</c> UI state even when the analyst changed
/// nothing meaningful. That makes <c>git diff</c> on a .twb file unreadable.
///
/// Canonicalization steps:
/// <list type="number">
/// <item>Drop XML comments (parsed away via IgnoreComments).</item>
/// <item>Drop volatile elements by localname (<c>windows</c>, <c>window</c>,
///   <c>repository-location</c>, <c>actions</c>, <c>thumbnails</c>) so namespaced
///   variants like <c>&lt;t:windows&gt;</c> are caught.</item>
/// <item>Drop volatile attributes (<c>source-build</c> on <c>workbook</c>).</item>
/// <item>Sort every element's attributes alphabetically.</item>
/// <item>Preserve internal text — <c>&lt;run&gt;A  B&lt;/run&gt;</c> stays
///   distinct from <c>&lt;run&gt;A B&lt;/run&gt;</c>.</item>
/// <item>Pretty-print with two-space indent + trailing newline.</item>
/// </list>
/// </summary>
public static class XmlNormalizer
{
    private static readonly HashSet<string> VolatileLocalNames = new(StringComparer.Ordinal)
    {
        "windows", "window", "repository-location", "actions", "thumbnails",
    };

    /// <summary>Element localname → set of attribute localnames to strip.</summary>
    private static readonly Dictionary<string, HashSet<string>> VolatileAttrsByLocalName = new(StringComparer.Ordinal)
    {
        ["workbook"] = new(StringComparer.Ordinal) { "source-build" },
    };

    public static byte[] CanonicalizeBytes(string path)
    {
        using var stream = OpenTwbStream(path);
        return CanonicalizeBytes(stream);
    }

    public static byte[] CanonicalizeBytes(Stream stream)
    {
        var doc = ParseDocument(stream);
        StripVolatile(doc.Root!);
        SortAttributes(doc.Root!);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            doc.Save(writer);
        }
        // Ensure trailing newline for clean unified diffs
        if (ms.Length == 0 || ms.GetBuffer()[ms.Length - 1] != (byte)'\n')
            ms.WriteByte((byte)'\n');
        return ms.ToArray();
    }

    public static byte[] CanonicalizeBytes(byte[] xml)
    {
        using var ms = new MemoryStream(xml);
        return CanonicalizeBytes(ms);
    }

    public static string CanonicalizeToText(string path) =>
        Encoding.UTF8.GetString(CanonicalizeBytes(path));

    public static string CanonicalizeToText(byte[] xml) =>
        Encoding.UTF8.GetString(CanonicalizeBytes(xml));

    public static string CanonicalizeToText(Stream stream) =>
        Encoding.UTF8.GetString(CanonicalizeBytes(stream));

    private static XDocument ParseDocument(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static void StripVolatile(XElement el)
    {
        // Remove volatile attributes
        if (VolatileAttrsByLocalName.TryGetValue(el.Name.LocalName, out var attrSet))
        {
            foreach (var attr in el.Attributes().Where(a => attrSet.Contains(a.Name.LocalName)).ToList())
                attr.Remove();
        }

        // Recurse, removing volatile children
        foreach (var child in el.Elements().ToList())
        {
            if (VolatileLocalNames.Contains(child.Name.LocalName))
                child.Remove();
            else
                StripVolatile(child);
        }
    }

    private static void SortAttributes(XElement el)
    {
        var sorted = el.Attributes()
            .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal)
            .ToList();
        el.ReplaceAttributes(sorted);
        foreach (var child in el.Elements())
            SortAttributes(child);
    }

    /// <summary>Open a .twb directly, or pull the .twb member out of a .twbx.</summary>
    internal static Stream OpenTwbStream(string path)
    {
        var suffix = Path.GetExtension(path).ToLowerInvariant();
        if (suffix == ".twb")
            return File.OpenRead(path);
        if (suffix == ".twbx")
            return Tabkit.Core.Loading.TwbxSafety.OpenTwbEntry(path);
        throw new InvalidOperationException($"unsupported extension: {suffix}");
    }
}
