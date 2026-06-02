# Corpus

Synthetic `.twb` / `.twbx` fixtures used to exercise the parser, audit rules, and inventory scanner. All hand-authored or programmatically generated, no real-world data, suitable for shipping in a public repo.

## Top-level fixtures (mixed-trigger)

These are general-purpose corpus inputs. Each trips multiple rules — useful for soak tests but not for isolating a single rule's behavior.

| Fixture | What it exercises |
|---|---|
| `clean.twb` | Baseline — passes every rule. Used to assert zero-finding behavior. |
| `clean.twbx` | Same workbook, zipped. Exercises `.twbx` zip path. |
| `deprecated.twb` | AUD003 across multiple deprecated functions in calculated fields. |
| `broken-refs.twb` | AUD005 broken column references on more than one data source. |
| `pii-heavy.twb` | GOV001 + GOV003 + GOV004 + GOV005 — embedded creds, PII fields, ad-hoc RLS, user-profile paths. |
| `large.twb` | Performance smoke: 20 data sources, 5 fields each, many worksheets, one dashboard. |
| `legacy-version.twb` | AUD006 — version `9.0` workbook, predates the modern XML floor. |
| `nested-dashboards.twb` | Layout zone nesting (worksheet → container → container → worksheet). |

## `one-rule-each/` — isolation fixtures

Each fixture in this directory trips **exactly one rule, exactly the expected number of times**, with no cross-contamination. These are the canonical regression-test inputs for `OneRuleEachFixtureTests`.

| Fixture | Trips | Why no other rule fires |
|---|---|---|
| `aud001_unused_worksheet.twb` | AUD001 ×1 | Two worksheets, dashboard references only one. Datasource is referenced. Version is modern. |
| `aud002_orphan_datasource.twb` | AUD002 ×1 | Two datasources, only one is referenced. The referenced one is on a dashboard. |
| `aud003_deprecated_function.twb` | AUD003 ×1 | Calc uses `ATTR()`. All column refs exist. Scaffolded. |
| `aud004_duplicate_calc.twb` | AUD004 ×1 | Same `IIF([amount] > 0, 1, 0)` calc in two datasources, both referenced + on dashboard. |
| `aud005_broken_column_ref.twb` | AUD005 ×1 | Calc references `[missing_col]` which isn't defined. |
| `aud006_old_version.twb` | AUD006 ×1 | `version='17.0'` (below 18.0 floor). Otherwise valid. |
| `gov001_embedded_password.twb` | GOV001 ×1 | Connection has `password=`. Server is `localhost` (allowlisted), so GOV002 stays quiet. |
| `gov002_external_server.twb` | GOV002 ×1 | Connection points at `vendor-prod.example.net`. No password, no PII fields. |
| `gov003_pii_pattern.twb` | GOV003 ×1 | Single field named `[ssn]`. No password, file-based connection. |
| `gov004_username_rls.twb` | GOV004 ×1 | Calc uses `USERNAME()`. Calc references valid columns only. |
| `gov005_user_profile_path.twb` | GOV005 ×1 | Connection filename starts `C:\Users\...`. No other governance trips. |

## `malformed_*.twb` — failure path fixtures

Inputs that must surface as `TwbParseException` (or, for truncated zip, either `TwbParseException` or a zip-layer IOException). Verifies the parser fails clean instead of crashing or returning a half-parsed workbook.

| Fixture | What's broken |
|---|---|
| `malformed_truncated_xml.twb` | XML cut off mid-element. |
| `malformed_wrong_root.twb` | Well-formed XML but root isn't `<workbook>`. |
| `malformed_empty.twb` | Zero-byte file. |

## `pathological_*.twb` — edge-case fixtures

Unusual but valid inputs that the parser + audit must handle without crashing or mangling.

| Fixture | Exercises |
|---|---|
| `pathological_unicode.twb` | Field names with emoji (`🎯`), RTL Arabic (`العنوان`), Han (`销售额度`), Latin diacritic (`café`), and a 100+ char identifier. |
| `pathological_deeply_nested_calc.twb` | 10-level nested `IIF()` chain — stresses the formula scanner. |
| `pathological_circular_calc_refs.twb` | `[a] = [b] + 1` and `[b] = [a] - 1` on the same datasource. Parser must not throw; AUD005 must not fire (both refs resolve). Cycle detection is out of scope. |
| `pathological_namespace_prefix.twb` | Root and all children prefixed with `t:` under `xmlns:t="..."`. Loader uses LocalName-based matching so prefixed elements are still discovered. |
| `pathological_no_bom_utf8.twb` | Byte-encoding probe: UTF-8 without BOM. Also acts as the canonical source from which the BOM-UTF8, UTF-16 LE, and mixed-line-ending variants are derived at test time. |
| `pathological_comments_in_calc.twb` | A `<calculation>` element carrying both a `formula=` attribute AND a child `<!-- comment -->`. Formula extraction must read the attribute intact regardless of the comment child. |
| `pathological_very_long_names.twb` | Field with a 1000+ character bracketed name plus a calc that references it. Exercises attribute-value handling and the column-ref regex `\[[^\[\]\r\n]+\]`. |

The BOM-UTF8, UTF-16 LE, and mixed-line-ending probes are NOT shipped as static files. `PathologicalFixtureTests` derives them from `pathological_no_bom_utf8.twb` at test setup time (via `MaterializeBytes` + temp path) and deletes them after the test runs. This avoids shipping `.gitattributes` (which the public-repo convention forbids) while still asserting parser behavior across encodings and line-ending styles.

## Generated at test time

`PerfFixtureTests.GenerateLargeWorkbook(N, M)` emits a synthetic workbook with N datasources × M fields each (half plain, half calculated). Used by the perf test to assert load + audit on 50 × 100 stays under 5 seconds — catches O(N²) regressions, not microbench wobble.

## Shipped at higher level

`minimal.twb` (used by the loader smoke tests) and `stress.twb` (everything-at-once) live one directory up in `tests/fixtures/`.
