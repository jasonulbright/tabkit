# Changelog

All notable changes to tabkit are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [POC]

First public cut of the .NET 10 toolkit: a single solution shipping a
cross-platform CLI (`tabkit`) and a Windows desktop app (`tabkit-app`) over one
shared engine (`Tabkit.Core`). Reads `.twb` / `.twbx` directly — no Tableau
Server, no REST API, no account.

### Added

**Engine (`Tabkit.Core`)**
- Workbook loader for `.twb` (plain XML) and `.twbx` (zip), tolerant of
  namespace-prefixed elements, mixed line endings, and UTF-8/UTF-16 encodings.
  Bounded `.twbx` extraction with size and compression-ratio caps.
- XML normalizer: stable canonical form for diffing (volatile-tag stripping,
  whitespace normalization, comment handling).
- Audit engine with two pluggable rule packs — `audit` (AUD001-006) and
  `governance` (GOV001-005), 11 rules total. Runtime-configurable via
  `GovernanceConfig` (GOV002 server allowlist, GOV003 per-pattern toggles).
- Semantic + canonical-XML diff between two workbook versions.
- Inventory store (SQLite): indexes paths, connections, referenced tables, and
  calculated-field formulas; queryable across a whole fleet.
- Extract pipelines: CSV / Parquet / SQL Server sources, cast / filter / rename
  / select / drop transforms, CSV / Parquet / Hyper sinks. Strict YAML
  validation that rejects malformed shapes before a run.
- Output writers: JSON, hand-rolled SARIF 2.1.0 (schema-validated), single-file
  HTML, and a TWB XSD validator.

**CLI (`tabkit`)**
- `audit run | diff | inspect`, `extract run | validate`,
  `inventory scan | find | stats`.
- `--format text | json | sarif | html`; unknown packs and formats error
  explicitly rather than silently doing nothing.
- 0 / 1 / 2 exit-code contract (clean / warning / error).

**Desktop app (`tabkit-app`)**
- WPF-UI Fluent on .NET 10. Audit / Inventory / Extract / Settings pages over
  the same engine, with drag-drop, severity filtering, recents, async progress,
  and JSON / SARIF / HTML export.
- Settings persisted to `%APPDATA%\Tabkit\settings.json`; corrupt or missing
  files fall back to defaults without crashing.

### Safety

- Connection passwords are surfaced only as `has_password: bool`, never the
  value — across model, JSON, SARIF, and HTML.
- No telemetry; the Hyper API is always called with
  `Telemetry.DoNotSendUsageDataToTableau`.
- No expression `eval` — pipeline filters are explicit `{col, op, value}` records.
