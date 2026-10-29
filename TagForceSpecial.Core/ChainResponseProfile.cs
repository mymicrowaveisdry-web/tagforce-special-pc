namespace TagForceSpecial.Core;

/// <summary>
/// Trusted, default-off research metadata for previously set responses in the
/// activation slice and registered trigger chains. This is not a card-text interpreter or a complete
/// native legality/protection contract. Effect-only probes require explicit configuration.
/// </summary>
public sealed class ChainResponseProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public ChainOperation Operation { get; }
    public ChainSpellSpeed SpellSpeed { get; }
    public IReadOnlyList<uint> DiscardCardIds { get; }
    public CardCategory? RespondsToCategory { get; }
    public bool DestroyNegatedSource { get; }
    internal DecisionSequence? Decisions { get; }

    public ChainResponseProfile(string effectId, uint cardId, ChainOperation operation,
        ChainSpellSpeed spellSpeed, IEnumerable<uint>? discardCardIds = null,
        CardCategory? respondsToCategory = null, bool destroyNegatedSource = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (operation is not (ChainOperation.DestroyBoundSpellTrap or ChainOperation.NegateActivation or ChainOperation.NegateEffect))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (spellSpeed is not (ChainSpellSpeed.Two or ChainSpellSpeed.Three))
            throw new ArgumentOutOfRangeException(nameof(spellSpeed));
        if (respondsToCategory is not (null or CardCategory.Monster or CardCategory.Spell or CardCategory.Trap))
            throw new ArgumentOutOfRangeException(nameof(respondsToCategory));
        if (destroyNegatedSource && operation == ChainOperation.DestroyBoundSpellTrap)
            throw new ArgumentException("Source destruction is a separate negation policy.", nameof(destroyNegatedSource));
        List<DecisionStep> steps = [];
        if (discardCardIds is not null) steps.Add(DecisionStep.Card(DecisionCardSource.Hand, discardCardIds));
        if (operation == ChainOperation.DestroyBoundSpellTrap) steps.Add(DecisionStep.SpellTrapTarget());
        EffectId = effectId;
        CardId = cardId;
        Operation = operation;
        SpellSpeed = spellSpeed;
        RespondsToCategory = respondsToCategory;
        DestroyNegatedSource = destroyNegatedSource;
        DiscardCardIds = Array.AsReadOnly(steps.Where(s => s.Kind == DecisionKind.Card).SelectMany(s => s.AllowedCardIds).ToArray());
        if (steps.Count > 0) Decisions = new($"chain-response:{effectId}", steps);
    }
}
