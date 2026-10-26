# Yu-Gi-Oh! ARC-V Tag Force Special — C# Reconstruction

Reverse-engineering and rebuilding the PSP game as a native PC application in C#/.NET, with a long-term goal of extensible cards and modern duel rules.

**Status:** Research and early duel-engine reconstruction. **Not yet a playable PC port.** The original PSP executable has not been byte-matched. No copyrighted game files are included; the planned importer requires a user-supplied, legally obtained game dump.

## Quick start

Requires the .NET SDK selected by [`global.json`](global.json).

```powershell
dotnet restore ygo-tag-force-special-decomp.slnx --locked-mode
dotnet build ygo-tag-force-special-decomp.slnx --no-restore
dotnet test ygo-tag-force-special-decomp.slnx --no-build --no-restore
dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo
```

The demo uses synthetic card data; it is not the original game.

<!-- progress:start -->
## Progress

**Verified native PSP code bytes** (relative to currently inventoried executable text):

`░░░░░░░░░░░░░░░░░░░░░░░░` **0.00%** — 0 / 5,547,712 bytes

**0** native functions matched · **11,482** discovered candidates · **7,254** decoded cards · **93/93** C# tests (last recorded 2026-10-26)

**Current milestone:** Stage 5: three primitive state projections compared with PPSSPP

This is **not** a whole-game completion percentage: the denominator is a partial, unclassified inventory and may grow. CI validates committed match evidence metadata; it cannot rerun PSP byte comparisons without the proprietary game binaries. C# behavioral matches and asset decoding are separate metrics.

[Detailed status](docs/STATUS.md) · [Roadmap](docs/ROADMAP.md) · [Updating progress](docs/DEVELOPMENT.md)
<!-- progress:end -->

## Solution layout

| Project | Responsibility |
| --- | --- |
| `TagForceSpecial.Assets` / `.Cards` | PSP resource readers and card catalog |
| `TagForceSpecial.Core` | Headless duel-state reconstruction |
| `TagForceSpecial.Effects` / `.AI` | Planned effect engine and opponent AI |
| `TagForceSpecial.PC` | Planned native application host |
| `TagForceSpecial.Tools` | Importers, research utilities and progress generation |
| `TagForceSpecial.Tests` | Synthetic tests and bounded PPSSPP reference comparisons |

## Project documentation

- [Progress and evidence status](docs/STATUS.md)
- [Milestones and research roadmap](docs/ROADMAP.md)
- [Development setup, byte-matching evidence and automation](docs/DEVELOPMENT.md)

**Accuracy policy:** A decoded asset or successful C# behavioral test is not proof of matching original executable bytes. Native matches are tracked separately and only counted with versioned comparison evidence.
