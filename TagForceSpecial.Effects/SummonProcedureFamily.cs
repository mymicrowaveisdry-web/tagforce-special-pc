using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Representative hand procedures, Fusion, Ritual, Synchro, Xyz and Pendulum cards.</summary>
public static class SummonProcedureFamily
{
    public const string FamilyId = "summon-procedure-definition";
    public const string BatchId = "tfsp-family-batch-10c-08";
    public const string FusionProcedureId = "fusion-summon";
    public const string RitualProcedureId = "ritual-summon";
    public const string SynchroProcedureId = "synchro-summon";

    private const uint EffectMonsterKind = 1, FusionKind = 2, RitualKind = 4, TunerKind = 16, SynchroKind = 18, NormalXyzKind = 22, XyzKind = 23,
        NormalPendulumKind = 25, SpecialSummonOnlyKind = 27, SpellKind = 13;
    private const uint IconRitual = 6, Light = 1, Dark = 2, Water = 3, Fire = 4, Earth = 5, Wind = 6, Warrior = 15;
    private const uint FlashKnight = 11210, FlashKnightScale = 7;
    private const SpecialSummonSources Revivable = SpecialSummonSources.Graveyard | SpecialSummonSources.Banished;
    private const string NoChain = "A built-in Summon: it uses no chain and only Summon negation can answer it.";
    private const string SpellCleanup = "Activation or effect negated: nothing is Summoned and no material is used.";
    private const string MonsterCleanup = "Negation skips the link; the cost stays paid.";

    private sealed record Entry(DefinitionCardProfile Card, string[] References, uint GetterKind, uint Icon, DuelCardRules Rules,
        EffectDefinition[] Definitions, TriggerEffectProfile[] Triggers);

    private static readonly DuelCardRules MainDeckMonster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true,
        AllowsTributeSummon: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true);
    private static readonly DuelCardRules HandOnlyMonster = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true);
    private static readonly DuelCardRules ExtraDeckMonster = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsExtraDeck: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true);
    private static readonly DuelCardRules SpellCard = new(CardCategory.Spell, AllowsSpellTrapSet: true);

    private static string Rank(uint rank) => $"xyz-rank-{rank}";

    private static Entry Card(uint id, string name, CardKind kind, uint getter, uint icon, DuelCardRules rules, string operation, string conditions,
        string costs = "none", string targets = "none", string cleanup = NoChain, string exceptions = "none", EffectDefinition[]? definitions = null, TriggerEffectProfile[]? triggers = null)
    {
        var registered = definitions ?? [];
        var fired = triggers ?? [];
        var anchor = new EffectDefinition($"summon-{id}-rules-v1", id, EffectKind.ContinuousSpell, [], isMarker: true);
        string[] references = [.. registered.Select(d => d.EffectId), .. fired.Select(t => t.EffectId)];
        if (references.Length == 0) references = [anchor.EffectId];
        var profile = new DefinitionCardProfile(id, name, kind, registered.FirstOrDefault() ?? anchor, operation, conditions, costs, targets,
            targets == "none" ? "none" : "Target when activated.", "Default.", cleanup, exceptions, [], []);
        return new(profile, references, getter, icon, rules, registered, fired);
    }

    private static Entry CyberDragon(uint id) => Card(id, "Cyber Dragon", CardKind.Monster, EffectMonsterKind, 0, MainDeckMonster,
        "Special Summon from the hand if your opponent controls a monster and you control none.", "Own Main Phase; you control no monsters and your opponent controls one.");

    private static EffectDefinition ChaosSorcererEffect { get; } = new("summon-5833-banish-v1", 5833, EffectKind.MonsterIgnition, [new BanishFieldTargetsStep()],
        fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp)], oncePerTurn: true, locksOwnAttack: true);
    private static EffectDefinition FrostAndFlameEffect { get; } = new("summon-7210-destroy-v1", 7210, EffectKind.MonsterIgnition, [new DestroyTargetsStep()],
        cost: new DiscardCost(), fieldTargets: [new FieldTarget(FieldCardKind.Monster)], oncePerTurn: true);
    private static EffectDefinition DesertTwisterEffect { get; } = new("summon-7211-destroy-v1", 7211, EffectKind.MonsterIgnition, [new DestroyTargetsStep()],
        cost: new DiscardCost(), fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap)], oncePerTurn: true);
    private static EffectDefinition GustavMaxEffect { get; } = new("summon-9963-burn-v1", 9963, EffectKind.MonsterIgnition, [new DealDamageStep(2000)],
        cost: new DetachCost(1), oncePerTurn: true);
    private static EffectDefinition AdreusEffect { get; } = new("summon-9846-destroy-v1", 9846, EffectKind.MonsterIgnition, [new DestroyTargetsStep()],
        cost: new DetachCost(1), fieldTargets: [new FieldTarget(FieldCardKind.Any, TargetSide.Opponent, FieldCardFilter.FaceUp)], oncePerTurn: true);
    private static EffectDefinition WayneSummon { get; } = new("summon-7835-recruit-v1", 7835, EffectKind.MonsterTrigger,
        [new SpecialSummonStep(SpecialSummonSources.Hand, new MonsterLevelFilter(4, Warrior))]);
    private static ContinuousEffectProfile ComradeAura { get; } = new("summon-7895-aura-v1", 7895, EffectRelation.Controller, 400, 0) { Filter = new MonsterTypeFilter([Warrior]) };

    private static Entry Fusion(uint id, string name, string materials) => Card(id, name, CardKind.Monster, FusionKind, 0, ExtraDeckMonster,
        $"Fusion Summoned with a Fusion Spell using {materials}.", "A Fusion Spell resolves.", cleanup: "Summoned as part of a resolving Spell; it cannot be negated once that Spell resolves.");
    private static Entry RitualMonster(uint id, string name, string spell) => Card(id, name, CardKind.Monster, RitualKind, 0, HandOnlyMonster,
        $"Ritual Summoned from the hand with \"{spell}\".", "A matching Ritual Spell resolves.", cleanup: "Summoned as part of a resolving Spell; it cannot be negated once that Spell resolves.");
    private static Entry RitualSpell(uint id, string name, string monster, uint level) => Card(id, name, CardKind.Spell, SpellKind, IconRitual, SpellCard,
        $"Ritual Summon \"{monster}\" by Tributing monsters from your hand or field whose Levels total {level} or more.", "Own Main Phase; the Ritual Monster in hand and enough Tributes.",
        costs: "Tributes at resolution; none if the activation is negated.", cleanup: SpellCleanup, exceptions: "No more Tributes may be used than are needed.");
    private static Entry Polymerization(uint id) => Card(id, "Polymerization", CardKind.Spell, SpellKind, 0, SpellCard,
        "Fusion Summon 1 reviewed Fusion Monster using monsters from your hand or field.", "Own Main Phase; the listed materials are available.",
        costs: "Materials at resolution; none if the activation is negated.", cleanup: SpellCleanup, exceptions: "Only reviewed Fusion recipes can be made.");
    private static Entry XyzMonster(uint id, string name, uint getter, uint rank, string operation, EffectDefinition[]? definitions = null, string costs = "none", string targets = "none") =>
        Card(id, name, CardKind.Monster, getter, 0, ExtraDeckMonster, operation, $"Own Main Phase; 2 face-up Level {rank} monsters.", costs, targets, definitions is null ? NoChain : MonsterCleanup, definitions: definitions);

    private static readonly uint[] GaiaKnight = [3809, 4044], BlueEyes = [3800, 3801, 3802, 3849, 3862, 4007];
    private static readonly uint[] MaterialIds = [4148, 4299, 4045, .. GaiaKnight, .. BlueEyes];

    private static readonly FusionSummonRecipe[] Recipes =
    [
        new(3803, [4148, 4299]),
        new(4021, [4148, 4299]),
        FusionSummonRecipe.WithAlternatives(4043, GaiaKnight, [4045]),
        FusionSummonRecipe.WithAlternatives(3820, BlueEyes, BlueEyes, BlueEyes),
        FusionSummonRecipe.WithAlternatives(4386, BlueEyes, BlueEyes, BlueEyes)
    ];

    private static readonly Entry[] Entries =
    [
        CyberDragon(3847),
        CyberDragon(6390),
        Card(5833, "Chaos Sorcerer", CardKind.Monster, SpecialSummonOnlyKind, 0, HandOnlyMonster, "Special Summon from the hand by banishing 1 LIGHT and 1 DARK monster from your Graveyard. Once per turn: banish 1 face-up monster; this card cannot attack that turn.",
            "Own Main Phase.", "Banish 1 LIGHT and 1 DARK monster from your Graveyard to Summon.", "1 face-up monster on the field.", MonsterCleanup, "Can be revived once properly Summoned. Cannot activate its effect if it already attacked this turn.", [ChaosSorcererEffect]),
        Card(7210, "Frost and Flame Dragon", CardKind.Monster, SpecialSummonOnlyKind, 0, HandOnlyMonster, "Special Summon from the hand by banishing 1 FIRE and 2 WATER monsters from your Graveyard. Once per turn: discard 1 card to destroy 1 monster on the field.",
            "Own Main Phase.", "Banish 1 FIRE and 2 WATER monsters to Summon; discard 1 card for its effect.", "1 monster on the field.", MonsterCleanup, "Cannot be Special Summoned any other way.", [FrostAndFlameEffect]),
        Card(7211, "Desert Twister", CardKind.Monster, SpecialSummonOnlyKind, 0, HandOnlyMonster, "Special Summon from the hand by banishing 1 EARTH and 2 WIND monsters from your Graveyard. Once per turn: discard 1 card to destroy 1 Spell or Trap Card on the field.",
            "Own Main Phase.", "Banish 1 EARTH and 2 WIND monsters to Summon; discard 1 card for its effect.", "1 Spell or Trap Card on the field.", MonsterCleanup, "Cannot be Special Summoned any other way.", [DesertTwisterEffect]),
        Polymerization(4837),
        Polymerization(4867),
        Fusion(3803, "Flame Swordsman", "\"Flame Manipulator\" + \"Masaki the Legendary Swordsman\""),
        Fusion(4021, "Flame Swordsman", "\"Flame Manipulator\" + \"Masaki the Legendary Swordsman\""),
        Fusion(4043, "Gaia the Dragon Champion", "\"Gaia the Fierce Knight\" + \"Curse of Dragon\""),
        Fusion(3820, "Blue-Eyes Ultimate Dragon", "3 \"Blue-Eyes White Dragon\""),
        Fusion(4386, "Blue-Eyes Ultimate Dragon", "3 \"Blue-Eyes White Dragon\""),
        RitualSpell(4676, "Black Luster Ritual", "Black Luster Soldier", 8),
        RitualSpell(4677, "Zera Ritual", "Zera the Mant", 8),
        RitualSpell(4679, "War-Lion Ritual", "Super War-Lion", 7),
        RitualMonster(4370, "Black Luster Soldier", "Black Luster Ritual"),
        RitualMonster(4366, "Zera the Mant", "Zera Ritual"),
        RitualMonster(4362, "Super War-Lion", "War-Lion Ritual"),
        XyzMonster(9601, "Gem-Knight Pearl", NormalXyzKind, 4, "Xyz Summoned using 2 Level 4 monsters."),
        XyzMonster(9963, "Superdreadnought Rail Cannon Gustav Max", XyzKind, 10, "Xyz Summoned using 2 Level 10 monsters. Once per turn: detach 1 material to inflict 2000 damage to your opponent.", [GustavMaxEffect], "Detach 1 Xyz Material."),
        XyzMonster(9846, "Adreus, Keeper of Armageddon", XyzKind, 5, "Xyz Summoned using 2 Level 5 monsters. Once per turn: detach 1 material to destroy 1 face-up card your opponent controls.", [AdreusEffect], "Detach 1 Xyz Material.", "1 face-up card your opponent controls."),
        Card(7835, "X-Saber Wayne", CardKind.Monster, SynchroKind, 0, ExtraDeckMonster, "Synchro Summoned with 1 Tuner and 1 or more non-Tuners. When Synchro Summoned: you can Special Summon 1 Level 4 or lower Warrior monster from your hand.",
            "Own Main Phase; a face-up Tuner and non-Tuners whose Levels total this card's.", cleanup: NoChain,
            triggers: [new(WayneSummon, MonsterEventKind.SpecialSummoned, EffectRelation.Self, TriggerRequirement.Optional, timing: TriggerTiming.When) { RequiredProcedureId = SynchroProcedureId }]),
        Card(7895, "Comrade Swordsman of Landstar", CardKind.Monster, TunerKind, 0, MainDeckMonster with { IsTuner = true }, "All Warrior monsters you control gain 400 ATK.", "Face-up on the field."),
        Card(FlashKnight, "Flash Knight", CardKind.Monster, NormalPendulumKind, 0, MainDeckMonster with { AllowsPendulumSummon = true }, "Normal Pendulum Monster with Pendulum Scale 7.", "Placed as a Scale from the hand, or Summoned as a monster.", cleanup: "A Scale's activation can be negated like a Spell.")
    ];

    public static IReadOnlyList<DefinitionCardProfile> Cards { get; } = Array.AsReadOnly(Entries.Select(e => e.Card).ToArray());
    public static IReadOnlyList<EffectDefinition> Definitions { get; } = Array.AsReadOnly(Entries.SelectMany(e => e.Definitions).ToArray());
    public static IReadOnlyList<TriggerEffectProfile> Triggers { get; } = Array.AsReadOnly(Entries.SelectMany(e => e.Triggers).ToArray());
    public static IReadOnlyList<ContinuousEffectProfile> ContinuousEffects { get; } = Array.AsReadOnly(new[] { ComradeAura });

    /// <summary>The family's cards plus the Normal Monsters its Fusion recipes use.</summary>
    public static IReadOnlyDictionary<uint, DuelCardRules> CardRules { get; } = Entries.Select(e => new KeyValuePair<uint, DuelCardRules>(e.Card.CardId, e.Rules))
        .Concat(MaterialIds.Select(id => new KeyValuePair<uint, DuelCardRules>(id, MainDeckMonster))).ToFrozenDictionary();

    /// <summary>Cards the rulebook lets revive only after a proper Summon, or, for two of them, never.</summary>
    public static IReadOnlyList<SpecialSummonCardProfile> SpecialSummonCards { get; } = Array.AsReadOnly(new[]
    {
        new SpecialSummonCardProfile(3847, SpecialSummonSources.All),
        new SpecialSummonCardProfile(6390, SpecialSummonSources.All),
        new SpecialSummonCardProfile(5833, Revivable, "chaos-sorcerer"),
        new SpecialSummonCardProfile(7210, SpecialSummonSources.None, "frost-and-flame"),
        new SpecialSummonCardProfile(7211, SpecialSummonSources.None, "desert-twister"),
        new SpecialSummonCardProfile(3803, Revivable, FusionProcedureId),
        new SpecialSummonCardProfile(4021, Revivable, FusionProcedureId),
        new SpecialSummonCardProfile(4043, Revivable, FusionProcedureId),
        new SpecialSummonCardProfile(3820, Revivable, FusionProcedureId),
        new SpecialSummonCardProfile(4386, Revivable, FusionProcedureId),
        new SpecialSummonCardProfile(4370, Revivable, RitualProcedureId),
        new SpecialSummonCardProfile(4366, Revivable, RitualProcedureId),
        new SpecialSummonCardProfile(4362, Revivable, RitualProcedureId),
        new SpecialSummonCardProfile(9601, Revivable, Rank(4)),
        new SpecialSummonCardProfile(9963, Revivable, Rank(10)),
        new SpecialSummonCardProfile(9846, Revivable, Rank(5)),
        new SpecialSummonCardProfile(7835, Revivable, SynchroProcedureId)
    });

    private static MonsterAttributeFilter Attribute(uint code) => new(code);

    public static IReadOnlyList<SpecialSummonProcedureProfile> Procedures { get; } = Array.AsReadOnly(new[]
    {
        new SpecialSummonProcedureProfile("cyber-dragon", [3847, 6390], requiresEmptyOwnField: true, requiresOpposingMonster: true),
        new SpecialSummonProcedureProfile("chaos-sorcerer", [5833], graveyardBanishCost: [Attribute(Light), Attribute(Dark)]),
        new SpecialSummonProcedureProfile("frost-and-flame", [7210], graveyardBanishCost: [Attribute(Fire), Attribute(Water), Attribute(Water)]),
        new SpecialSummonProcedureProfile("desert-twister", [7211], graveyardBanishCost: [Attribute(Earth), Attribute(Wind), Attribute(Wind)])
    });

    private static ExtraDeckMaterialProcedureProfile Xyz(uint id, uint rank) =>
        new(Rank(rank), [id], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(rank), anyMaterial: true);

    public static IReadOnlyList<ExtraDeckMaterialProcedureProfile> MaterialProcedures { get; } = Array.AsReadOnly(new[]
    {
        new ExtraDeckMaterialProcedureProfile(SynchroProcedureId, [7835], [], 2, synchro: SynchroMaterialRequirements.AnyTuner(), anyMaterial: true),
        Xyz(9601, 4),
        Xyz(9963, 10),
        Xyz(9846, 5)
    });

    private static SummonEffectProfile FusionSpell(uint id) => new($"summon-{id}-fusion-v1", id, FusionProcedureId, fusionRecipes: Recipes);
    private static SummonEffectProfile RitualProfile(uint spell, uint monster, uint level) =>
        new($"summon-{spell}-ritual-v1", spell, RitualProcedureId, ritualRecipes: [RitualSummonRecipe.AnyTributes(monster, level)]);

    public static IReadOnlyList<SummonEffectProfile> SummonEffects { get; } = Array.AsReadOnly(new[]
    {
        FusionSpell(4837), FusionSpell(4867), RitualProfile(4676, 4370, 8), RitualProfile(4677, 4366, 8), RitualProfile(4679, 4362, 7)
    });

    public static IReadOnlyList<PendulumCardProfile> PendulumCards { get; } = Array.AsReadOnly(new[] { new PendulumCardProfile(FlashKnight, FlashKnightScale, FlashKnightScale) });

    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } = Entries.ToFrozenDictionary(e => e.Card.CardId,
        e => new CardStaticMetadata(e.Card.Kind, new(CardEffectState.Referenced, e.References)));

    /// <summary>Throws when the catalog's metadata, kind codes or Pendulum scales differ from this family.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("This family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var entry in Entries)
        {
            var card = entry.Card;
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Name != card.Name || actual.Kind != card.Kind ||
                actual.Effects is not { State: CardEffectState.Referenced } effects || !effects.References.SequenceEqual(entry.References) ||
                actual.Codes is { } codes && (codes.GetterKind != entry.GetterKind || codes.Icon != entry.Icon))
                throw new InvalidDataException($"Card {card.CardId} metadata differs from this family.");
        }
        foreach (var scale in PendulumCards)
            if (byId[scale.CardId].Codes is { } codes && (codes.Field23To26 != scale.LeftScale || codes.Field27To30 != scale.RightScale))
                throw new InvalidDataException($"Card {scale.CardId} has different Pendulum Scales.");
        foreach (uint id in MaterialIds)
            if (!byId.ContainsKey(id)) throw new InvalidDataException($"Fusion material {id} is missing.");
    }
}
