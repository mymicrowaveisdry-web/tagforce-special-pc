namespace TagForceSpecial.Core;

/// <summary>
/// Default-off negation responses for set cards in open chains, trigger chains, battle and summon windows.
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
    public BattleTiming BattleWindows { get; }
    internal DecisionSequence? Decisions { get; }

    public ChainResponseProfile(string effectId, uint cardId, ChainOperation operation,
        ChainSpellSpeed spellSpeed, IEnumerable<uint>? discardCardIds = null,
        CardCategory? respondsToCategory = null, bool destroyNegatedSource = false,
        BattleTiming battleWindows = BattleTiming.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (operation is not (ChainOperation.NegateActivation or ChainOperation.NegateEffect))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (spellSpeed is not (ChainSpellSpeed.Two or ChainSpellSpeed.Three))
            throw new ArgumentOutOfRangeException(nameof(spellSpeed));
        if (respondsToCategory is not (null or CardCategory.Monster or CardCategory.Spell or CardCategory.Trap))
            throw new ArgumentOutOfRangeException(nameof(respondsToCategory));
        if ((battleWindows & ~BattleTiming.All) != 0) throw new ArgumentOutOfRangeException(nameof(battleWindows));
        BattleWindows = battleWindows;
        List<DecisionStep> steps = [];
        if (discardCardIds is not null) steps.Add(DecisionStep.Card(DecisionCardSource.Hand, discardCardIds));
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
