# Extra Deck and material movement

This supports explicit generic procedures and bounded Synchro/Xyz profiles;
[Fusion/Ritual effects](EffectSummons.md) and [Pendulum](Pendulum.md) use separate profiles. Its tests are synthetic except for the retained narrow Synchro projection; original PSP material
selection, movement and timing still need gameplay comparisons.

## Extra Deck

`playerOneExtraDeck` and `playerTwoExtraDeck` are optional, separately supplied
ordered lists. Each permits 0–15 explicitly `DuelCardRules.AllowsExtraDeck`
catalog monsters. Their card instances have unique game-local instance IDs and
owners, and are held in immutable `DuelPlayerState.ExtraDeck` snapshots. This
list treats entries as face-down; Pendulum uses the separate `FaceUpExtraDeck` list.
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
`SynchroMaterialRequirements.AnyTuner` with `anyMaterial: true` reads Tuners from
`DuelCardRules.IsTuner` instead of listed IDs. Identity is never inferred from card data. Field-level
modifiers, multiple Tuners, card-specific material conditions and alternative
material destinations are unsupported; do not register profiles that need them.

For Xyz, supply `xyz: new XyzMaterialRequirements(rank)` and
`MaterialDestination.Attached` with two to five materials. Each selected face-up
non-token must have a printed level equal to the authored rank; registered Xyz
targets have no level for material checks. `FieldMonster.XyzMaterials` and
`PendingSummon.XyzMaterials` retain immutable ordered attachments outside field
zones and Graveyards. Negating the summon or removing the host sends attachments
to their owners' Graveyards without destruction or field-departure events; revival
does not restore them. A trigger's `detachMaterialCount` requests
`ChooseTriggerXyzMaterials` through `PendingTriggerCommitment.CardOptions` before
responses; paid materials stay detached if the activation/effect is negated.
An activated monster effect with `DetachCost` carries its choice in the command's
`DetachInstanceIds`. With `anyMaterial: true`, any allowed monster of the right Level
can be used. Attachment effects, alternate Xyz procedures, level modifiers and
material replacement remain outside this contract.
The rules contract follows [official rulebook v9.0](https://www.yugioh-card.com/eu/wp-content/uploads/2022/07/Rulebook_v9_en.pdf),
pages 14–15 and 47; Xyz original-game comparisons remain pending.

This standalone procedure does not implement effect-driven Fusion/Ritual selection, opponent-controlled
materials, tokens, Pendulum hybrid procedures, material replacement,
or full card-specific restrictions. Existing control-change and general banishment
contracts remain separate work. Explicitly omit unsupported cards from profiles.
