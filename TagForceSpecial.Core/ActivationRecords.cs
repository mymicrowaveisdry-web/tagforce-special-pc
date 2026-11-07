namespace TagForceSpecial.Core;

/// <summary>Identity of one uninterrupted back-row placement; face-up changes do not retarget it.</summary>
public sealed record SpellTrapTarget(PlayerId Player, int Zone, CardInstance Card, long PlacementId, bool IsPendulumZone = false)
{
    public FieldSpellTrap? Current(DuelState state) => IsPendulumZone
        ? state.GetPlayer(Player).PendulumZones[Zone] : state.GetPlayer(Player).SpellTrapFields[Zone];
    public bool IsStillPresent(FieldSpellTrap? current) => current is not null &&
        current.Card == Card && current.PlacementId == PlacementId;
}

/// <summary>An activation after its costs were paid and its target bound; consumed by its chain link.</summary>
public sealed record CommittedActivation(PlayerId Player, string EffectId, CardInstance Spell,
    int SpellTrapZone, CardInstance? DiscardedCost, SpellTrapTarget? Target);
public sealed record ActivationCostPaid(PlayerId Player, string EffectId, CardInstance Card) : DuelEvent;
public sealed record ActivationCommitted(CommittedActivation Activation) : DuelEvent;
