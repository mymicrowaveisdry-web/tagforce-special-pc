# Roadmap and acceptance criteria

The current implementation is a bounded, headless duel prototype. The milestones below describe the work toward a playable PC game.

## Established baseline

- **Inventory (M1):** 1,512 extracted files indexed locally with hashes; EBOOT, Card Album and Duel Engine function inventories exist. The inventories are incomplete and candidates are not confirmed game-owned code.
- **Asset research (M2):** 7,254 cards normalized; four card getter samples and six loaded tables compared against PPSSPP memory. Full visual card-screen comparisons remain open.
- **Primitive behavior (M3):** three bounded PPSSPP state-projection comparisons pass: summon, end-turn and draw. C# tests use synthetic deck contents, and do not validate RNG, animation timing or effect behavior.
- **Basic battle subset (M3):** monster and Spell/Trap sets, position changes, basic attacks, damage, destruction and LP defeat are implemented. A fourth PPSSPP comparison verifies a direct attack twice: Krebons deals 1,200 damage, taking the opponent from 8,000 to 6,800 LP. Destruction, position changes and LP defeat currently have synthetic test coverage; original-game comparisons remain open.
- **Native MIPS comparison:** Docker PSPDEV GCC 15.2.0 compiled the getter twice. Both trials produced the same measured mismatch: 68 rebuilt bytes versus 84 original bytes. Actual byte validation passes; zero native matches remain recorded.

## Next work (parallel tracks)

| Track | Next deliverable | Acceptance gate |
| --- | --- | --- |
| **Native function reconstruction** | Investigate the first getter's Docker compiler diff using separate MIPS analysis, compiler-trial and C# behavior work units. Preserve the function queue and provenance. | Reproducible local compile/compare and independent validation; no native-match credit without reviewed exact bytes. See [experiment](NATIVE-COMPARISON.md). |
| **M3 — duel edge cases** | Summon-limit and occupied-zone rejections; hand-limit discard; deck-out. | Repeated PPSSPP state projections match C# transitions, including rejections. |
| **M3 — broader battle verification** | Capture monster battles, defense reveals, sets, position changes and LP defeat. | Original-game projections agree with C#; unsupported effects remain explicitly excluded. |
| **M2 — visual acceptance** | Full card-screen checks for Blue-Eyes, Dark Magician, Monster Reborn and Odd-Eyes. | Descriptions, formatting, scrolling and Pendulum scale order have accepted comparison evidence. |

I LOVE ODD-EYES

## Later milestones

| Order | Work | Completion gate |
| --- | --- | --- |
| Native (parallel) | Reconstruct small classified functions through separate MIPS analysis, Docker compiler trials and C# behavior work units. | Source hashes, boundaries, relocations and dependencies are recorded; actual byte comparisons reproduce. Native-match credit requires an independently reviewed exact match. |
| M4 | Reconstruct effect activation, costs, targeting, priorities, chains and triggers for a small supported card set. | Address-backed analysis and repeatable PPSSPP inputs/outputs support the C# behavior; primitive and battle regressions remain green. |
| M4 | Reconstruct tag-duel partner state and opponent decisions; add a bounded opponent for the supported duel subset. | Partner transitions and selected decisions match repeatable reference scenarios; the PC opponent produces legal, reproducible actions. |
| M5 | Connect native UI, input, renderer, deck selection/editor and local asset import into an initial PC build. | A packaged Windows build completes a supported sample duel from start to win/loss without PPSSPP or the PSP toolchain at runtime; imported assets and card presentation pass their checks. |
| M6 | Reconstruct characters, progression and save structures incrementally. | Selected story scenarios match reference observations; read-only save import verifies source identity and preserves the original save. |
| M7 | Add versioned rules, contemporary cards and mechanics after the classic subset is verified. | Each supported addition has explicit acceptance tests; classic PSP-reference regressions remain green, with modern behavior tracked separately. |
