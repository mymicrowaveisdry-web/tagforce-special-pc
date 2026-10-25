# Development and progress automation

## Requirements and build

Use the SDK pinned by `global.json`. From the repository root:

```powershell
dotnet restore ygo-tag-force-special-decomp.slnx --locked-mode
dotnet build ygo-tag-force-special-decomp.slnx --no-restore
dotnet test ygo-tag-force-special-decomp.slnx --no-build --no-restore
dotnet run --project TagForceSpecial.Tools --no-build -- progress
dotnet run --project TagForceSpecial.Tools --no-build -- progress --check
```

The generator updates the marked sections in `README.md` and `docs/STATUS.md`. Handwritten text outside the marker pairs is preserved. The GitHub Actions [progress workflow](../.github/workflows/progress.yml) runs on pushes, PRs, manual dispatch, and weekly; it builds and tests, regenerates both pages, uploads reports for PRs, and commits refreshed pages on the default branch when write permissions allow.

## What the byte bar means

`matched native function bytes / total currently inventoried executable-section bytes`

The denominator comes from `config/progress.json` across listed inventories, **not** from a measured complete game-code classification. The numerator comes only from `config/native-matches.json` plus the corresponding integrity-checked evidence records. It starts at **0** and does not grow from Ghidra pseudocode, discovered candidates, C# tests or asset comparisons.

The CI runner does not have a game ISO and **cannot reproduce the actual MIPS byte comparison**. It checks the manifest, supported function address and length, overlap, game-code classification assertion, SHA-256 digest equality and the digest of the committed evidence record. This is an evidence register, not independent attestation that a compiler reproduced a proprietary original function. Every positive entry must be produced by a separately reviewed local comparison before committing.

## Recording a verified native function

Only after a verified PSP-targeted compile and exact byte comparison, write a non-proprietary report under `config/match-evidence/` and add an entry to `config/native-matches.json`:

```json
{
  "module": "EBOOT.BIN",
  "address": "0x<verified function address>",
  "length": 0,
  "kind": "game-c",
  "sourceSha256": "<module SHA-256 as in config/progress.json>",
  "originalSha256": "<SHA-256 of the original function bytes>",
  "rebuiltSha256": "<SHA-256 of compiled PSP function bytes>",
  "evidencePath": "config/match-evidence/<record>.json",
  "evidenceSha256": "<SHA-256 of the committed evidence JSON>"
}
```

The example is **not valid until placeholders are replaced** and length equals the candidate size in the module's function CSV. `kind` can be `game-c` or `game-asm`. Avoid storing the original binary bytes, assets or personal paths in the public evidence. Records may describe the toolchain, compiler flags, hashes, tested function boundaries and comparison output.

The tracker rejects unknown modules, mismatched source hashes, unrecognized addresses, partial functions, invalid digests, contradictory original/rebuilt hashes, duplicate or overlapping match regions, missing evidence files and coverage exceeding the module's inventoried executable bytes. The tracker does not certify arbitrary matches as native source-equivalent; evidence should be peer reviewed.

For **C#-only behavior reconstructions**, keep test evidence and address provenance in the research records; do not add them to the native match manifest.
