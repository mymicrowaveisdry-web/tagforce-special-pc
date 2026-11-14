namespace TagForceSpecial.Core;

public enum EffectSummonKind { Fusion, Ritual }

/// <summary>An exact card-ID multiset; no substitutes, names, types or alternate procedures are inferred.</summary>
public sealed class FusionSummonRecipe
{
    public uint TargetCardId { get; }
    public IReadOnlyList<uint> MaterialCardIds { get; }
    /// <summary>One entry per material; each lists the card IDs (artworks of the named card) that can fill it.</summary>
    public IReadOnlyList<IReadOnlyList<uint>> MaterialSlots { get; }

    public FusionSummonRecipe(uint targetCardId, IEnumerable<uint> materialCardIds)
    {
        ArgumentNullException.ThrowIfNull(materialCardIds);
        var ids = materialCardIds.ToArray();
        if (targetCardId == 0 || ids.Length is < 2 or > 5 || ids.Any(id => id == 0))
            throw new ArgumentException("Supply a target and two to five explicit materials, including multiplicities.");
        TargetCardId = targetCardId; MaterialCardIds = Array.AsReadOnly(ids);
        MaterialSlots = Array.AsReadOnly(ids.Select(id => (IReadOnlyList<uint>)Array.AsReadOnly(new[] { id })).ToArray());
    }

    private FusionSummonRecipe(uint targetCardId, IReadOnlyList<uint>[] slots)
    {
        if (targetCardId == 0 || slots.Length is < 2 or > 5 || slots.Any(s => s.Count == 0 || s.Any(id => id == 0)))
            throw new ArgumentException("Supply a target and two to five material slots with explicit card IDs.");
        TargetCardId = targetCardId; MaterialSlots = Array.AsReadOnly(slots);
        MaterialCardIds = Array.AsReadOnly(slots.SelectMany(s => s).Distinct().ToArray());
    }

    /// <summary>A recipe whose materials can each be any of several reviewed card IDs.</summary>
    public static FusionSummonRecipe WithAlternatives(uint targetCardId, params uint[][] slots) =>
        new(targetCardId, (slots ?? throw new ArgumentNullException(nameof(slots))).Select(s => (IReadOnlyList<uint>)Array.AsReadOnly(s.ToArray())).ToArray());
}

/// <summary>One to five Tributes reaching a positive printed-level total, exactly or, for "or more" recipes, without a surplus Tribute.</summary>
public sealed class RitualSummonRecipe
{
    public uint TargetCardId { get; }
    public uint RequiredLevel { get; }
    public IReadOnlyList<uint> TributeCardIds { get; }
    /// <summary>Any eligible monster can be Tributed and the total can exceed the requirement, if no Tribute is unnecessary.</summary>
    public bool OrMore { get; }

    /// <summary>A Ritual Spell that accepts any monsters whose Levels total the requirement or more.</summary>
    public static RitualSummonRecipe AnyTributes(uint targetCardId, uint requiredLevel) => new(targetCardId, requiredLevel);

    private RitualSummonRecipe(uint targetCardId, uint requiredLevel)
    {
        if (targetCardId == 0 || requiredLevel == 0) throw new ArgumentException("Supply a target and positive level total.");
        TargetCardId = targetCardId; RequiredLevel = requiredLevel; OrMore = true;
        TributeCardIds = Array.AsReadOnly(Array.Empty<uint>());
    }

    public RitualSummonRecipe(uint targetCardId, uint requiredLevel, IEnumerable<uint> tributeCardIds)
    {
        ArgumentNullException.ThrowIfNull(tributeCardIds);
        var ids = tributeCardIds.ToArray();
        if (targetCardId == 0 || requiredLevel == 0 || ids.Length == 0 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Supply a target, exact positive level total and distinct Tribute IDs.");
        TargetCardId = targetCardId; RequiredLevel = requiredLevel; TributeCardIds = Array.AsReadOnly(ids);
    }
}

/// <summary>Bounded Normal Spell effect: own hand/field materials and own Extra Deck Fusion or hand Ritual target.</summary>
public sealed class SummonEffectProfile
{
    public string EffectId { get; }
    public uint SpellCardId { get; }
    public string ProperProcedureId { get; }
    public bool AllowFaceDownFieldMaterials { get; }
    public EffectSummonKind Kind => FusionRecipes.Count != 0 ? EffectSummonKind.Fusion : EffectSummonKind.Ritual;
    public IReadOnlyList<FusionSummonRecipe> FusionRecipes { get; }
    public IReadOnlyList<RitualSummonRecipe> RitualRecipes { get; }
    public SummonEffectProfile(string effectId, uint spellCardId, string properProcedureId,
        IEnumerable<FusionSummonRecipe>? fusionRecipes = null, IEnumerable<RitualSummonRecipe>? ritualRecipes = null,
        bool allowFaceDownFieldMaterials = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(properProcedureId);
        if (spellCardId == 0) throw new ArgumentOutOfRangeException(nameof(spellCardId));
        var fusion = (fusionRecipes ?? []).ToArray(); var ritual = (ritualRecipes ?? []).ToArray();
        if ((fusion.Length == 0) == (ritual.Length == 0) || fusion.Any(r => r is null) || ritual.Any(r => r is null) ||
            fusion.Select(r => r.TargetCardId).Distinct().Count() != fusion.Length ||
            ritual.Select(r => r.TargetCardId).Distinct().Count() != ritual.Length)
            throw new ArgumentException("Supply distinct target recipes for exactly one summon kind.");
        EffectId = effectId; SpellCardId = spellCardId; ProperProcedureId = properProcedureId;
        FusionRecipes = Array.AsReadOnly(fusion); RitualRecipes = Array.AsReadOnly(ritual);
        AllowFaceDownFieldMaterials = allowFaceDownFieldMaterials;
    }
}

/// <summary>A hand card has no field binding; field options retain uninterrupted placement identity.</summary>
public sealed record EffectSummonMaterial(CardInstance Card, MonsterBinding? Field);

public sealed class PendingEffectSummon
{
    public long DecisionId { get; }
    public PlayerId Player { get; }
    public string EffectId { get; }
    public EffectSummonKind Kind { get; }
    public IReadOnlyList<CardInstance> Targets { get; }
    public IReadOnlyList<EffectSummonMaterial> Materials { get; }
    internal PendingEffectSummon(long decisionId, PlayerId player, SummonEffectProfile profile,
        IEnumerable<CardInstance> targets, IEnumerable<EffectSummonMaterial> materials)
    {
        DecisionId = decisionId; Player = player; EffectId = profile.EffectId; Kind = profile.Kind;
        Targets = Array.AsReadOnly(targets.ToArray()); Materials = Array.AsReadOnly(materials.ToArray());
    }
}

/// <summary>Resolution choice, never a free summon. The current chain decision also pins the current state revision.</summary>
public sealed record ChooseEffectSummon : DuelCommand
{
    public long DecisionId { get; }
    public int TargetInstanceId { get; }
    public IReadOnlyList<int> MaterialInstanceIds { get; }
    public int MonsterZone { get; }
    public BattlePosition Position { get; }
    public ChooseEffectSummon(PlayerId player, long decisionId, int targetInstanceId,
        IEnumerable<int> materialInstanceIds, int monsterZone, BattlePosition position = BattlePosition.FaceUpAttack) : base(player)
    {
        ArgumentNullException.ThrowIfNull(materialInstanceIds);
        DecisionId = decisionId; TargetInstanceId = targetInstanceId;
        MaterialInstanceIds = Array.AsReadOnly(materialInstanceIds.ToArray()); MonsterZone = monsterZone; Position = position;
    }
}

public sealed record EffectSummonRequested(PendingEffectSummon Decision) : DuelEvent;
/// <summary>Fusion material movement is an effect, not destruction, discard or an activation cost.</summary>
public sealed record FusionMaterialSentToGraveyard(EffectSummonMaterial Material, string EffectId) : DuelEvent;
/// <summary>A Ritual Tribute is paid at effect resolution, from hand or field, and is not destruction.</summary>
public sealed record RitualMaterialTributed(EffectSummonMaterial Material, string EffectId) : DuelEvent;
