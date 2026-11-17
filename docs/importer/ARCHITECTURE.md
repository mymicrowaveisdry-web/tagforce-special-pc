# Importer architecture

`TagForceSpecial.Import` owns source recognition, orchestration, immutable output caching, session manifests, diagnostics, validation, maintenance and inspection. It calls existing Assets readers and Cards models. PC, Tools and the Windows WPF Workbench consume that core; UI classes do not decode original formats.

An import checks the source, inventories files, converts the catalog, decodes the selected presentation scope, carries/imports prepared recipes, validates outputs and publishes a session. Progress reports measured fractions only; otherwise front ends show indeterminate activity. Cancellation retains completed outputs and preserves the previous active import.

| Path under private data root | Contract |
|---|---|
| `import/active.json` | Session ID and SHA-256 of its manifest |
| `import/sessions/<id>/` | Manifest, validation JSON, Markdown report and JSONL diagnostics |
| `import/stages/<kind>/<key>/` | Reusable generated artifacts keyed by source/options/converter identity |
| `import/import.lock` | Exclusive lease shared by import and cleanup |
| Decks, fonts, profile and preferences | Player data; excluded from importer deletion |

`InstalledGameReader` verifies the active pointer and opens pinned catalog/presentation data. Inspection verifies output hashes and checks original-file hashes before preview. Legacy cleanup previews owned paths and blocks until existing recipes have a verified replacement. Unused-output cleanup keeps the active session plus three recent successful sessions. Plans are rechecked under the lease before deletion; linked paths are rejected.

WPF uses plain property notifications, a background import view model and separate page owners. Dispatcher callbacks carry progress/log entries to the UI. No new NuGet package is required. The shared CSV reader also backs existing CLI progress; extraction does not increase decompilation metrics.
