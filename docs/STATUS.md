# Research status

This document contains workflow-generated detail. The [README](../README.md) intentionally shows only the headline indicators.

<!-- progress:start -->
## Generated status

`░░░░░░░░░░░░░░░░░░░░░░░░` **0.00%** · **0 / 5,547,712** inventoried executable bytes (verified native matches)

| Module | Function candidates | Native matches | Matched bytes / inventoried bytes | Coverage |
| --- | ---: | ---: | ---: | ---: |
| `EBOOT.BIN` | 4,504 | 0 | 0 / 834,908 | 0.00% |
| `rel_CardAlbum.prx` | 269 | 0 | 0 / 61,196 | 0.00% |
| `rel_DuelEng.prx` | 6,709 | 0 | 0 / 4,651,608 | 0.00% |

The inventories mix as-yet-unclassified game code, CRT/SDK code and candidate function boundaries. This denominator is **only currently inventoried executable text**, not a measured whole-game scope. Overlapping candidates are never summed as coverage. Native byte matches require matched original/rebuilt code digests and recorded evidence; CI cannot reproduce those comparisons without the original binaries.

### C# reconstruction and asset research

| Metric | Recorded value |
| --- | --- |
| Milestone | Stage 5: three primitive state projections compared with PPSSPP |
| Tested C# scenarios | 93 / 93 (2026-10-25, Europe/Berlin) |
| Decoded cards | 7,254 |
| Card getter samples compared with PPSSPP | 4 |
| Card tables compared with PPSSPP memory | 6 tables · 932,344 **asset bytes** |
| Card atlas sprite samples | 16 |
| Card-screen visual acceptance | Partial; complete card-screen comparisons pending |
| Implemented subset | Draws, normal summons, turn/phase transitions, hand-limit discards, deck-out |
| Pending | Battle resolution, executable effects, AI, native UI |

Inventory recorded: 2026-10-16. Tests and research counts are recorded snapshots; CI does not include the original game ISO or rerun PPSSPP evidence capture.

Inputs: `config/progress.json`, `config/native-matches.json` and the inventoried function CSVs under `config/npjh00142/`.
<!-- progress:end -->

## Evidence limitations

Native byte matches, functional equivalence, asset validation, and completeness are different claims. The progress bar only shows native byte matches against the inventoried PSP executable sections. An increasing denominator can reduce the percentage when more modules are inventoried. Validation of committed evidence metadata by CI is not equivalent to fresh binary re-verification.
