namespace TagForceSpecial.Core;

/// <summary>
/// Explicit research opt-in: hand activation, deck selection, attack-position Special Summon
/// and Spell cleanup. Optionally permits phase progression/basic battle to End Phase banishment.
/// No shuffle, responses or general gameplay continuation.
/// </summary>
public sealed class DeckSpecialSummonSpell
{
    public string EffectId { get; }
    public uint SpellCardId { get; }
    public IReadOnlyList<uint> EligibleMonsterIds { get; }
    public bool BanishAtEndPhase { get; }
    internal DecisionSequence Decisions { get; }

    public DeckSpecialSummonSpell(string effectId, uint spellCardId, IEnumerable<uint> eligibleMonsterIds,
        bool banishAtEndPhase = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (spellCardId is 0 or > 0x3fff) throw new ArgumentOutOfRangeException(nameof(spellCardId));
        var cardStep = DecisionStep.Card(DecisionCardSource.Deck, eligibleMonsterIds);
        EffectId = effectId;
        SpellCardId = spellCardId;
        BanishAtEndPhase = banishAtEndPhase;
        EligibleMonsterIds = cardStep.AllowedCardIds;
        Decisions = new($"spell:{effectId}", [cardStep, DecisionStep.Position([BattlePosition.FaceUpAttack]),
            DecisionStep.EmptyMonsterZone()]);
    }
}

public sealed record FieldSpellTrap(CardInstance Card, bool IsFaceUp, long PlacementId = 0, int SetOnTurn = 0);
public sealed record PendingSpellActivation(PlayerId Player, string EffectId, CardInstance Spell, int SpellTrapZone,
    CardInstance? DiscardedCost = null);
public sealed record ActivateSpell(PlayerId Player, int CardInstanceId, int SpellTrapZone) : DuelCommand(Player);
public sealed record SpellActivated(PlayerId Player, string EffectId, CardInstance Card, int Zone) : DuelEvent;
public sealed record MonsterSpecialSummoned(PlayerId Player, FieldMonster Monster, int Zone, DecisionCardSource Source) : DuelEvent;
public sealed record SpellSentToGraveyard(PlayerId Player, CardInstance Card, int Zone) : DuelEvent;
public sealed record EffectResolved(PlayerId Player, string EffectId, CardInstance Spell) : DuelEvent;
/// <summary>Bound to the summoned instance and its uninterrupted, face-up field placement.</summary>
public sealed record PendingEndPhaseBanishment(PlayerId Player, string EffectId, CardInstance Spell,
    CardInstance Monster, int MonsterZone, int DueTurn);
public sealed record EndPhaseBanishmentScheduled(PendingEndPhaseBanishment Banishment) : DuelEvent;
public sealed record MonsterBanished(PlayerId Player, CardInstance Card, int Zone, string EffectId) : DuelEvent;
public sealed record EndPhaseBanishmentExpired(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
