# Shared Special Summon foundation

This is a bounded, explicitly profiled foundation. [Extra Deck and material movement](ExtraDeck.md)
use separate profiles; [Fusion/Ritual effects](EffectSummons.md) and [Pendulum](Pendulum.md) reuse this foundation, while complete card text remains separate work. Tests use authored scenarios;
no new PSP gameplay comparison is claimed.

## Eligibility and procedures

`experimentalSpecialSummonCards` registers immutable `SpecialSummonCardProfile`
objects: catalog monster ID, allowed effect-source mask, and an optional required
proper procedure ID. A policy narrows existing Spell/battle effect allowlists;
it does not grant an effect, activation or a standalone summon command.

`experimentalSpecialSummonProcedures` registers costless, non-activated
`SpecialSummonProcedureProfile` objects: unique ID, explicit card IDs, own Hand /
Graveyard / Banished source, allowed face-up positions, and optional empty-own-field
and opposing-monster requirements. Every target needs a card profile. These are
authored procedures, not inferred support for real cards. Source Deck is reserved
for registered effects; there is no unrestricted deck-search procedure.

```csharp
var command = new SpecialSummonByProcedure(player, engine.State.Revision,
    "registered-procedure", instanceId, zone, BattlePosition.FaceUpDefense);
var result = engine.Execute(command);
```

The command requires an open Main Phase, correct actor/revision, matching source
instance and procedure, permitted position, empty destination and satisfied
conditions. Rejections preserve the identical state and emit no events. A Special
Summon does not spend or reset the Normal Summon/Set allowance. Normal Summon
eligibility alone never grants Special Summon permission.

Shared preflight/resolution validation checks the exact source instance, category,
position, destination and proper-summon requirement. Existing deck Spells preflight
and filter choices before activation. Battle-response summons validate before
activation and again on resolution; a missing source or occupied zone skips the
summon. Both use shared source removal, fresh placement and MonsterSpecialSummoned
events. Existing research-stop, shuffle and lifecycle limits are preserved.

## History and proper qualification

`State.SummonHistory` is an immutable ledger of the latest attempt/placement per
instance, not a complete event log or a live field binding. It records method,
source, player, position, zone, attempt/completion revision, placement, status,
procedure and retained proper-procedure ID. Normal/Set/Flip and legacy token
placements also participate; a completed Set record does not emit a summon event.

A matching hand or Extra Deck material procedure can establish initial proper
qualification; it does so only on successful completion. A restricted card cannot
use an ordinary hand/deck effect to bypass that requirement. Graveyard/banished
revival checks the same instance's retained proper ID and cannot borrow a copy's
history. Public-zone revival preserves that qualification and creates a fresh
placement. Negated first attempts never qualify. Returning a card to Hand/Deck or
this slice's face-down Extra Deck
clears retained qualification; those movement effects themselves remain future work.
The procedure ID is trusted configuration, not a card-family or PSP encoding code.

Token creation records method Token with no source/proper ID; this preserves the
existing frozen token scenario and does not implement general token departure or
revival. Existing End Phase banishment retains history but still stops its research
slice. The Banished source uses the existing list, without a new face-down banishment
or general movement contract. No hydration API was added for testing these paths.

## Negation boundary

A procedure may opt into `SummonWindowProfile`; the held card is outside the field
until success. `PendingSummon.IsSpecialSummon`, source, chosen position and procedure
metadata survive passes, response chains and cost decisions. A negating definition
lists the kinds it answers in `MonsterWouldBeSummonedCondition`.
The existing opposing set Counter Trap chain rules apply. A counter-negated negator
allows success; a successful negator sends the unsuccessful target to its owner,
records Negated, grants no new proper status and emits no successful summon event.

Summons performed during an activated effect's resolution never open this separate
window. Their effect/activation can be negated through its supported chain contract.
Successful Special Summon triggers run through the existing collector, after chain
completion; battle-response field changes still drive attack replay.

These distinctions follow the [official Special Summon explanation](https://www.yugioh-card.com/en/play/psct/psct-5/)
and [era rulebook](https://img.yugioh-card.com/ygo_cms/ygo/all/uploads/Rulebook_v9_en.pdf).
They are rules references, not proof that each PSP branch has been reproduced.

Own-source/own-field, single-monster, face-up placement is the supported slice.
Opponent-source summons, simultaneous groups, material/cost procedures, protection,
general Special Summon restrictions, and Ritual/Fusion/Synchro/Xyz/Pendulum proper
methods need separate profiles and tests. They must not be simulated by overloading
the main deck or by treating a card-type code as summon eligibility.
