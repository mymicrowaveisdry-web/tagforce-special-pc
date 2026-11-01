# Extra Deck and material movement

This supports explicit generic material procedures and bounded Synchro profiles; tests use authored scenarios. The retained Synchro projection is narrower than full material selection/movement/timing equivalence.

## Extra Deck

`playerOneExtraDeck` and `playerTwoExtraDeck` are optional, separately supplied
ordered lists. Each permits 0–15 explicitly `DuelCardRules.AllowsExtraDeck`
catalog monsters. Their card instances have unique game-local instance IDs and
owners, and are held in immutable `DuelPlayerState.ExtraDeck` snapshots. This
list treats entries as face-down.
No monster type, level or ID is used to infer Extra Deck eligibility.

## Material procedure

Register a `SpecialSummonCardProfile` for each target and an
`ExtraDeckMaterialProcedureProfile` through `experimentalMaterialProcedures`.
The procedure names explicit targets, an explicit material-ID allowlist, an exact
count from 1–5, a Graveyard or Banished destination, and whether materials must
be face-up. Each source also needs `AllowsSummonMaterial`. Both flags are trusted
gameplay opt-ins; token history is rejected until token movement exists.

```csharp
var command = new SpecialSummonWithMaterials(player, engine.State.Revision,
    "registered-material-procedure", extraDeckInstanceId, destinationZone,
    selectedMonsterBindings, BattlePosition.FaceUpAttack);
var result = engine.Execute(command);
```

Selections bind the current controller, zone, card instance and uninterrupted
placement. They must be distinct own-field placements satisfying the profile.
The target must remain in the actor's Extra Deck, and destination/position and
proper-procedure eligibility must pass after the selected removals. A destination
freed by a selected material is allowed. All checks precede publication; rejection
keeps the identical snapshot and emits no events. The Normal Summon/Set allowance
is unchanged.

Selected materials move simultaneously, in ascending zone order for deterministic
events, to their owners' Graveyards or Banished lists. `SummonMaterialSentToGraveyard`
creates a Graveyard occurrence, while `SummonMaterialBanished` does not. Neither
is destruction or a Tribute. Supported Graveyard triggers are collected and
continuous sources rederived. Optional When triggers use the existing timing
groups: the later summon/attempt can make a material movement miss timing.

The target leaves the Extra Deck and uses the shared Special Summon placement,
history and trigger flow. A matching required procedure grants proper-summon
qualification only on success; returning to this face-down Extra Deck clears it.
A configured summon-negation window commits materials first and holds the target
outside the field. On negation materials stay moved, the target enters its owner's
Graveyard without qualification, and no successful-summon event is emitted.
Counter-negating the negator permits completion. Effect-driven summons do not use
this procedure window.

For a Synchro procedure, supply `SynchroMaterialRequirements` with disjoint,
explicit Tuner and non-Tuner card IDs and the allowed non-Tuner count range.
Exactly one Tuner and at least one non-Tuner must be face-up on the controller's
field, and their nonzero printed levels must exactly equal the target's nonzero
printed level. The profile uses the minimum total as `materialCount` and sends
materials to the Graveyard. Selection, movement and negation use the shared flow.
These authored profiles do not infer Tuner identity from card data. Field-level
modifiers, multiple Tuners, card-specific material conditions and alternative
material destinations are unsupported; do not register profiles that need them.

This standalone procedure does not implement effect-driven Fusion/Ritual selection, opponent-controlled
materials, tokens, Pendulum hybrid procedures, material replacement,
or full card-specific restrictions. Existing control-change and general banishment
contracts remain separate work. Explicitly omit unsupported cards from profiles.
