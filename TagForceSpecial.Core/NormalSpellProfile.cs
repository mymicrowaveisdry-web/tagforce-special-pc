namespace TagForceSpecial.Core;

public enum NormalSpellOperation { GainLifePoints, DamageOpponent }

/// <summary>Explicit targetless, cost-free Normal Spell; values are authored, never parsed from text.</summary>
public sealed record NormalSpellProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public NormalSpellOperation Operation { get; }
    public int Amount { get; }

    public NormalSpellProfile(string effectId, uint cardId, NormalSpellOperation operation, int amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        EffectId = effectId;
        CardId = cardId;
        Operation = operation;
        Amount = amount;
    }
}

public sealed record ActivateSetSpell(PlayerId Player, int SpellTrapZone) : DuelCommand(Player);
public sealed record EffectDamageDealt(PlayerId Player, int Damage, int LifePointsBefore,
    int LifePointsAfter, string EffectId) : DuelEvent;
