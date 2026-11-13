# Card effects

[`PlayablePreview`](PlayablePreview.cs) defines the tested 14-card [preview pool](Preview.md).
[`NormalMonsterFamily`](NormalMonsterFamily.cs) supports all 665 reviewed Normal Monsters: Level 1–4 Normal Summon/Set, Level 5–8 Tribute Summon/Set, being Tributed, flip and basic battle; combine with other profiles through `RulesWith`. Tokens are excluded from release manifests.
[`ChainCardFamily`](ChainCardFamily.cs) supports Mystical Space Typhoon, Magic Jammer, Lightning Vortex, Scapegoat and Emergency Teleport as `EffectDefinition`s on the shared Core pipeline, with their rules, Sheep Tokens and a reviewed 24-card Level 1–3 Psychic list.
[`LifePointFamily`](LifePointFamily.cs) supports 13 LP Normal Spells/Traps: fixed amounts (four corroborated by the signed LP table), both-player effects (a simultaneous double knockout is a draw) and amounts counted at resolution.
[`DeckOperationFamily`](DeckOperationFamily.cs) supports 10 draw, Deck search, Deck-to-Graveyard and mill cards; search filters are reviewed ID lists (e.g. every icon-2 Field Spell).
[`RemovalFamily`](RemovalFamily.cs) supports 15 destroy, return and position-change cards by field target, "all matching" or highest/lowest stat.
[`GraveyardFamily`](GraveyardFamily.cs) supports 8 Graveyard and banished-zone cards, including Monster Reborn from either Graveyard and "up to" targets.
[`BattleTrapFamily`](BattleTrapFamily.cs) supports 9 attack-declaration Traps, including Mirror Force, Magic Cylinder and Dimensional Prison.
[`SummonTrapFamily`](SummonTrapFamily.cs) supports 5 summon-response Traps: Trap Hole, Eatgaboon, House of Adhesive Tape, Bottomless Trap Hole and Torrential Tribute.
[`SummonNegationFamily`](SummonNegationFamily.cs) supports Horn of Heaven, Black Horn of Heaven and Solemn Judgment, negating Summon attempts (Solemn Judgment also Spell/Trap activations).
[`MonsterTriggerFamily`](MonsterTriggerFamily.cs) supports 13 monsters that trigger on leaving the field, such as Sangan and Mystic Tomato; register `Triggers` as trigger effects and supply a seed. `TfspSummonRestrictions` lists monsters an effect cannot Special Summon from the Deck.
[`MonsterSubtypeFamily`](MonsterSubtypeFamily.cs) supports 12 Flip, Spirit and Gemini monsters; register `Triggers` and `CardRules`. A duel with flip or battle-window definitions runs the staged battle flow.

[`LastingCardFamily`](LastingCardFamily.cs) supports 26 representative Field, Equip, revival, negation, Toon, Union, activated and counter cards; [`SummonProcedureFamily`](SummonProcedureFamily.cs) supports 24 hand-procedure, Fusion, Ritual, Synchro, Xyz and Pendulum cards; register `Definitions`, `Triggers`, `CardRules` and `SpecialSummonCards`.
[`CardFamilies`](CardFamilies.cs) lists every family, feeding the metadata overlay, the manifest and `CardPool`, which loads all supported cards into one engine. [`CardTable`](CardAuthoring.cs) builds a family from short card entries and generates its manifest text; [`TemplateCardFamily`](TemplateCardFamily.cs) uses it for 189 battle-ability monsters, restricted Equips, "cannot" rule locks, by-name limits, stat Field Spells, Type wipes, searches, recovery, trigger, "choose 1", face-up and hand or Graveyard activated-effect, dice, coin, revealed-hand, excavation, declaration, control-change, Pendulum Effect, phase-skip, battle-ending, Type, Attribute and scale change, control-change trigger, hand trigger, Summon response, damage response, Charmer, per-target, declared-name trigger, Qliphort, Level-setting and Pendulum Scale cards; [`EgyptianGodFamily`](EgyptianGodFamily.cs) holds the Egyptian Gods and [`ApoqliphortFamily`](ApoqliphortFamily.cs) the Apoqliphorts, kept apart because their Summon needs a registered Tribute procedure, [`TokenCardFamily`](TokenCardFamily.cs) holds cards whose Tokens carry their own rules, and [`ShortTextCardFamily`](ShortTextCardFamily.cs) holds 125 short-text equips, searchers, recruiters, simple triggers, one-step Spells and Traps and archetype searchers and recruiters, in behaviour and archetype modules. Reviewed [archetype content](Archetypes/Schema.md) supplies semantic filters with independent kind/Level/Rank restrictions.
[`ReviewedCardMetadata`](ReviewedCardMetadata.cs) overlays reviewed metadata when a catalog is opened; converted packages keep the frozen Cards baseline, so review batches never change pinned cache bytes or artwork bindings. [Archetype contracts](Archetypes/Schema.md) validate separate content documents and compile immutable membership snapshots with both lookup directions; archetype consumers use pinned effective-identity resolution.

```csharp
var engine = new DuelEngine(catalog, rules, playerOneDeck, playerTwoDeck,
    effectDefinitions: ChainCardFamily.Definitions, shuffleSeed: seed);
```

Emergency Teleport shuffles after a Deck summon, so its definition needs a seed. Card definitions answer staged battle windows (not the Damage Step), successful Summons and Normal/Tribute/Special Summon attempts; Flip and Pendulum Summons cannot be negated yet.

Trigger/aura primitives, staged battle and [bounded Fusion/Ritual effects](../TagForceSpecial.Core/EffectSummons.md) use separate [Core opt-ins](../TagForceSpecial.Core/README.md).
Existing card factories do not automatically gain battle or Damage Step eligibility.
Selected outcomes have optional local PSP fixtures; new timing comparisons remain deferred.
CI runs synthetic tests without those fixtures.
