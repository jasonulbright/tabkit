# NOTICE — Vendored schemas

## `sarif-2.1.0.json` — SARIF 2.1.0 JSON Schema

- **Source**: https://github.com/oasis-tcs/sarif-spec — `sarif-2.1/schema/sarif-schema-2.1.0.json`
- **License**: OASIS IPR Policy, RF on RAND terms
- **Copyright**: © OASIS Open
- Vendored for synthetic validation of tabkit's SARIF output. Use at test
  time only; not redistributed in any tabkit binary.

## `xml.xsd` — W3C XML namespace schema

- **Source**: https://www.w3.org/2001/xml.xsd
- **License**: W3C Document License
- Vendored locally so the Tableau TWB XSD's `xml:base` reference resolves
  without network access at test time.

## `tableau-user.xsd` — synthetic Tableau "user" namespace stub

- **Source**: handwritten by tabkit. Tableau's `tableau-document-schemas`
  repo imports the user namespace from the TWB XSD but doesn't publish a
  corresponding schema document. This stub defines the imported types
  permissively (`<xs:anyAttribute processContents="lax"/>`) so the main
  XSD compiles under .NET's strict XmlSchemaSet.
- **License**: same as tabkit (MIT, when public)

## `twb_2026.1.0.xsd` — Tableau TWB Schema

`twb_2026.1.0.xsd` is Tableau's officially-published TWB schema, vendored here
for synthetic-test validation of handcrafted fixtures and parser correctness
assertions.

- **Source**: https://github.com/tableau/tableau-document-schemas
- **License**: Apache License 2.0
- **Copyright**: © Tableau Software, LLC, a Salesforce company
- **Schema version**: 2026.1.0 (published 2026-02-27)

Use of this file is governed by the upstream Apache 2.0 license. A copy of the
license is available at https://www.apache.org/licenses/LICENSE-2.0.

Tabkit uses this XSD purely as a test-time input — it is **not** redistributed
as part of any tabkit binary, and the runtime parser does **not** depend on it.
