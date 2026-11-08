# Bounded original-era Pendulum foundation

Authored metadata enables two dedicated Pendulum Zones, separate from the five
back-row slots, with side-specific printed scales. Face-up Extra Deck cards have
separate immutable storage from the existing face-down Extra Deck. This models
the original-era layout without Link/Extra Monster Zone restrictions.

Register `PendulumCardProfile` entries through `experimentalPendulumCards` for
Pendulum identities and left/right scales. Independently set
`DuelCardRules.AllowsPendulumSummon` for every permitted summoned monster,
including ordinary hand monsters. These opt-ins grant no card-specific effects.

## Scales and simultaneous summons

A scale activates from hand as a Spell and enters a response chain. Destruction
without activation negation uses the field departure rule; negating activation
sends the unsuccessful card to the Graveyard. Effect negation alone does not
remove a scale. Card-specific scale effects and activation restrictions require
later profiles; printed scales alone do not implement a real card.

Use `ActivatePendulumScale` with dedicated zone 0 or 1. Existing Spell/Trap target
decisions expose `IsPendulumZone`; preserve it when answering with
`ChooseDecisionTarget`. Scales count as Spells in these chains while their
catalog identity remains Monster.

During an open Main Phase, one revision-checked command selects one to five
distinct eligible own-hand or face-up Extra Deck cards, distinct empty monster
zones and face-up positions. Both scales must be present, and every printed
level must lie strictly between their values. Ordinary explicitly eligible
non-Pendulum monsters may be selected from hand. Face-down Extra Deck cards and
registered Xyz monsters are ineligible; proper-summon restrictions still apply.

Validation precedes every removal and placement. Invalid selections preserve the
identical snapshot and emit no events. Successful placements are simultaneous
for trigger timing and do not spend the Normal Summon allowance. The Pendulum
attempt is once per player turn, including an attempt whose summon is negated.
The group response window holds the whole selection outside the field;
batch-capable negation rejects it together, otherwise all placements complete.

Submit `PendulumSummon` with the current revision and `PendulumSummonSelection`
entries (instance, `Hand` or `FaceUpExtraDeck` source, destination, position).
Answer `PendingSummon` through the usual pass/negation commands. Multi-monster
negators need `MonsterWouldBeSummonedCondition.Group`; others cannot answer a group. Successful placements use one timing group.

## Departures

| Source and operation | Destination |
| --- | --- |
| Registered Pendulum on the field, sent toward Graveyard by battle, destruction, Tribute or summon material use | Owner's face-up Extra Deck |
| Active Pendulum Zone card destroyed | Owner's face-up Extra Deck |
| Hand/deck card discarded or used as material | Owner's Graveyard |
| Card attached as Xyz material, then detached or released | Owner's Graveyard |
| Scale activation or summon negated before successful field entry | Owner's Graveyard |
| Field card banished | Existing banished destination |

Redirected field departures retain destruction/Tribute/material causes but do
not create a Sent-to-Graveyard occurrence. Becoming Xyz material is attachment,
so it does not redirect to the Extra Deck. Face-up Extra Deck growth does not
consume the initial face-down Extra Deck's 15-card limit. All routing preserves
card ownership, instance identity and immutable prior snapshots.

## Scope and evidence

The ordinary rule basis is the [official v9.0 rulebook](https://www.yugioh-card.com/eu/wp-content/uploads/2022/07/Rulebook_v9_en.pdf),
pages 5 and 12–13. PSP callback/code comparisons remain separate evidence; these
tests do not establish original-game parity. General scale changes, extra
Pendulum attempts, card-specific restrictions, replacement/prevention effects,
Pendulum hybrid procedures and complete card effects remain outside this slice.
