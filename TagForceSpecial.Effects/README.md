# Card effects

Default-off research profiles; none provides full card support.

| Profiles | Scope |
| --- | --- |
| `EmergencyTeleport.ActivationResolutionOnly()` | Deck Krebons, attack position; stop after resolution |
| `EmergencyTeleport.EndPhaseLifecycleOnly()` | Banish the surviving summoned instance in End Phase, then stop |
| `ActivationResearch.LightningVortex(allowedDiscardIds)` | Commit one eligible discard |
| `ActivationResearch.MysticalSpaceTyphoon()` | Bind a back-row target by instance and placement |
| `ChainResearch` | Bounded Vortex, Typhoon/Scapegoat, set Typhoon and Magic Jammer chains |

```csharp
var engine = new DuelEngine(catalog, rules, playerOneDeck, playerTwoDeck,
    experimentalSpellEffects: [EmergencyTeleport.ActivationResolutionOnly()]);
```

Activate from Main1 and answer current decisions. Activation-only contracts stop before
responses/resolution unless matching chain profiles are registered.
Explicit source, discard and token rules are required; committed costs are not refunded.
Shuffle, broader movement and general card legality remain unsupported.

Trigger/aura primitives and staged battle use separate [Core opt-ins](../TagForceSpecial.Core/README.md).
Existing card factories do not automatically gain battle or Damage Step eligibility.
Selected outcomes have optional local PSP fixtures; new timing comparisons remain deferred.
CI runs synthetic tests without those fixtures.
