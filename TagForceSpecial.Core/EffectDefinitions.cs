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
/// Shuffles the activating player's Deck with the duel's seeded generator. The event carries no order; views
/// expose only the Deck count. Definitions using it require an engine seed.
/// </summary>
public sealed record ShuffleDeckStep : EffectStep;

/// <summary>
/// Special Summons the listed Tokens (one per entry, in order) to the activating player's lowest free Monster
/// Zones. All or nothing: with fewer free zones than Tokens at resolution, none are summoned (link skipped,
/// <see cref="ChainSkipReason.SummonUnavailable"/>). Every ID needs explicit <c>IsToken</c> card rules.
/// </summary>
public sealed record SummonTokensStep : EffectStep
{
    public IReadOnlyList<uint> TokenCardIds { get; }
    public BattlePosition Position { get; }

    public SummonTokensStep(IEnumerable<uint> tokenCardIds, BattlePosition position)
    {
        ArgumentNullException.ThrowIfNull(tokenCardIds);
        var ids = tokenCardIds.Take(6).ToArray();
        if (ids.Length is 0 or > 5 || ids.Any(id => id == 0))
            throw new ArgumentException("Summon 1–5 Tokens with nonzero IDs.", nameof(tokenCardIds));
        if (position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense))
            throw new ArgumentOutOfRangeException(nameof(position), "Tokens are summoned face-up.");
        TokenCardIds = Array.AsReadOnly(ids);
        Position = position;
    }
}

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
                    SummonTokensStep => false, // validated by its constructor; engine checks Token rules
                    ShuffleDeckStep => false,
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
/// <summary>The player's Deck was shuffled. Deliberately carries no card order.</summary>
public sealed record DeckShuffled(PlayerId Player, string EffectId) : DuelEvent;
/// <summary>A Token left the field and ceased to exist; follows the departure's cause event.</summary>
public sealed record TokenRemoved(PlayerId Owner, CardInstance Card) : DuelEvent;
