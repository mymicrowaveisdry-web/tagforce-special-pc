namespace TagForceSpecial.Core;

/// <summary>Card activation kinds. Spell Speed and set-turn rules derive from the kind.</summary>
public enum EffectKind { NormalSpell, QuickPlaySpell, NormalTrap, CounterTrap }

/// <summary>
/// One ordered resolution operation. The set is closed: only Core defines steps, each with its own
/// contract and tests. Values are authored, never parsed from card text.
/// </summary>
public abstract record EffectStep
{
    private protected EffectStep() { }
}

/// <summary>The activating player gains LP (existing integer saturation policy).</summary>
public sealed record GainLifePointsStep(int Amount) : EffectStep;

/// <summary>Effect damage to the activating player's opponent; zero LP is an immediate terminal defeat.</summary>
public sealed record DealDamageStep(int Amount) : EffectStep;

/// <summary>
/// Explicit, validated card effect: identity, kind and ordered steps. Activation (preflight, commit,
/// response window) and resolution (last-first, negation, cleanup) are shared by every definition.
/// </summary>
public sealed class EffectDefinition
{
    public string EffectId { get; }
    public uint CardId { get; }
    public EffectKind Kind { get; }
    public IReadOnlyList<EffectStep> Steps { get; }

    public ChainSpellSpeed SpellSpeed => Kind switch
    {
        EffectKind.NormalSpell => ChainSpellSpeed.One,
        EffectKind.CounterTrap => ChainSpellSpeed.Three,
        _ => ChainSpellSpeed.Two
    };

    public EffectDefinition(string effectId, uint cardId, EffectKind kind, IEnumerable<EffectStep> steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(steps);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var snapshot = steps.Take(33).ToArray();
        if (snapshot.Length is 0 or > 32)
            throw new ArgumentException("A definition needs 1–32 resolution steps.", nameof(steps));
        foreach (var step in snapshot)
            if (step switch
                {
                    GainLifePointsStep s => s.Amount <= 0,
                    DealDamageStep s => s.Amount <= 0,
                    _ => true
                })
                throw new ArgumentException("Steps must be supported Core operations with positive amounts.", nameof(steps));
        EffectId = effectId;
        CardId = cardId;
        Kind = kind;
        Steps = Array.AsReadOnly(snapshot);
    }
}

public sealed record ActivateSetSpell(PlayerId Player, int SpellTrapZone) : DuelCommand(Player);
public sealed record EffectDamageDealt(PlayerId Player, int Damage, int LifePointsBefore,
    int LifePointsAfter, string EffectId) : DuelEvent;
