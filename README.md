# Tag Force Special PC

A C# rewrite of the PSP game **Yu-Gi-Oh! ARC-V Tag Force Special** for native PC
play, with a long-term goal of extensible cards and modern duel rules.

**Status:** Research and early duel-engine reconstruction. **Not yet a playable PC port.** No copyrighted game files are included; the planned importer requires a user-supplied, legally obtained game dump.

## Reconstruction approach

The main path is to recover behavior from original-game observations, data and
targeted MIPS analysis, implement understood contracts in C#, and verify them
against reproducible PSP reference scenarios. Reconstructed C or pseudocode can
help explain a function; neither is a mandatory intermediate language.

**Exact native byte matching is optional research, not a requirement for the C#
engine or playable PC build.** Its byte bar measures only that separate track.
Functional fidelity is checked through reference comparisons; PC readiness is
checked by completing supported duels in the packaged application.

Next: close the remaining M3 behavior-reference gaps, add a small supported
effects set and deterministic opponent, then connect input, rendering and local
asset import for a playable sample duel. Preserve the current .NET solution and
keep unsupported behavior explicit.

CI builds the C# solution, tests behavior/reference fixtures and checks evidence
and progress integrity. Native comparison tooling tests are opt-in through the
workflow's `native_tooling` input; original PSP byte comparisons run locally.

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

**Current milestone:** Emergency Teleport End Phase banishment and battle-destruction expiry comparisons pass; full card support and PC integration pending

| Measure | Recorded status |
| --- | --- |
| C# tests | **193/193** (last recorded 2026-10-27) |
| C# acceptance-criterion coverage | N/A (denominator not defined) |
| PSP reference-scenario coverage | N/A (denominator not defined) |
| Decoded cards | 7,254; full visual acceptance remains pending |

**Implemented subset:** Draws, normal summons, monster and Spell/Trap sets, battle position changes and flip summons, effect-free attacks, battle damage and destruction, LP defeat, turn/phase transitions, hand-limit discards, deck-out, opt-in immutable card, position and zone decision sequences (managed tests), experimental Emergency Teleport activation/resolution only (deck Krebons, attack position), experimental End Phase banishment or expiry after basic battle destruction (deck Krebons).

**Still to build:** Battle response windows and effect-modified combat, general effects, shuffle and full delayed effect lifecycle including movement/reset exceptions, AI, native UI.

Passing tests and decoded assets are not a whole-game completion percentage. PSP comparisons cover only their declared inputs and behavior; a supported duel in the PC application remains a separate acceptance gate.

<details>
<summary>Optional native byte-matching research</summary>

`░░░░░░░░░░░░░░░░░░░░░░░░` **0.00%** — 0 / 5,547,712 inventoried executable bytes

**0** native functions matched · **11,482** discovered candidates.

This measures only exact native reconstruction against a partial, unclassified inventory. It is not PC-build progress and does not gate the C# rewrite. CI checks evidence metadata; original-byte comparisons require local proprietary inputs.

</details>

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
- [Development setup, behavior validation and automation](docs/DEVELOPMENT.md)
- [Bounded M3 reference comparisons and limitations](docs/M3_REFERENCE.md)
- [Optional native compiler comparison](docs/NATIVE-COMPARISON.md)

**Accuracy policy:** Passing synthetic tests alone does not establish original-game
fidelity. Reference comparisons prove only their declared behavior and inputs.
Native matches require independently reviewed byte-comparison evidence; neither
those matches nor decoded assets measure whole-game or PC-build completion.
