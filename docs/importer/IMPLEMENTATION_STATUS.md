# Importer implementation status

| Area | Implemented | Limits |
|---|---|---|
| Sources | Read-only ISO/extracted-directory import; PARAM.SFO recognition, structure and trusted-build support are separate findings | English v2.1 exact image/tree pins; other builds can be inventoried but are not game-ready |
| Pipeline | Catalog, presentation, prepared recipes, complete inventory, validation, immutable keyed outputs, cancellation, atomic active pointer | Audio/dialogue conversion, encrypted saves and arbitrary archive decompression are unsupported |
| Game integration | New import layout only; source selection, recipes, repeat/cancel, results and confirmed legacy cleanup | Native dialogs and cleanup confirmation need human acceptance |
| Dashboard / Import / Settings | Session metadata/results, source verification, options, progress/cancel, recent sources, settings and cleanup previews | Windows WPF; settings are local |
| Asset Explorer | Directory tree, virtualized searchable/sortable table, metadata/hex, EHP members, GIM first-frame preview/PNG export, normalized cards | Compressed/audio/script/executable content remains metadata-only; source must be available and unchanged |
| Validation / Logs | Historical checks, artifact revalidation, JSON/Markdown export, searchable diagnostics, copy/export and view-only clearing | Revalidation does not repeat source verification; checks are scoped |
| Decompilation Analysis | Existing CSV candidates and research provenance, evidence hashes, separate unknown metrics | No disassembler, reconstruction verifier or behavior-test runner; native matching and C# parity show Not measured |
| Comparison | Two sessions: identities/options, decoder versions, keys, artifact/file hashes, validation outcomes and card counts | Historical outputs must still exist; no general semantic round-trip or PSP behavior comparison |

Windows verification: root solution builds 12 projects with zero errors/warnings; real ISO GUI import, all eight routes, inventory search/sort, EHP/GIM inspection, cards, reports, logs, function inventory and artifact revalidation exercised in the actual WPF window. In-game setup/duel and presentation smokes passed. Actual desktop captures are retained in an ignored local folder. Unit tests compile but have **not been run**. Dialogs, clipboard/export destinations and full keyboard accessibility remain human acceptance items.

Measured English v2.1 import: 1,512 files, four unclassified signatures, one unreadable EHP index, 7,254 cards, 1,333 presentation resources and 203 prepared recipes. Older saved inventories can have different classifications; repeat import refreshes them. The [card-count audit](FORMATS.md) identifies one sentinel slot rather than an omitted card.
