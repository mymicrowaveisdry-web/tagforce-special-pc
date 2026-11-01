# Bounded Fusion and Ritual effects

Fusion and Ritual use authored Spell effect profiles and shared summon operations.
They do not infer recipes, card text or family identity from catalog codes. All
new tests are synthetic; no original-game Fusion/Ritual comparison is claimed.

Register `SummonEffectProfile` through `experimentalSummonEffects`, with either
`FusionSummonRecipe` or `RitualSummonRecipe` entries and a proper-procedure ID.
Targets also require `experimentalSpecialSummonCards`; Fusion targets need
`AllowsExtraDeck`, Fusion materials `AllowsSummonMaterial`, and Ritual Tributes
`AllowsTributeForSummon`. Catalog monster identity alone grants no permission.

## Resolution contract

Activate the registered Spell from hand in an open supported Main Phase. A legal
summon must be possible before activation. The Spell enters the normal response
chain; materials and the summoned target remain in their source zones until the
effect resolves. Activation/effect negation consumes no resolution materials.

At resolution, the effect requests an immutable selection of its target and
materials from the current state, plus a destination and face-up position. Own
hand/field sources are explicitly profiled, including whether face-down field
cards are permitted. Selections identify distinct instances and current field
placements. All eligibility, recipe/level and destination checks precede movement;
an invalid answer preserves state and events. A zone freed by selected materials
is usable. When no legal combination remains, resolution skips the summon and
finishes the chain's ordinary Spell cleanup.

Read `State.PendingChain.SummonDecision`, then submit `ChooseEffectSummon` with
its player and decision ID, target instance ID, material instance IDs, zone and
position. `Targets` and `Materials` expose immutable options; field material
options retain `MonsterBinding`. This command is accepted only for that pending
resolution, and cannot perform a standalone summon.

Fusion selects an Extra Deck target and two to five materials matching the exact
authored recipe, including repeated card IDs. Ritual selects a hand target and one to five Tributes with a printed-level
sum exactly equal to the target's printed level. The Ritual target cannot also be
a Tribute; registered Xyz targets cannot supply Ritual levels. Field and hand cards move
to their owners' Graveyards with distinct material/Tribute causes, never as
destruction. Xyz attachments on a departing host use the shared cleanup path.

Successful summons reuse source removal, fresh placement, proper-summon history
and event collection, preserve the Normal Summon allowance, and bypass standalone
summon-negation windows. Supported material/summon triggers wait for the chain
boundary. Revival eligibility belongs to the successfully summoned instance.

The ordinary rules basis is the [official v9.0 rulebook](https://www.yugioh-card.com/eu/wp-content/uploads/2022/07/Rulebook_v9_en.pdf),
pages 18–19; this is separate from PSP evidence.

## Limits

No substitutes, contact Fusion, opposing/Deck/Graveyard materials, tokens,
Pendulum departures, alternate destinations or replacements, Ritual overpayment,
effective-level modifiers, double-value Tributes, or arbitrary Spell instructions.
Do not register cards requiring these branches. General card legality, shuffle
and earlier research-stop restrictions remain outside this slice.
