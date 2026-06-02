# Example reports

Three pre-generated HTML reports — open in any browser, no JS framework, no runtime.

| File | Findings | What it shows |
|---|---|---|
| [01-clean.html](01-clean.html) | 0 | The green empty state when no rule fires. |
| [02-deprecated.html](02-deprecated.html) | 3 warnings | Audit pack flagging deprecated `ATTR()` / `WINDOW_VAR()` usage. |
| [03-pii-governance.html](03-pii-governance.html) | 10 mixed | The full governance story — embedded creds, PII patterns, ad-hoc RLS via `USERNAME()`, user-profile filesystem paths. |

## Regenerate

Run the audit on any workbook with HTML output:

```bash
tabkit audit run <workbook>.twbx --format html --out report.html
```

The same data also exports as SARIF (`--format sarif`), which drops directly into the VS Code SARIF Viewer or a GitHub PR check.
