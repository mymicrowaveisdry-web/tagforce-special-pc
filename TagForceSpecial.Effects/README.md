# Card effects

Contains bounded Emergency Teleport profiles for **activation/resolution** and
**direct End Phase banishment**, using the observed deck Krebons / attack-position
branch. Costs, targets, chains, triggers and the broader effect system remain
future work; see the [implemented scope](../README.md#implemented-scope).

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
