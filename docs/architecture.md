# Architecture

`tabkit` is one .NET 10 solution: a shared engine library plus two consumer surfaces (a CLI and a WPF desktop app). Every surface goes through the same `Tabkit.Core` — parser, model, normalizer, audit rules, extract pipelines, inventory store, output formatters. Nothing runs in a vendor SaaS; nothing requires Tableau Server.

## Solution layout

```
Tabkit.slnx
src/
  Tabkit.Core/            class library, net10.0          — the engine
  Tabkit.Cli/             console app, net10.0            — tabkit.exe (System.CommandLine + Spectre.Console)
  Tabkit.App/             WPF app, net10.0-windows        — tabkit-app.exe (WPF-UI Fluent + Generic Host)
```

## Data flow

```
                              +----------------------------------+
                              |          Tabkit.Core             |
                              |  parser . model . normalizer     |
                              |  audit  . extract . inventory    |
                              |  diff   . output  . validation   |
                              +-------+--------------------+-----+
                                      |                    |
                          +-----------+                    +-----------+
                          |                                            |
                          v                                            v
                +-----------------+                          +-------------------+
                |  Tabkit.Cli     |                          |   Tabkit.App      |
                |  System.CmdLine |                          |   WPF-UI Fluent   |
                |  Spectre.Console|                          |   CommunityToolkit
                |  tabkit.exe     |                          |   .Mvvm + Hosting |
                +--------+--------+                          +---------+---------+
                         |                                             |
                +--------+--------+                          +---------+---------+
                | CI pipelines    |                          | Analysts /        |
                | scheduled jobs  |                          | governance leads  |
                | pre-commit      |                          | iterating on .twb |
                +-----------------+                          +-------------------+
```

## Tabkit.Core layers (bottom-up)

### Loading + model
- `Loading/TwbLoader.cs` — `.twb` via `System.Xml.Linq`; `.twbx` via `System.IO.Compression.ZipArchive` + the single embedded `.twb` entry.
- `Model/*` — immutable C# records: `Workbook`, `DataSource`, `Connection`, `Field`, `Calculation`, `Worksheet`, `Dashboard`, `Zone`. Behavior-free, pure structure.
- `Normalization/XmlNormalizer.cs` — canonical XML form: sort attributes, strip volatile elements (`<windows>`, `source-build`), normalize internal whitespace. Underpins the diff.
- `Loading/TwbParseException.cs` — single exception type surfaced for every parse failure (broken XML, missing root, truncated zip).

### Audit
- `Audit/IRule.cs` + `Audit/Severity.cs` + `Audit/RuleMeta.cs` + `Audit/Finding.cs` — rule contract, three-level severity (info / warn / error) mapping cleanly to SARIF (note / warning / error).
- `Audit/RuleRegistry.cs` — `RuleRegistry.Default(GovernanceConfig?)` builds the shipping packs; `Run(workbook, pack?)` returns findings.
- `Audit/Packs/AuditPack.cs` — AUD001–006 (unused worksheets, orphan datasources, deprecated functions, duplicate calcs, broken column references, old version).
- `Audit/Packs/GovernancePack.cs` — GOV001–005 (embedded credentials, external servers, PII patterns, `USERNAME()`-based ad-hoc RLS, user-profile filesystem paths). Configurable allowlist (GOV002) and per-pattern disable (GOV003) via `GovernanceConfig`.
- `Audit/FormulaDetectors.cs` — single source of truth for `USERNAME` / `ISUSERNAME` / `ISMEMBEROF` / `FULLNAME` / `USERDOMAIN` recognition; used by GOV004 and by the inventory's `references_username` index.

### Diff
- `Diff/DiffEngine.cs` — `DiffWorkbooks(a, b)` returns a semantic diff over the parsed model; `CanonicalUnifiedDiff(aPath, bPath)` returns a unified diff over the canonicalized XML via DiffPlex.

### Extract
- `Extract/Pipeline.cs` — YAML schema (`name` / `source` / `transforms` / `sink`). `Pipeline.FromYaml(text)` / `FromFile(path)` parse; `Run()` executes once and exits. No daemon, no scheduling.
- `Extract/Sources/{Csv,Mssql,Parquet}Source.cs` — CSV via CsvHelper, MSSQL via reflective `Microsoft.Data.SqlClient`, Parquet via ParquetSharp (LSE-owned).
- `Extract/Transforms/BuiltinTransforms.cs` — `cast` / `filter` / `rename` / `select` / `drop`. Filter clauses are explicit `{col, op, value}` records — no `eval` of user-supplied strings.
- `Extract/Sinks/{Csv,Parquet,Hyper}Sink.cs` — CSV via CsvHelper, Parquet via ParquetSharp, Hyper via `Tableau.HyperAPI.NET` with proper `Date` / `Timestamp` types (not text representation).

### Inventory
- `Inventory/InventoryStore.cs` — `Microsoft.Data.Sqlite`. Tables: workbooks / datasources / connections / fields / worksheets / dashboards, with indexes on `server`, `has_password`, `is_calculated`, `references_username`. `UpsertWorkbook` is transactional; the schema is plain SQL embedded as a const string.
- `Inventory/InventoryScanner.cs` — `Directory.EnumerateFiles` walk → SHA-256 hash → `TwbLoader.Load` → `UpsertWorkbook`. Returns a `ScanResult` summarizing scanned / indexed / skipped / errors.
- `Inventory/InventoryQueries.cs` — `WorkbooksWithEmbeddedCredentials`, `WorkbooksReferencingTable`, `WorkbooksUsingUsername`, `ServerSummary`, `ListWorkbooks`. Return shape is `IReadOnlyList<IReadOnlyDictionary<string, object?>>` so the UI can bind a `DataView` and let WPF's `DataGrid` auto-generate columns per query.

### Output formatters
- `Output/JsonOutput.cs` — flat JSON dump via `System.Text.Json`, `snake_case` policy.
- `Output/SarifOutput.cs` — SARIF 2.1.0 compliant; conforms to the official OASIS schema. Driver block declares every rule even if it didn't fire; remediation surfaces as `message.markdown` + `result.properties`.
- `Output/HtmlOutput.cs` — single-file HTML, oklch dark theme, no external CSS/JS, no runtime needed to view. Hand-rolled `StringBuilder` template; the CSS header is a single `const string`.

### Validation
- `Validation/TwbXsdValidator.cs` — wraps `System.Xml.Schema.XmlSchemaSet` against the vendored Tableau 2026.1 TWB XSD. Companion stubs (`xml.xsd`, `tableau-user.xsd`) bridge the two namespaces Tableau imports but doesn't ship. Optional validation surface; the runtime parser doesn't depend on it.

## Surfaces

### Tabkit.Cli (`tabkit.exe`)
- `System.CommandLine` 2.0.x command tree:
  - `tabkit audit run | diff | inspect`
  - `tabkit extract run | validate`
  - `tabkit inventory scan | find | stats`
- `Spectre.Console` for table rendering + glyph severity (`✗ ⚠ ⓘ`, no red/green per the brand convention).
- Exit codes: `0` clean, `1` warnings, `2` errors. Mirrors the Python POC contract.
- `Console.OutputEncoding = UTF8` at startup so box-drawing characters don't mojibake on Windows code pages.

### Tabkit.App (`tabkit-app.exe`)
- WPF-UI 4.3.0 by lepo (Fluent design tokens, FluentWindow + NavigationView).
- CommunityToolkit.Mvvm 8.4.2 for `[ObservableProperty]` / `[RelayCommand]` source generators.
- `Microsoft.Extensions.Hosting` 10.0.x for Generic Host + DI. Pages and ViewModels register as singletons; `ApplicationHostService` shows MainWindow on start and routes to AuditPage.
- Three function pages (`Audit` / `Inventory` / `Extract`) plus `Settings` in the footer.
  - Audit: open file dialog or drag-drop `.twb` / `.twbx`, pack picker, severity filter chips, free-text filter, export buttons (JSON / SARIF / HTML), recents combo persisted to `%APPDATA%\Tabkit\settings.json`.
  - Inventory: open or create SQLite store, scan folder with progress, five query modes via combo, results auto-bind to a DataGrid via `DataView`, free-text filter, export CSV.
  - Extract: YAML editor (`TextBox` with monospace font), Validate (parse + report shape), Run (async + cancel + result panel), recents persisted.
  - Settings: theme (Dark / Light, applied at runtime), default output dirs, GOV002 allowlist (textarea), GOV003 per-pattern toggles. All settings flow through `SettingsService.BuildGovernanceConfig` into `RuleRegistry.Default(config)` so the audit picks them up on the next run.
- `BusyOverlay` user control (`Views/Controls/`) — Border + ProgressRing + title + step text bound to `IsBusy` / `Title` / `Step` dependency properties. Rendered as the last child in each page's grid so it covers content during scans / runs.

### Shared engine wrappers (`Tabkit.App/Services/`)
Thin services around `Tabkit.Core` for DI:
- `WorkbookService` — async `TwbLoader.Load`.
- `AuditService` — owns the `RuleRegistry`; rebuilds it whenever `SettingsService` raises `Changed`.
- `InventoryService` — owns the `InventoryStore` lifetime, async scan with `IProgress<string>`, query helpers that return `DataTable`.
- `ExtractService` — async pipeline run with cwd anchoring so relative paths in YAML resolve like the CLI.
- `SettingsService` — load / save `%APPDATA%\Tabkit\settings.json`, broadcast changes.

## What runs where

| Concern | Layer | Reason |
|---|---|---|
| XML parsing | `Tabkit.Core` (`System.Xml.Linq`) | First-party, zero external deps, handles namespaces cleanly |
| Rule packs | `Tabkit.Core` | Shared between CLI and WPF app |
| SQLite store | `Tabkit.Core` (`Microsoft.Data.Sqlite`) | First-party MS, no native dep packaging |
| Hyper writes | `Tabkit.Core` (`Tableau.HyperAPI.NET`) | Official Tableau NuGet; proper `Timestamp` / `Date` types |
| Parquet | `Tabkit.Core` (`ParquetSharp`) | LSE-owned, wraps Apache Arrow's parquet-cpp, stable API |
| YAML | `Tabkit.Core` (`YamlDotNet`) | De-facto standard, no script-execution risk |
| CLI parsing | `Tabkit.Cli` (`System.CommandLine`) | First-party MS, .NET 10 native |
| Terminal rendering | `Tabkit.Cli` (`Spectre.Console`) | Idiomatic table + markup output |
| Desktop chrome | `Tabkit.App` (WPF-UI by lepo) | First-party-feeling Fluent design tokens, FluentWindow Mica, Microsoft-vetted contrast palette |
| MVVM plumbing | `Tabkit.App` (`CommunityToolkit.Mvvm`) | Source-gen `ObservableProperty` + `RelayCommand`, Microsoft-owned |
| DI / lifetime | `Tabkit.App` (`Microsoft.Extensions.Hosting`) | Standard Generic Host pattern (same as dployr, project-dashboard) |

## Safety properties enforced in code

- **Passwords never exposed.** `Connection.Password` is read but the model surface (`has_password: bool`) is what every consumer sees. The password value never appears in any serializer output (JSON / SARIF / HTML).
- **No `eval`.** Pipeline filter clauses deserialize to typed `FilterClause` records; transform names map to a fixed switch in `Pipeline.BuildTransform`. There is no path from YAML text to executed code.
- **No telemetry.** Hyper API constructed with `Telemetry.DoNotSendUsageDataToTableau` in `HyperSink.cs`. No HTTP client elsewhere in the engine.
