# Roadmap and acceptance criteria

The repository's Stage 0–8 work breakdown and the engineering handbook's M1–M7 are not interchangeable. In particular, **Stage 5 means a bounded, headless duel prototype**, not a playable PC game.

## Established baseline

- **Inventory (M1):** 1,512 extracted files indexed locally with hashes; EBOOT, Card Album and Duel Engine function inventories exist. The inventories are incomplete and candidates are not confirmed game-owned code.
- **Asset research (M2):** 7,254 cards normalized; four card getter samples and six loaded tables compared against PPSSPP memory. Full visual card-screen comparisons remain open.
- **Primitive behavior (M3):** three bounded PPSSPP state-projection comparisons pass: summon, end-turn and draw. C# tests use synthetic deck contents, and do not validate RNG, animation timing or effect behavior.
- **Native MIPS decompilation:** no functions or executable bytes are byte-matched yet. A PSP comparison toolchain still needs to be built.

## Next work (parallel tracks)

| Track | Next deliverable | Acceptance gate |
| --- | --- | --- |
| **Stage 5R — native reconstruction** | Identify one card getter address, export its MIPS instructions, recover C# behavior and record source-level provenance. | PPSSPP tests for the observed inputs; do **not** claim a native byte match without a PSP build and binary comparison. |
| **M3 — duel edge cases** | Summon-limit and occupied-zone rejections; hand-limit discard; deck-out. | Repeated PPSSPP state projections match C# transitions, including rejections. |
| **M2 — visual acceptance** | Full card-screen checks for Blue-Eyes, Dark Magician, Monster Reborn and Odd-Eyes. | Descriptions, formatting, scrolling and Pendulum scale order have accepted comparison evidence. |

## Later milestones

| Order | Work | Completion gate |
| --- | --- | --- |
| M3 | Set cards, position changes, basic attacks, damage, destruction, LP win | A measured battle scenario passes with existing primitive tests green. |
| M4 | Effect activation, costs, targeting, priorities, chains and triggers | A small trace-backed card set matches reference results. |
| M4 | Tag-duel partner behavior and opponent decision layer | Partner state and repeatable tag scenarios are verified. |
| M5 | Native UI, input, renderer, deck editor, asset importer | One sample duel completes in the new PC application. |
| M6 | Characters, progression and save migration | Story smoke tests and verified read-only save import. |
| M7 | Versioned rules, contemporary cards and mechanics | Modern tests pass without breaking classic regression scenarios. |

No calendar or whole-project percentage is inferred from these milestones. Prioritize independently verifiable functions and behaviors before broader architecture reconstruction.
