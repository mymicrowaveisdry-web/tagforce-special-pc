# Shared duel model

Immutable two-player state and deterministic commands for draws, turns, eligible Level 1–4 summons, sets, positions, basic combat, discards and defeat.

Gameplay requires explicit metadata; snapshots expose full information. Rendering, visibility filtering, randomness and general card legality remain outside Core.

See the [quick start](../README.md#quick-start).

## Pending decisions

`decisionSequences` registers selection-only workflows: `BeginDecisionSequence`, then `ChooseDecisionCard`, `ChooseDecisionPosition` or `ChooseDecisionZone`.

Answer `State.PendingDecision` using its owner, ID and options. Accepted commands advance the revision; rejections preserve state and events.

Selections do not move cards. Completion stops research by default.

## Experimental Spell activation and resolution

`experimentalSpellEffects` enables bounded deck Special Summons through `ActivateSpell` and decision answers.

Resolution revalidates instances/zones and cleans up the Spell. The optional End Phase lifecycle banishes the surviving bound instance, then stops.

See [Effects](../TagForceSpecial.Effects/README.md) for supported profiles.

## Activation costs and targets

`experimentalActivationContracts` commits eligible discard costs and back-row targets, then freezes unless matching chain profiles are enabled.

Targets bind controller, slot, instance and placement; paid costs remain paid.

## Bounded chain resolution

`experimentalChainEffects` and `experimentalChainResponses` enable scoped chains and set responses.

Two consecutive passes close a window; `ResolveNextChainLink` resolves in reverse order.

Operations include destruction, scoped token creation and activation/effect negation. Missing bound targets skip destruction; cleanup handles surviving sources once.

Responses require declared speed and earlier-turn sets. Broader timing/card legality is unsupported.

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

`experimentalBattleResponses` enables earlier-turn set profiles for attack negation, attacker destruction, allowed hand summons and temporary combat-stat changes.

Window metadata controls eligibility; Calculation changes require an explicit exception.

Damage applies before Graveyard cleanup; eligible Flip triggers wait until after calculation.

Battle-destroyed monsters stop contributing auras before cleanup. Lethal damage ends the duel first.

The atomic compatibility path remains the default. Hand responses, redirection, multiple attacks and complete Damage Step legality remain unsupported.

## Evidence

Synthetic tests validate these bounded contracts. Retained PSP projections cover selected activation, chain and battle outcomes; additional timing comparisons remain deferred.

No experimental profile implies full card support or complete original-game equivalence.
