# Yu-Gi-Oh! ARC-V Tag Force Special PC

A C# rewrite of the PSP game **Yu-Gi-Oh! ARC-V Tag Force Special** for native PC
play, with a long-term goal of extensible cards and modern duel rules.

**Status:** Early duel-engine reconstruction. The PC host is a placeholder;
this is not yet a playable PC port. No original game assets are included.

## Quick start

Requires the .NET 10 SDK. These commands work with the committed source and
synthetic inputs; no game dump, Docker, Python tooling or local research files
are required.

```powershell
dotnet restore ygo-tag-force-special-decomp.slnx -p:RestorePackagesWithLockFile=false
dotnet build ygo-tag-force-special-decomp.slnx --no-restore -p:IncludeReferenceFixtures=false
dotnet test ygo-tag-force-special-decomp.slnx --no-build --no-restore -p:IncludeReferenceFixtures=false --filter 'Category!=LocalReference'
dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo
```

The console demo uses invented cards and exercises the headless engine. It is
not the original game or a graphical PC application.

## Implemented scope

The deterministic two-player engine supports opening hands, draws, phase and
turn transitions, eligible normal summons, monster and Spell/Trap sets, battle
position changes, flip summons, explicitly enabled effect-free attacks, battle
damage and destruction, LP defeat, hand-limit discards and deck-out.

Opt-in immutable decision sequences select card instances, positions and empty
monster zones. Experimental Emergency Teleport profiles support a bounded deck
Krebons / attack-position branch, with either a stop after resolution or an End
Phase banishment lifecycle. They do not implement the full card.

Separate activation-only research contracts support a discard cost and binding
a Spell/Trap target to its card instance and field placement. These stop before
responses or effect resolution unless a separate chain research profile is enabled.
That default-off slice supports explicit passes, Vortex resolution and a targeted
Typhoon/Scapegoat two-link chain through token creation and cleanup, then stops.
Additional default-off response profiles support set Typhoon responses and a
discard-paid Magic Jammer branch. The engine distinguishes activation negation
from effect-only negation, preserves paid costs, and lets Typhoon finish without
destruction when its bound target is gone. These contracts follow native static
analysis with synthetic interaction tests; fresh PSP negation/interruption replays
remain pending. They do not make those cards generally playable; stage 6 is in progress.

Stage 6's fresh PSP comparisons are deferred for later. The first Stage 7 slice
adds opt-in monster event triggers, player-selected simultaneous ordering,
follow-up chains, continuous ATK/DEF auras and temporary stat modifiers. Basic
battle consumes effective stats; removing sources and expiring modifiers update
the immutable snapshot. These are rules-informed synthetic-tested primitives,
with no new full-card or native timing support claim. See [Core](TagForceSpecial.Core/README.md#stage-7-event-driven-triggers-and-continuous-stats).

General effects, broader response windows/chains, shuffle, delayed-effect
handling, opponent AI, rendering, input and first-launch asset import remain
unfinished. The next application milestone is a small supported duel with a
deterministic opponent, player-specific visibility and a complete win/loss flow.

The card catalog accepts authored additions with full-width IDs, direct stats
and optional import metadata. Catalogs can be merged without losing each card's
origin. PSP encoding constraints are checked at import; additional cards still
need explicit gameplay support. See [Cards](TagForceSpecial.Cards/README.md).

## Validation and research

[CI](.github/workflows/progress.yml) restores and builds the C# solution, runs
tests that do not require local reference fixtures, and publishes a test summary
and TRX artifact. It does not regenerate this README or commit progress reports.
Use the workflow run's results for current test counts.

Tests marked `Category=LocalReference` read PSP observation projections from
local JSON fixtures. Those fixtures are excluded from this branch, so the quick
start disables copying them and filters those tests out. Running them requires
the corresponding local fixtures, a build with `IncludeReferenceFixtures=true`,
and a test run without that filter.

Decompilation scripts, native comparison infrastructure, Docker configuration,
research JSON files and Markdown notes under `docs/` remain local. The retained
legacy progress/evidence commands depend on those local research files and are
not part of the clean-checkout workflow.

Original-game fidelity requires bounded reference comparisons with declared
inputs and limitations. Passing synthetic tests does not establish full PSP
equivalence. Exact native byte matching is separate research; neither it nor
decoded asset counts measures completion of the C# engine or PC application.

## Solution layout and documentation

| Project | Responsibility |
| --- | --- |
| [Assets](TagForceSpecial.Assets/README.md) | PSP resource readers and packed-property decoding |
| [Cards](TagForceSpecial.Cards/README.md) | Immutable normalized card records and catalog |
| [Core](TagForceSpecial.Core/README.md) | Headless duel state, commands, decisions and scoped spell lifecycle |
| [Effects](TagForceSpecial.Effects/README.md) | Experimental Emergency Teleport profiles |
| [AI](TagForceSpecial.AI/README.md) | Placeholder for deterministic opponent logic |
| [PC](TagForceSpecial.PC/README.md) | Placeholder application host and planned asset setup |
| [Tools](TagForceSpecial.Tools/README.md) | Synthetic duel demo, local asset inspection and legacy research commands |
| `TagForceSpecial.Tests` | Synthetic tests and optional local reference comparisons |
