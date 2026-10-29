# Shared duel model

Provides immutable two-player duel state and a deterministic command engine.
It handles opening hands, draws, phases, eligible Level 1–4 normal summons,
monster and Spell/Trap sets, position changes, effect-free attacks, battle damage,
destruction, LP defeat, hand-limit discards and deck-out. Card instances reference
the normalized catalog;
explicit gameplay metadata is required rather than guessing binary kind codes.

Battle eligibility and ordinary Spell/Trap placement require explicit opt-in metadata.
There is no renderer, emulator, randomness, response window or effect execution.
Snapshots expose complete engine information; presentation needs a visibility view.
See [development instructions](../docs/DEVELOPMENT.md) for battle scope, API and validation.
