# Shared duel model

Provides immutable two-player duel state and a deterministic command engine.
It handles opening hands, draws, phases, eligible Level 1–4 normal summons,
monster and Spell/Trap sets, position changes, effect-free attacks, battle damage,
destruction, LP defeat, hand-limit discards and deck-out. Card instances reference
the normalized catalog;
explicit gameplay metadata is required rather than guessing binary kind codes.

Battle eligibility and ordinary Spell/Trap placement require explicit opt-in metadata.
There is no renderer, emulator, randomness or general effect engine.
Snapshots expose complete engine information; presentation needs a visibility view.
See the [root quick start](../README.md#quick-start) for build and validation commands.

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

## Activation costs and targets: commitment only

`experimentalActivationContracts` registers immutable, default-off
`ActivationContract` profiles. These are distinct from executable Spell effects:
they stop at an `ActivationCommitted` receipt, before resolving effects or
responses. Cost and target availability are checked before moving the source
Spell from hand to a face-up slot.

`ChooseDecisionCard` pays an eligible discard immediately, appending the selected
owned hand instance to graveyard and emitting `ActivationCostPaid`. The source
cannot pay for itself. If a target choice follows, payment remains committed;
invalid answers neither refund nor charge it again. Combined cost/target order
has synthetic tests, not a newly verified native costed-targeting card.

`ChooseDecisionTarget` selects an immutable `PendingDecision.TargetOptions` entry.
The binding records player, slot, card instance and `FieldSpellTrap.PlacementId`.
Engine placements receive deterministic revision-based tokens. Face-up changes
must preserve the token; leaving/re-entering must allocate a new one.
`SpellTrapTarget.IsStillPresent` checks the field at the bound player/slot:
it accepts a face-up change and rejects absence, replacement and a new placement
of the same card. It does not resolve, fizzle or automatically retarget an effect.
The target policy covers the five back-row slots; Field Zone targeting is outside
this research profile.

The committed receipt retains source Spell, paid discard and optional target.
Research then freezes with the source still on the field. No destruction,
negation, refund, cleanup or chain resolution is silently performed. Costs and
targets differ from resolution choices such as Emergency Teleport's deck choice.
Snapshots remain full-information: opponent face-down target identities require
an opaque presentation view before UI integration.

## Bounded chain research

`experimentalChainEffects` adds a separate, default-off opt-in. A matching
`ChainEffectProfile` consumes the committed receipt and opens `State.PendingChain`
instead of freezing. Existing activation-only callers keep their previous behavior.
The supported operations are opposing face-up monster destruction, bound back-row
destruction, and the targeted, previously set four-token response. Registration
validates matching commitment kinds and explicit Spell/Monster metadata.

`PassChainResponse(player, decisionId)` alternates responders. Two consecutive
passes close the response window. `ActivateChainResponse(player, decisionId, zone)`
preserves the source placement and resets passes after commitment. The opponent's
targeted Scapegoat response retains its original two-link restriction.

The separate `experimentalChainResponses` argument registers immutable
`ChainResponseProfile` metadata for previously set responses. It is empty by
default. Supported operations are bound back-row destruction, activation negation
and effect-only negation. Profiles declare response speed, eligible discard IDs,
optional preceding source category and whether negation destroys that source.
Only speed 2/3 responses are accepted; a speed 2 response cannot follow speed 3.
Sources must be face-down and set on an earlier turn. Hand responses, general
timing windows, protection effects and full card legality remain unsupported.

Costs and target choices use the existing decision commands, owned by the
responder rather than necessarily the active turn player. Invalid answers reject
atomically; a paid discard remains paid while a target choice waits. Each receipt
is stored in its chain link; `State.CommittedActivation` retains the initial
activation receipt. A negation response binds the immediately preceding link.

`ResolveNextChainLink(activePlayer, decisionId)` advances one complete link in
reverse order, with no responses during resolution. Both players' commands go
through the same `Execute` entry point, independently of the active turn player
while a response is pending. Wrong player, stale ID, unsupported response and
unrelated commands reject with the same state object and no events. Snapshots,
links, source placements and paid costs remain immutable and deterministic.

The four tokens resolve before Typhoon destroys the bound Scapegoat. Its face-up
change preserves target identity; its later destruction does not remove tokens.
Source cleanup follows the last link and does not duplicate a destroyed source.
Completion freezes the research slice without a duel result or later token play.
Typhoon's missing/replaced target skips destruction, emits `ChainLinkSkipped`
and continues through cleanup without retargeting. Negated links also emit a skip
reason before completing. `ChainLink.Negation` distinguishes effect-only and
activation negation; both suppress the effect, while only the latter sets
`ActivationNegated`. Source destruction is a separate profile policy and checks
the bound source placement. Paid costs stay in graveyard, even if a response is
itself negated. Source cleanup appends each surviving source once and emits
`ChainTrapSentToGraveyard` for Traps. At most 16 links may be committed.

The alternating-pass protocol, revision IDs, explicit resolution stepping, token
instance/zone allocation and event ordering are PC design choices. Local reference
tests compare retained repeated paid/committed, four-token and final state
projections. Native controller analysis corroborates other-player-first offers,
reverse resolution, distinct negation flags and target identity revalidation.
New synthetic interaction tests exercise the interpreted contract, including
negating a negation and destroying a lower link's source without negating it.
Effect-only negation is an engine primitive tested with invented profiles; no
real effect-only card's complete eligibility is advertised. Fresh native
rejection, interruption and negation replays remain required before the roadmap's
stage 6 completion gate can pass.
