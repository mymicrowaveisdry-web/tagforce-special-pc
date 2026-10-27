# Shared duel model

Provides immutable two-player duel state and a deterministic command engine.
It handles opening hands, draws, phases, eligible Level 1–4 normal summons,
monster and Spell/Trap sets, position changes, effect-free attacks, battle damage,
destruction, LP defeat, hand-limit discards and deck-out. Card instances reference
the normalized catalog;
explicit gameplay metadata is required rather than guessing binary kind codes.

Battle eligibility and ordinary Spell/Trap placement require explicit opt-in metadata.
There is no renderer, emulator, randomness, response window or general effect engine.
Snapshots expose complete engine information; presentation needs a visibility view.
See [development instructions](../docs/DEVELOPMENT.md) for battle scope, API and validation.

## Pending decisions

The optional `decisionSequences` constructor argument registers trusted immutable
selection policies. No sequences are enabled by default. `DecisionStep.Card`
selects instances from the acting player's hand or deck using explicit catalog
IDs; `DecisionStep.Position` supplies allowed positions; `EmptyMonsterZone`
offers only unoccupied monster slots. Definitions are copied and validated.
Sequences contain 1–32 steps and can start only in the active player's Main1.

The command/answer flow is:

```text
BeginDecisionSequence(player, configuredSequenceId)
ChooseDecisionCard(player, currentDecisionId, cardInstanceId)
ChooseDecisionPosition(player, currentDecisionId, position)
ChooseDecisionZone(player, currentDecisionId, monsterZone)
```

Read the current `State.PendingDecision` after each accepted answer; its kind,
options, owner and ID define the next legal answer. Not every definition needs
all three kinds. Decision IDs use the revision of the accepted request state.
`State.Revision` advances once per accepted command and never for rejections.
No random IDs or mutable out-of-state decision counters are used.

Before opening a sequence, the engine preflights every step for available
choices. While pending, unrelated commands reject with `DecisionPending`.
Wrong player/ID/kind/option and out-of-order/repeated answers reject with the
same state object, no revision change and no events. Pending request/options,
earlier answers, events and completed results are immutable snapshots.

Accepted answers emit `DecisionAnswered` followed by either `DecisionRequested`
or `DecisionSequenceCompleted`. Completion clears the request and publishes
`State.DecisionResult`. Choosing cards/zones does not move or summon anything,
consume a summon, activate a Spell, or execute an effect.

`DecisionSequence` defaults to `stopAfterCompletion: true`: completion sets
`ResearchSliceComplete`, further gameplay rejects, and `Outcome` remains
unchanged. Construct a new duel to restart that research slice. A trusted
selection-only workflow can explicitly use `false` to resume ordinary commands.
That option must not be used to bypass unsupported card lifecycle behavior.

Both pending card options and completed answers expose full engine information.
A future UI must provide an actor-specific visibility view. These workflows have
synthetic managed tests; they do not establish PSP activation equivalence.

## Experimental Spell activation/resolution

The separate `experimentalSpellEffects` constructor argument explicitly registers
immutable `DeckSpecialSummonSpell` profiles supplied by Effects. It is empty by
default and independent of normal-summon, battle and back-row-setting flags.
Profiles require compatible explicit Spell/Monster metadata; ordinary sequence
commands cannot start a profile's reserved activation decisions.

`ActivateSpell` commits the hand card to a face-up back-row slot and creates a
card decision. Subsequent choices use the same typed decision API. On the final
zone answer, Core revalidates the bound card instances and destination, removes
the selected deck instance, Special Summons it in attack position and appends the
Spell to the graveyard atomically. Normal Summon allowance and unrelated state
remain unchanged. The activation/resolution profile sets the research boundary
after cleanup. The separate lifecycle profile permits phase progression and
explicitly opted-in basic battle. In End Phase it banishes the bound summoned
instance if still on the field, or expires the obligation if battle destroyed it
and it remains in the graveyard, then stops. Other card actions reject atomically.

`SpellTrapFields` is the authoritative immutable view of back-row cards and
face-up state. `SpellTrapZones` remains a compatible card-only projection.
`SetSpellTrap` still places face-down cards. `PendingSpellActivation` binds the
committed Spell and effect while decisions are pending and clears on resolution.

See [Effects](../TagForceSpecial.Effects/README.md) for Emergency Teleport's exact
research scope and PSP comparison limitations.

`DuelPlayerState.Banished` and `PendingEndPhaseBanishments` are copied read-only
collections. The lifecycle profile records source Spell, summoned instance,
zone and due turn; it removes only that field instance and appends it to its
owner's banished zone. Graveyard, hand copies, earlier field copies and Normal
Summon allowance remain unchanged. This is a bounded experiment, with no general
delayed-effect scheduler, shuffle, movement/reset exceptions or next-turn continuation.
