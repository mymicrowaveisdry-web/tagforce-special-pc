using System.Text.Json.Serialization;

namespace TagForceSpecial.Core;

/// <summary>How a revived monster and the card that revived it depend on each other.</summary>
public enum RevivalLink
{
    None,
    /// <summary>The monster is destroyed when the card leaves the field; the card is destroyed when the monster is destroyed.</summary>
    Haunted,
    /// <summary>The card equips the monster; the monster is destroyed when the card is destroyed.</summary>
    Equip
}

/// <summary>A public count that multiplies a lasting stat change, from the lasting card's controller's side.</summary>
public enum LastingCount { None, OwnFaceUpMonsters, OwnSpellTraps }

/// <summary>What a lasting card negates while its effect is active.</summary>
public enum LastingNegation { None, FaceUpMonsters, Spells }

/// <summary>
/// A stat change a lasting card applies to matching face-up monsters while its effect is active. Side is relative to
/// the card's controller; a null filter matches every monster.
/// </summary>
public sealed record LastingStatModifier
{
    public TargetSide Side { get; }
    public int Attack { get; }
    public int Defense { get; }
    public CardFilter? Filter { get; }
    /// <summary>Applies only to the monster this card equips.</summary>
    public bool EquippedOnly { get; }
    /// <summary>When set, multiplies both changes.</summary>
    public LastingCount PerCount { get; }

    public LastingStatModifier(TargetSide side, int attack, int defense, CardFilter? filter = null, bool equippedOnly = false,
        LastingCount perCount = LastingCount.None)
    {
        if (!Enum.IsDefined(side) || !Enum.IsDefined(perCount)) throw new ArgumentOutOfRangeException(nameof(side));
        if (attack == 0 && defense == 0) throw new ArgumentException("A stat modifier must change ATK or DEF.", nameof(attack));
        if (equippedOnly && (side != TargetSide.Either || filter is not null))
            throw new ArgumentException("An equipped-only modifier has no side or filter of its own.", nameof(equippedOnly));
        Side = side; Attack = attack; Defense = defense; Filter = filter; EquippedOnly = equippedOnly; PerCount = perCount;
    }
}

/// <summary>Monsters whose raw type code is listed. Codes are decoded metadata, reviewed against known monsters.</summary>
public sealed record MonsterTypeFilter : CardFilter
{
    public IReadOnlySet<uint> TypeCodes { get; }
    public MonsterTypeFilter(IEnumerable<uint> typeCodes)
    {
        ArgumentNullException.ThrowIfNull(typeCodes);
        var codes = typeCodes.ToHashSet();
        TypeCodes = codes.Count != 0 ? codes : throw new ArgumentException("Supply at least one type code.", nameof(typeCodes));
    }
}

/// <summary>Attaches the resolving Equip Spell to its bound monster target.</summary>
public sealed record EquipTargetStep : EffectStep;

/// <summary>Moves the resolving Union monster to a Spell/Trap Zone as an Equip Card on its bound target.</summary>
public sealed record UnionEquipStep : EffectStep;

/// <summary>
/// Places counters on the bound face-up monster or Spell/Trap, up to <see cref="Max"/> in total. Protective counters
/// are removed instead of the card being destroyed.
/// </summary>
public sealed record PlaceCounterStep : EffectStep
{
    public int Count { get; }
    public int Max { get; }
    public bool Protective { get; }
    public PlaceCounterStep(int count, int max, bool protective = false)
    {
        if (count < 1 || max < count) throw new ArgumentOutOfRangeException(nameof(count));
        Count = count; Max = max; Protective = protective;
    }
}

/// <summary>Places the source card from the Graveyard on top of its owner's Deck; skipped if it is no longer there.</summary>
public sealed record ReturnSourceToDeckTopStep : EffectStep;

/// <summary>What a destruction replacement applies to.</summary>
[Flags]
public enum DestructionCause { Battle = 1, Effect = 2, Any = Battle | Effect }

/// <summary>Tribute the activating monster itself (never refunded). The effect still resolves.</summary>
public sealed record TributeSelfCost : EffectCost;

/// <summary>Remove counters from the activating monster.</summary>
public sealed record RemoveCounterCost : EffectCost
{
    public int Count { get; }
    public RemoveCounterCost(int count) => Count = count >= 1 ? count : throw new ArgumentOutOfRangeException(nameof(count));
}

/// <summary>Banish matching cards from your Graveyard, chosen at activation (never refunded).</summary>
public sealed record BanishFromGraveyardCost : EffectCost
{
    public CardFilter Filter { get; }
    public int Count { get; }
    public BanishFromGraveyardCost(CardFilter filter, int count = 1)
    {
        Filter = filter ?? throw new ArgumentNullException(nameof(filter));
        Count = count is >= 1 and <= 5 ? count : throw new ArgumentOutOfRangeException(nameof(count));
    }
}

/// <summary>A face-up monster's controller activates its ignition or quick effect in an open Main Phase state.</summary>
public sealed record ActivateMonsterEffect(PlayerId Player, int MonsterZone) : DuelCommand(Player);
/// <summary>A face-up monster's quick effect as the next chain link.</summary>
public sealed record ActivateMonsterChainResponse(PlayerId Player, long DecisionId, int MonsterZone) : DuelCommand(Player);
/// <summary>A face-up monster's quick effect starting a chain in an open battle window.</summary>
public sealed record ActivateMonsterBattleResponse(PlayerId Player, long DecisionId, int MonsterZone) : DuelCommand(Player);
/// <summary>Unequips a Union monster and Special Summons it; shares the Union's once-per-turn use.</summary>
public sealed record UnequipUnion(PlayerId Player, int SpellTrapZone) : DuelCommand(Player);
/// <summary>Settles a Standby Phase upkeep by paying LP or destroying the card.</summary>
public sealed record ResolveUpkeep(PlayerId Player, int SpellTrapZone, bool Pay) : DuelCommand(Player);
/// <summary>Special Summons a Toon monster from the hand by Tributing the monsters its Level requires.</summary>
public sealed record ToonSummon : DuelCommand
{
    public int CardInstanceId { get; }
    public int MonsterZone { get; }
    public BattlePosition Position { get; }
    public IReadOnlyList<int> TributeZones { get; }
    public long ExpectedRevision { get; }

    public ToonSummon(PlayerId player, int cardInstanceId, int monsterZone, BattlePosition position, IEnumerable<int> tributeZones, long expectedRevision)
        : base(player)
    {
        CardInstanceId = cardInstanceId; MonsterZone = monsterZone; Position = position;
        TributeZones = Array.AsReadOnly((tributeZones ?? throw new ArgumentNullException(nameof(tributeZones))).ToArray());
        ExpectedRevision = expectedRevision;
    }
}

public sealed record MonsterEffectActivated(PlayerId Player, string EffectId, CardInstance Card, int Zone) : DuelEvent;
/// <summary>A controller's old Field Spell went to the Graveyard because they placed a new one; it was not destroyed.</summary>
public sealed record FieldSpellReplaced(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record UnionEquipped(PlayerId Player, CardInstance Card, int SpellTrapZone, MonsterBinding Target) : DuelEvent;
/// <summary>A destruction was replaced: the Union equip was destroyed and the monster stayed.</summary>
public sealed record DestructionReplaced(MonsterBinding Monster, CardInstance Replacement) : DuelEvent;
/// <summary>A protective counter was removed so the Spell/Trap in that zone was not destroyed.</summary>
public sealed record SpellTrapDestructionReplaced(PlayerId Player, int Zone, CardInstance Card) : DuelEvent;
public sealed record SpellTrapCountersPlaced(PlayerId Player, int Zone, CardInstance Card, int Count, int Total) : DuelEvent;
public sealed record CardPlacedOnDeckTop(PlayerId Owner, CardInstance Card, string EffectId) : DuelEvent;
public sealed record CountersPlaced(MonsterBinding Monster, int Count, int Total) : DuelEvent;
public sealed record CountersRemoved(MonsterBinding Monster, int Count, int Total) : DuelEvent;
public sealed record UpkeepSettled(PlayerId Player, CardInstance Card, bool Paid) : DuelEvent;
