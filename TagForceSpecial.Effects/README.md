# Card effects

[`PlayablePreview`](PlayablePreview.cs) defines the tested 14-card [preview pool](Preview.md).
[`NormalMonsterFamily`](NormalMonsterFamily.cs) supports all 665 reviewed Normal Monsters: Level 1–4 Normal Summon/Set, Level 5–8 Tribute Summon/Set, being Tributed, flip and basic battle; combine with other profiles through `RulesWith`. Tokens are excluded from release manifests.
[`ChainCardFamily`](ChainCardFamily.cs) supports Mystical Space Typhoon, Magic Jammer, Lightning Vortex, Scapegoat and Emergency Teleport as `EffectDefinition`s on the shared Core pipeline, with their rules, Sheep Tokens and a reviewed 24-card Level 1–3 Psychic list.
[`LifePointFamily`](LifePointFamily.cs) supports 13 LP Normal Spells/Traps: fixed amounts (four corroborated by the signed LP table), both-player effects (a simultaneous double knockout is a draw) and amounts counted at resolution.
[`DeckOperationFamily`](DeckOperationFamily.cs) supports 10 draw, Deck search, Deck-to-Graveyard and mill cards; search filters are reviewed ID lists (e.g. every icon-2 Field Spell).
[`ReviewedCardMetadata`](ReviewedCardMetadata.cs) overlays reviewed metadata when a catalog is opened; converted packages keep the frozen Cards baseline, so review batches never change pinned cache bytes or artwork bindings.

```csharp
var engine = new DuelEngine(catalog, rules, playerOneDeck, playerTwoDeck,
    effectDefinitions: ChainCardFamily.Definitions, shuffleSeed: seed);
```

Emergency Teleport shuffles after a Deck summon, so its definition needs a seed. Card definitions do not yet answer Battle Phase or summon windows.

Trigger/aura primitives, staged battle and [bounded Fusion/Ritual effects](../TagForceSpecial.Core/EffectSummons.md) use separate [Core opt-ins](../TagForceSpecial.Core/README.md).
Existing card factories do not automatically gain battle or Damage Step eligibility.
Selected outcomes have optional local PSP fixtures; new timing comparisons remain deferred.
CI runs synthetic tests without those fixtures.
