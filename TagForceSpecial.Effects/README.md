# Card effects

Contains bounded Emergency Teleport profiles for **activation/resolution** and
**direct End Phase banishment**, using the observed deck Krebons / attack-position
branch. Costs, targets, chains and event effects have separate bounded opt-ins;
broader card support remains future work. See the [implemented scope](../README.md#implemented-scope).

Enable only through explicit research configuration:

```csharp
var engine = new DuelEngine(catalog, rules, playerOneDeck, playerTwoDeck,
    experimentalSpellEffects: [EmergencyTeleport.ActivationResolutionOnly()]);
```

From the active player's Main1, execute `ActivateSpell(player, spellInstanceId,
spellTrapZone)`, then answer the current card, position and zone decisions.
The Spell becomes face-up on the field, the selected deck instance is Special
Summoned, and the same Spell instance enters its owner's graveyard. Published
snapshots and existing face-down Set behavior are preserved.

The profile is disabled by default, supports only deck Krebons and face-up attack,
and stops gameplay after resolution. It does not implement hand summons,
shuffle, responses/chains or cancellation after commitment.
Do not expose it as a fully supported card in normal PC gameplay.

The optional local `config/reference/emergency-teleport.json` fixture is exported
from validated PSP captures; C# compares two before/placed/resolved observations. Actor, turn,
phase, acting-player card IDs/counts and selected slots agree. Absolute LP after
an earlier native effect, opponent contents, deck order and response timing are
excluded; LP deltas and unchanged opponent state are checked separately.

For the separately verified lifecycle experiment, register
`EmergencyTeleport.EndPhaseLifecycleOnly()` instead. After resolution, phase
progression and explicitly opted-in basic battles are accepted. Entering End
Phase banishes the surviving summoned instance, or expires the obligation if
battle already put it in the graveyard, then stops the research slice. It
preserves other copies. Other card actions fail atomically.

The optional local `emergency-teleport-lifecycle.json` fixture contains two independently validated original
before/after projections. `emergency-teleport-destroyed.json` verifies that a
summoned Krebons destroyed by battle stays in the graveyard through End Phase.
Other movement, face-down resets, control changes,
leave/re-enter cases, multiple obligations and effect
exceptions are unsupported. These profiles remain disabled in normal PC play;
neither claims full Emergency Teleport support. Reference JSON fixtures are not
included in this branch; CI excludes the tests that require them and runs the
synthetic lifecycle tests.

## Cost and target commitment research

Register the separate, default-off `experimentalActivationContracts` argument:

```csharp
experimentalActivationContracts: [
    ActivationResearch.LightningVortex(allowedDiscardIds),
    ActivationResearch.MysticalSpaceTyphoon()
]
```

These profiles only commit an activation from hand during Main1. Vortex requests
one eligible discard; that exact instance enters the graveyard before the
committed receipt is published. The discard is a cost, not a monster target.
Typhoon uses `ChooseDecisionTarget` to bind a Spell/Trap card instance, controller,
slot and uninterrupted placement token. The source cannot target itself.
Field Zone targeting is outside the current back-row-only profile.

Both profiles freeze before responses or resolution, leaving the source on the
field. They do not implement Vortex destruction, Typhoon destruction, Scapegoat,
chains, negation or refunds. They are not fully supported cards.

Local PSP references repeat payment before the response prompt and selection of
Scapegoat as a target. In the original, Scapegoat turns face-up, resolves first
and creates four tokens; Typhoon destroys it afterward and the tokens survive.
The separate `ChainResearch` profiles now consume those receipts for a bounded
Vortex or Typhoon/Scapegoat chain, then freeze. Register them through Core's
`experimentalChainEffects` together with the matching activation contracts.
Scapegoat requires explicit Monster rules for token IDs 3902-3905 and a previously
set, targeted source with four empty slots. Its token allocation is a deterministic
PC policy; later token play and full Scapegoat support are outside this slice.
The activation-only profiles still freeze unless separate chain profiles are enabled.

`ChainResearch.MysticalSpaceTyphoonResponse()` and
`ChainResearch.MagicJammer(allowedDiscardIds)` register through the separate
`experimentalChainResponses` argument. They require explicit ordinary set rules
for their source IDs; Magic Jammer (4862) is a Trap. Typhoon requires matching
initial chain/activation profiles when those are also registered. Its set response
requests a back-row target. Magic Jammer responds to a Spell, pays one eligible
owned discard and commits a speed-3 link that negates the preceding activation
and destroys its still-present source placement. It does not refund either cost.

These are native-static-code-backed research branches with synthetic managed
interaction tests. They do not establish all card restrictions, protection,
activation windows, refund exceptions or fresh PSP outcome equivalence. The Core
effect-only negation primitive is tested with invented profiles; The Dragon's
Bead and other real effect-only cards are not made playable by that primitive.
Typhoon now continues without destruction if its bound target is absent/replaced,
using the inspected native validator path. Re-entry and broader movement cases
still need original gameplay replays. The slice freezes after chain cleanup.

## Reusable event effects

Core's separate `experimentalTriggerEffects` and `experimentalContinuousEffects`
register rules-informed monster trigger and aura primitives. They are independent
of the hard-coded original-card research profiles above. The synthetic test
profiles exercise simultaneous ordering, follow-up chains, cost/target commitments,
once-per-turn use limits, optional-when timing, negation, LP gains, placement-bound
temporary deltas and aura removal. No real trigger/continuous
card profile is declared fully supported. See the [Core contract](../TagForceSpecial.Core/README.md#stage-7-event-driven-triggers-and-continuous-stats)
for supported events, durations, response integration and remaining timing rules.
