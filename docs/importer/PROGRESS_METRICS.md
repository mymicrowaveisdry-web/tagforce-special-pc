# Progress and evidence metrics

Import progress is `processed / total` only when both counts are known and total is positive. Otherwise the UI shows indeterminate activity. Reused, skipped, cancelled, unsupported, warning and failed results retain distinct labels.

| Measure | Evidence and scope |
|---|---|
| Discovered files | Inventory rows for one source; not decoded-asset coverage |
| Parsed indexes | Successful bounded archive reads out of applicable files; not payload validation |
| Converted cards/resources | Actual output records for the selected conversion scope |
| Validation | Separate Passed/Warning/Failed/Skipped/NotSupported counts; skipped/unsupported never contribute to passed |
| Function candidates | Unique addresses per module CSV; body-byte sums may overlap and are not executable coverage |
| Asset extraction/validation coverage | Not measured as a universal percentage; no accepted complete resource denominator |
| Binary analysis coverage | Not measured without validated classified ranges and executable denominator |
| Function reconstruction | Not measured without accepted implementation/evidence mappings |
| Native byte matching | Not measured by this adapter; existing native evidence tooling remains authoritative |
| C# behavioral parity | Not measured without scoped original-versus-C# results; no test runner is invoked |

Research status is displayed as persisted evidence, alongside provenance hash matches/missing/changed files. CSV candidates yield Discovered; research records can yield Classified/Named/Under Investigation. The adapter never promotes records to Reconstructed or Verified. Missing fields remain unknown. Imports never reset research history or manufacture progress.
