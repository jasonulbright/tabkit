# External test corpus — attribution

This directory holds workbooks downloaded from third-party sources for synthetic
parser/audit/inventory testing. The files themselves are **not committed** —
run `./fetch.ps1` to populate them locally.

## Sources

### `book/` — Visual Analytics with Tableau (Wiley)

- **Upstream**: https://github.com/aloth/tableau-book-resources
- **Author**: Alexander Loth
- **License**: Creative Commons Attribution 4.0 International (CC BY 4.0)
- **Contents**: 12 `.twbx` workbooks + 1 `.tfl` prep flow spanning charts,
  parameters, calculated fields, dual-axis, maps, k-means clustering, dashboards.
- **Attribution**: derived from "Visual Analytics with Tableau" by Alexander Loth
  (Wiley), licensed under CC BY 4.0. License text:
  https://creativecommons.org/licenses/by/4.0/

### `server-client/` — Tableau Server Client (Python)

- **Upstream**: https://github.com/tableau/server-client-python
- **Author**: Tableau Software, LLC (a Salesforce company)
- **License**: MIT
- **Contents**: 2 fixtures from `test/assets/` — `RESTAPISample.twb`,
  `SampleWB.twbx`
- **Attribution**: bundled MIT license terms apply to these files.

## Why fetch-on-demand instead of vendoring

- CC BY 4.0 requires attribution preservation; cleaner to keep the binaries out
  of git history and tie attribution to the live upstream rather than fork it.
- Workbooks are several MB each; vendoring would inflate the repo.
- Upstream may publish corrections; on-demand fetch tracks them.
