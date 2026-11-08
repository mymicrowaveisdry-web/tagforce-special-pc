# Shared duel model

Immutable two-player state and deterministic commands for draws, turns, eligible Normal/Tribute Summons and sets, positions, basic combat, discards and defeat; [summon profiles](SummonProfiles.md), [Special Summons](SpecialSummons.md), [Extra Deck materials, Synchro and Xyz](ExtraDeck.md), [Fusion/Ritual effects](EffectSummons.md) and [Pendulum](Pendulum.md) use explicit eligibility; Tokens cease to exist when they leave the field.

Gameplay requires explicit metadata; snapshots expose full information. Rendering and visibility filtering remain outside Core; effect shuffles use a seeded generator in the snapshot. Card-specific gameplay uses explicit opt-ins.
`Decks` adds immutable versioned deck/collection models and explicit legality policies; unknown original-era restrictions remain indeterminate.

See the [quick start](../README.md#quick-start).

## Pending decisions

`decisionSequences` registers selection-only workflows: `BeginDecisionSequence`, then `ChooseDecisionCard`, `ChooseDecisionPosition` or `ChooseDecisionZone`.

Answer `State.PendingDecision` using its owner, ID and options. Accepted commands advance the revision; rejections preserve state and events.

Selections do not move cards. Completion stops research by default (the only remaining research stop).

## Card effect definitions

`effectDefinitions` registers `EffectDefinition`s: kind (Normal/Quick-Play Spell, Normal/Counter Trap), conditions, a discard or LP cost, Spell/Trap and field targets, a summon lock and ordered steps (LP gain or damage for either or both players, optionally counted from public zones; both players at 0 LP is a draw; draw, Deck search or Deck-to-Graveyard by a reviewed filter (`ChooseDefinitionCard`), mill; destroy, return to hand or change to Defense by field target (`ChooseDecisionFieldTarget`), "all matching" or highest/lowest stat; revive, recover, banish or shuffle in Graveyard/banished targets ("up to" picks end with `FinishDecision`); shuffle, Tokens, target/face-up destruction, activation negation, hand/Deck Special Summon with End Phase banishment). One legality check covers hand, set, open-window and chain-response activation (including the turn player's hand Quick-Play via `ActivateHandChainResponse`).

Costs and targets are answered as decisions before the link is added; targets bind controller, slot, instance and placement, and paid costs remain paid. A resolution-time summon choice is answered with `ChooseDefinitionSummon`.

Two consecutive passes close a window; `ResolveNextChainLink` resolves in reverse order. Missing bound targets skip destruction; cleanup handles surviving sources once. Definitions answer staged battle windows outside the Damage Step (an attack-declaration condition binds the attacker; the turn player may use a hand Quick-Play Spell via `ActivateHandBattleResponse`). After a successful Summon, a summon-response open window goes to the turn player, then the other player, when they have a set card whose summon condition is met; that Summon stays answerable by later links until play reopens. A Normal, Tribute or Special Summon attempt also pauses ("would be Summoned") when the responder has a set Counter Trap definition that can negate it; the action adapter drives that window.

Negation responses are definitions (`RespondsToActivationCondition`, `NegateActivationStep`/`NegateEffectStep`, optional filtered discard) and answer open, trigger, battle and summon chains. See [Effects](../TagForceSpecial.Effects/README.md) for supported cards.

## Event-driven triggers and continuous stats

`experimentalTriggerEffects`, `experimentalContinuousEffects` and `experimentalContinuousNegations` register explicit monster profiles; all default off.

- Triggers cover supported summons, flips, destruction and Graveyard movement.
- `ChooseTriggerOrder` orders mandatory/optional groups, turn player first within each kind.
- Costs/targets commit before responses; committed use limits survive negation.
- Optional `When` timing checks the last gameplay action group; resolution steps declare dependencies.
- Auras derive effective ATK/DEF from live placements. Temporary modifiers expire at declared boundaries.
- Placement-bound or continuous negation suppresses eligible effects; resolved modifiers remain.

Follow-up triggers wait for chain completion. Basic combat uses effective stats; these effects alone add no battle windows. Full stat layers and card-specific exceptions remain unsupported.

## Interruptible battle flow

`experimentalBattleFlow` enables Start, Battle, Damage and End steps with five damage timings.

Use `PassBattleResponse`, `AdvanceBattle` and `ChooseAttackReplay` with current decision IDs.

A `TriggerEffectProfile` can resolve through a `MonsterTrigger` definition and filter its event by `TriggerEventCause`; the action adapter drives trigger order, decline, costs and targets. `GeminiSummon` gives a face-up Gemini monster its effects and can be negated; `NormalMonsterFilter` also matches Gemini monsters in the Graveyard; `DuelCardRules.CannotBeSpecialSummoned` blocks every Special Summon. Definitions cover attack negation, attacker destruction, hand summons (chosen at resolution) and combat-stat changes that last until the attack ends.

`BattleTimingCondition` limits their windows and is required in the Damage Step; during calculation needs an explicit exception.

Damage applies before Graveyard cleanup; eligible Flip triggers wait until after calculation.

Battle-destroyed monsters stop contributing auras before cleanup. Lethal damage ends the duel first.

The atomic compatibility path remains the default. Hand responses, redirection, multiple attacks and complete Damage Step legality remain unsupported.

## Evidence

Synthetic tests validate these bounded contracts. Retained PSP projections cover selected activation, chain and battle outcomes; additional timing comparisons remain deferred.

No experimental profile implies full card support or complete original-game equivalence.
