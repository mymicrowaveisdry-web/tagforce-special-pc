# Development and progress automation

## Requirements and build

Use the SDK pinned by `global.json`. From the repository root:

```powershell
dotnet restore ygo-tag-force-special-decomp.slnx --locked-mode
dotnet build ygo-tag-force-special-decomp.slnx --no-restore
dotnet test ygo-tag-force-special-decomp.slnx --no-build --no-restore
dotnet run --project TagForceSpecial.Tools --no-build -- progress
dotnet run --project TagForceSpecial.Tools --no-build -- progress --check
dotnet run --project TagForceSpecial.Tools --no-build -- research-check
```

The generator updates the marked sections in `README.md` and `docs/STATUS.md`.
The [progress workflow](../.github/workflows/progress.yml) builds/tests and validates
research metadata. PRs regenerate using the committed snapshot date, check test
counts against the executed suite, and run `git diff --exit-code`. Default-branch
runs may refresh only the generated pages and test snapshot through the controlled
refresh job. Hosted CI never runs the proprietary PSP comparison.

## Python tools and local inputs

Install Python dependencies with `python -m pip install -r requirements.txt`.
The root file forwards to the canonical pinned list in
`native/python-requirements.txt`, also used by the Docker image and CI.

```text
python -m unittest discover -s tests -p "test_*.py"
python -m unittest discover -s native -p "test_*.py"
python tools/validate_duel_reference.py --iso PATH_TO_LOCAL_ISO
python tools/validate_duel_battle.py --iso PATH_TO_LOCAL_ISO
```

The first two commands use synthetic data. The validators require the matching
local dump and private captures, verify their hashes, and regenerate sanitized
fixtures. Game inputs, captures, installed tools and build outputs stay ignored;
source tools, tests, dependency lists and reviewed project docs are versioned.
Historical research/migration reports remain local. Windows window capture and
PPSSPP process control run on the host; offline processing and compiler work can
use the persistent Docker service described in `native/USAGE.rst`.
`.gitattributes` preserves exact fixture/compiler-source bytes and uses LF for
other text so provenance checks also work in fresh Windows/Linux checkouts.

## What the byte bar means

`matched native function bytes / total currently inventoried executable-section bytes`

The denominator comes from `config/progress.json` across listed inventories, **not** from a measured complete game-code classification. The numerator comes only from `config/native-matches.json` plus the corresponding integrity-checked evidence records. It starts at **0** and does not grow from Ghidra pseudocode, discovered candidates, C# tests or asset comparisons.

The CI runner does not have a game ISO and **cannot reproduce the actual MIPS byte comparison**. It checks the manifest, supported function address and length, overlap, game-code classification assertion, SHA-256 digest equality and the digest of the committed evidence record. This is an evidence register, not independent attestation that a compiler reproduced a proprietary original function. Every positive entry must be produced by a separately reviewed local comparison before committing.

## Recording a verified native function

Keep `config/native-matches.json` empty until an independent reviewer reruns a PSP
compile and actual byte validation. The local [experiment](NATIVE-COMPARISON.md)
does not modify this manifest, even if a compiler trial reports equal bytes.

Positive entries require complete inventoried boundaries, source-module identity,
equal original/rebuilt digests and a hashed review under `config/match-evidence/`.
`NativeEvidenceReview` links distinct native and analysis work units, the declared
reconstructed source and sanitized `NativeComparisonSummary` metadata. The summary
must identify the compiler/flags, exact lengths, zero differences, reconstructed
source hash and private comparison-report hash. The loader rehashes committed
source/summary files; matching digest strings alone are rejected. Metadata and work
unit IDs cannot authenticate who ran a compiler: independent local rebuild/review
remains mandatory. Never commit raw bytes, assets, private command logs or paths.

For **C#-only behavior reconstructions**, keep test evidence and address provenance in the research records; do not add them to the native match manifest.

## Research queue and coverage

`config/research-queue.json` preserves the original candidate CSVs and records only
bounded, classified work. `research-check` validates its strict JSON contract,
source hashes, complete candidate lengths, nonoverlapping ranges, provenance and
acyclic function/work-unit dependencies. `--local` additionally rehashes private
evidence. Public evidence is always rehashed. Structural JSON Schemas are beside
the manifests; the .NET validator also enforces cross-file constraints.

`config/coverage.json` separates C# acceptance criteria from PSP reference
scenarios. Both coverage values are **N/A** until their complete denominators are
defined. A future versioned scope must enumerate every criterion; accepted entries
need fixture and test provenance. Current observations do not define whole-engine
coverage. Historical getter samples retain their missing-anchor/transcript caveat.

## Basic battle subset

The command engine supports `SetMonster`, `SetSpellTrap`, `ChangeMonsterPosition`
and `DeclareAttack`. A null attack target means a direct attack. Monster sets share
the normal summon allowance; ordinary Spell/Trap sets use separate back-row slots.
Position changes include Flip Summons and enforce summon-turn, once-per-turn and
attack restrictions. Battle resolution handles attack/defense comparisons, reveals,
damage, destruction and LP defeat. Published snapshots remain immutable.

`DuelCardRules.AllowsBasicBattle` must explicitly opt a monster into unmodified
combat without effect resolution. Do not enable it when omitted mandatory or
continuous effects would change the result. `AllowsSpellTrapSet` only permits
ordinary back-row placement; Field Spells and activation are outside this subset.
Unknown battle statistics reject the command without changing state. Battles are
atomic: response windows, chains, attack replays, piercing, stat modifiers and
effect-based exceptions are not implemented. LP defeat ends the duel during
damage calculation, before subsequent destruction processing.

Synthetic battle tests follow the relevant basic rules in the
[official rulebook](https://www.yugioh-card.com/en/downloads/rulebook/SD_RuleBook_EN_10.pdf).
This is a rules reference, not proof of original-game fidelity.

`config/reference/duel-battle.json` records a measured direct attack on turn 3:
Krebons (local ID 7716, ATK 1200) attacks from zone 2, and the opponent's LP changes
from 8000 to 6800. Optional activations were declined, and both before/after
projections repeat after restoring a private save-state anchor. The comparison
checks LP, hand/deck counts, monster IDs, active player, turn and phase. Synthetic
C# deck contents reproduce these fields, not original identities/order or hidden
state. The test's explicit Krebons battle opt-in applies only to this no-activation
scenario and does not implement that card's effect.

Private snapshots, input transcripts, frames and `m3-attack-baseline.ppst` remain
under `analysis/duel-reference/`. The local `tools/validate_duel_battle.py` verifies
ISO/module hashes, RAM hashes, loaded module base, 128 runtime/static entry bytes
and repeated projections before generating the public fixture. Original-game
comparisons for destruction, LP defeat, sets and position changes remain pending.

Run the full .NET suite and `dotnet run --project TagForceSpecial.Tools --no-build -- duel-demo`
to verify the implementation and demonstrate equal-ATK destruction and a direct attack.
