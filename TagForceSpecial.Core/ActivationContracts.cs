namespace TagForceSpecial.Core;

/// <summary>Research-only commitment boundary. It never resolves effects or chains.</summary>
public sealed class ActivationContract
{
    public string EffectId { get; }
    public uint SpellCardId { get; }
    public IReadOnlyList<uint> DiscardCardIds { get; }
    public bool RequiresSpellTrapTarget { get; }
    internal DecisionSequence Decisions { get; }

    public ActivationContract(string effectId, uint spellCardId,
        IEnumerable<uint>? discardCardIds = null, bool requiresSpellTrapTarget = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (spellCardId is 0 or > 0x3fff) throw new ArgumentOutOfRangeException(nameof(spellCardId));
        var steps = new List<DecisionStep>();
        if (discardCardIds is not null) steps.Add(DecisionStep.Card(DecisionCardSource.Hand, discardCardIds));
        if (requiresSpellTrapTarget) steps.Add(DecisionStep.SpellTrapTarget());
        if (steps.Count == 0) throw new ArgumentException("An activation contract requires a cost or target choice.");
        EffectId = effectId;
        SpellCardId = spellCardId;
        RequiresSpellTrapTarget = requiresSpellTrapTarget;
        DiscardCardIds = Array.AsReadOnly(steps.Where(s => s.Kind == DecisionKind.Card).SelectMany(s => s.AllowedCardIds).ToArray());
        Decisions = new($"activation:{effectId}", steps);
    }
}

/// <summary>Identity of one uninterrupted back-row placement; face-up changes do not retarget it.</summary>
public sealed record SpellTrapTarget(PlayerId Player, int Zone, CardInstance Card, long PlacementId)
{
    public bool IsStillPresent(FieldSpellTrap? current) => current is not null &&
        current.Card == Card && current.PlacementId == PlacementId;
}

public sealed record CommittedActivation(PlayerId Player, string EffectId, CardInstance Spell,
    int SpellTrapZone, CardInstance? DiscardedCost, SpellTrapTarget? Target);
public sealed record ActivationCostPaid(PlayerId Player, string EffectId, CardInstance Card) : DuelEvent;
public sealed record ActivationCommitted(CommittedActivation Activation) : DuelEvent;
