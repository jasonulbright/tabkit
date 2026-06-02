using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace Tabkit.Core.Validation;

/// <summary>
/// Validates a Tableau workbook (`.twb` or `.twbx`) against an XML Schema
/// Definition. The shipping XSD is Tableau's officially-published 2026.1
/// schema, vendored under <c>tests/fixtures/schemas/twb_2026.1.0.xsd</c>; this
/// class is schema-agnostic so callers can target older or newer versions by
/// loading a different XSD.
/// <para>
/// Validation is structural only. Tableau's own README on the schema repo
/// notes that calc semantics (function names, column references) are NOT
/// covered by the XSD; only XML shape is.
/// </para>
/// </summary>
public sealed class TwbXsdValidator
{
    private readonly XmlSchemaSet _schemas;

    public TwbXsdValidator(XmlSchemaSet schemas)
    {
        _schemas = schemas;
    }

    public static TwbXsdValidator FromFile(string xsdPath)
    {
        if (!File.Exists(xsdPath))
            throw new FileNotFoundException("XSD not found.", xsdPath);

        var set = new XmlSchemaSet();

        // The TWB XSD references two namespaces that aren't shipped alongside
        // it by Tableau: the W3C XML namespace (xml:base etc.) and Tableau's
        // own "user" namespace (UserAttributes-AG). Load any companion stubs
        // sitting next to the main XSD before compiling.
        var schemaDir = Path.GetDirectoryName(xsdPath) ?? ".";
        TryAddCompanion(set, "http://www.w3.org/XML/1998/namespace",
                        Path.Combine(schemaDir, "xml.xsd"));
        TryAddCompanion(set, "http://www.tableausoftware.com/xml/user",
                        Path.Combine(schemaDir, "tableau-user.xsd"));

        using var reader = XmlReader.Create(xsdPath);
        set.Add(targetNamespace: null, schemaDocument: reader);
        set.Compile();
        return new TwbXsdValidator(set);
    }

    private static void TryAddCompanion(XmlSchemaSet set, string targetNamespace, string path)
    {
        if (!File.Exists(path)) return;
        using var reader = XmlReader.Create(path);
        set.Add(targetNamespace, reader);
    }

    public ValidationReport Validate(string workbookPath)
    {
        if (!File.Exists(workbookPath))
            throw new FileNotFoundException(workbookPath);

        using var stream = OpenTwb(workbookPath);
        return ValidateStream(stream, source: workbookPath);
    }

    public ValidationReport ValidateXml(string xml, string source = "<inline>")
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        return ValidateStream(stream, source);
    }

    private ValidationReport ValidateStream(Stream stream, string source)
    {
        var errors = new List<ValidationFinding>();

        var settings = new XmlReaderSettings
        {
            Schemas = _schemas,
            ValidationType = ValidationType.Schema,
            ValidationFlags =
                XmlSchemaValidationFlags.ReportValidationWarnings |
                XmlSchemaValidationFlags.ProcessInlineSchema |
                XmlSchemaValidationFlags.ProcessSchemaLocation,
            DtdProcessing = DtdProcessing.Ignore,
        };
        settings.ValidationEventHandler += (_, args) =>
        {
            errors.Add(new ValidationFinding(
                Severity: args.Severity == XmlSeverityType.Error
                    ? ValidationSeverity.Error
                    : ValidationSeverity.Warning,
                LineNumber: args.Exception?.LineNumber ?? 0,
                LinePosition: args.Exception?.LinePosition ?? 0,
                Message: args.Message));
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read()) { /* validation fires via event */ }
        }
        catch (XmlException ex)
        {
            errors.Add(new ValidationFinding(
                Severity: ValidationSeverity.Error,
                LineNumber: ex.LineNumber,
                LinePosition: ex.LinePosition,
                Message: "xml parse: " + ex.Message));
        }

        return new ValidationReport(
            Source: source,
            Findings: errors.ToImmutableArray(),
            IsValid: !errors.Any(f => f.Severity == ValidationSeverity.Error));
    }

    /// <summary>Open a .twb (plain XML) or .twbx (zip containing one .twb) for reading.</summary>
    private static Stream OpenTwb(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".twb")
            return File.OpenRead(path);

        if (ext == ".twbx")
            return Tabkit.Core.Loading.TwbxSafety.OpenTwbEntry(path);

        throw new ArgumentException($"unsupported workbook extension: '{ext}' (expected .twb / .twbx)");
    }
}

public enum ValidationSeverity { Warning, Error }

public sealed record ValidationFinding(
    ValidationSeverity Severity,
    int LineNumber,
    int LinePosition,
    string Message);

public sealed record ValidationReport(
    string Source,
    IReadOnlyList<ValidationFinding> Findings,
    bool IsValid)
{
    public int ErrorCount => Findings.Count(f => f.Severity == ValidationSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == ValidationSeverity.Warning);
}
