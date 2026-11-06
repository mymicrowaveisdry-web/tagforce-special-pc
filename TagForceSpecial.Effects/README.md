# Card effects

[`PlayablePreview`](PlayablePreview.cs) defines the tested 14-card [preview pool](Preview.md); the default-off research profiles below remain partial.
[`NormalMonsterFamily`](NormalMonsterFamily.cs) supports all 665 reviewed Normal Monsters: Level 1–4 Normal Summon/Set, Level 5–8 Tribute Summon/Set, being Tributed, flip and basic battle; combine with other profiles through `RulesWith`. Tokens are excluded from release manifests.
[`ReviewedCardMetadata`](ReviewedCardMetadata.cs) overlays reviewed metadata when a catalog is opened; converted packages keep the frozen Cards baseline, so review batches never change pinned cache bytes or artwork bindings.

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

Trigger/aura primitives, staged battle and [bounded Fusion/Ritual effects](../TagForceSpecial.Core/EffectSummons.md) use separate [Core opt-ins](../TagForceSpecial.Core/README.md).
Existing card factories do not automatically gain battle or Damage Step eligibility.
Selected outcomes have optional local PSP fixtures; new timing comparisons remain deferred.
CI runs synthetic tests without those fixtures.
