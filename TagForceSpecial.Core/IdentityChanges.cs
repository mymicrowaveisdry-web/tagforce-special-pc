namespace TagForceSpecial.Core;

/// <summary>A monster property an effect can change: its Type or Attribute (raw catalog codes).</summary>
public enum IdentityTrait { Type, Attribute }

/// <summary>Which monsters a lasting change from a face-up card reaches.</summary>
public enum TraitScope
{
    /// <summary>Every face-up monster on the field.</summary>
    AllFaceUp,
    /// <summary>The monster this Equip Card is equipped to.</summary>
    Equipped,
    /// <summary>Every monster in either Graveyard.</summary>
    Graveyards
}

/// <summary>While the card is face-up, the monsters in <paramref name="Scope"/> have the card's declared value as their <paramref name="Trait"/>.</summary>
public sealed record LastingTraitChange(IdentityTrait Trait, TraitScope Scope);

/// <summary>Each bound face-up monster takes the effect's declared value as its <paramref name="Trait"/>, until the end of the turn or for good.</summary>
public sealed record ChangeTargetsTraitStep(IdentityTrait Trait, bool UntilEndOfTurn = true) : EffectStep;

/// <summary>Each bound card in a Pendulum Zone takes the effect's declared value as both of its Pendulum Scales for the rest of the turn.</summary>
public sealed record SetTargetScaleStep : EffectStep;

public sealed record TraitChanged(CardInstance Card, IdentityTrait Trait, uint Value, string EffectId) : DuelEvent;
public sealed record ScaleChanged(CardInstance Card, uint Scale, string EffectId) : DuelEvent;

internal sealed record IdentityChange(CardInstance Card, long PlacementId, IdentityTrait Trait, uint Value, int LastTurn);
internal sealed record ScaleChange(CardInstance Card, long PlacementId, uint Scale, int Turn);

public sealed partial class DuelEngine
{
    /// <summary>The state current trait checks read: the one being resolved or prepared, otherwise the accepted state.</summary>
    private DuelState? identityView;
    private DuelState IdentityState => identityView ?? State;

    private bool? identityEffects;
    private bool HasIdentityEffects => identityEffects ??= allEffects.Any(d => d.LastingTraits.Count != 0 || d.Steps.Any(s => s is ChangeTargetsTraitStep)) ||
        triggerEffects.Any(p => p.Definition?.Steps.Any(s => s is ChangeTargetsTraitStep) == true);

    /// <summary>
    /// The monster's current value for <paramref name="trait"/>: lasting changes from face-up cards first, then changes made to it by
    /// resolved effects (the latest wins). Null when nothing changes it.
    /// </summary>
    private uint? ChangedTrait(DuelState state, CardInstance card, IdentityTrait trait)
    {
        if (!HasIdentityEffects) return null;
        var field = FieldMonsters(state).FirstOrDefault(e => e.Monster.Card == card);
        bool faceUp = field.Monster is { Position: not BattlePosition.FaceDownDefense };
        bool inGraveyard = state.Players.Any(p => p.Graveyard.Contains(card));
        uint? value = null;
        foreach (var (_, _, source) in ActiveCards(state))
        {
            if (source.Declared is not { } declared || !effectsByCard.TryGetValue(source.Card.GameCardId, out var effects)) continue;
            foreach (var lasting in effects.SelectMany(d => d.LastingTraits).Where(l => l.Trait == trait))
                if (lasting.Scope switch
                    {
                        TraitScope.AllFaceUp => faceUp,
                        TraitScope.Equipped => faceUp && source.EquippedTo is { } equipped && equipped.Card == card && equipped.PlacementId == field.Monster!.PlacementId,
                        _ => inGraveyard
                    })
                    value = (uint)declared;
        }
        if (faceUp)
            foreach (var change in state.Bookkeeping.IdentityChanges)
                if (change.Card == card && change.PlacementId == field.Monster!.PlacementId && change.Trait == trait && change.LastTurn >= state.TurnNumber)
                    value = change.Value;
        return value;
    }

    private uint? TypeCode(CardInstance card) => ChangedTrait(IdentityState, card, IdentityTrait.Type) ?? catalog[card.GameCardId].Codes?.Type;
    private uint? AttributeCode(CardInstance card) => ChangedTrait(IdentityState, card, IdentityTrait.Attribute) ?? catalog[card.GameCardId].Codes?.Attribute;

    private static DuelState ChangeTrait(DuelState state, FieldCardTarget target, ChangeTargetsTraitStep step, int value, string effectId, List<DuelEvent> events)
    {
        if (target.Location != FieldLocation.MonsterZone || state.GetPlayer(target.Player).MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense })
            return state;
        events.Add(new TraitChanged(target.Card, step.Trait, (uint)value, effectId));
        var change = new IdentityChange(target.Card, target.PlacementId, step.Trait, (uint)value, step.UntilEndOfTurn ? state.TurnNumber : int.MaxValue);
        return state.WithBookkeeping(state.Bookkeeping with
        {
            IdentityChanges = Array.AsReadOnly(state.Bookkeeping.IdentityChanges.Where(c => c.LastTurn >= state.TurnNumber).Append(change).ToArray())
        });
    }

    private static DuelState SetScale(DuelState state, FieldCardTarget target, int value, string effectId, List<DuelEvent> events)
    {
        if (target.Location != FieldLocation.PendulumZone) return state;
        events.Add(new ScaleChanged(target.Card, (uint)value, effectId));
        return state.WithBookkeeping(state.Bookkeeping with
        {
            ScaleChanges = Array.AsReadOnly(state.Bookkeeping.ScaleChanges.Where(c => c.Turn == state.TurnNumber)
                .Append(new ScaleChange(target.Card, target.PlacementId, (uint)value, state.TurnNumber)).ToArray())
        });
    }

    /// <summary>A scale's current Pendulum Scale on one side: a change made this turn, otherwise the printed value.</summary>
    private uint CurrentScale(DuelState state, FieldSpellTrap scale, bool left) =>
        state.Bookkeeping.ScaleChanges.LastOrDefault(c => c.Card == scale.Card && c.PlacementId == scale.PlacementId && c.Turn == state.TurnNumber) is { } changed
            ? changed.Scale
            : left ? pendulumCards[scale.Card.GameCardId].LeftScale : pendulumCards[scale.Card.GameCardId].RightScale;

    /// <summary>A declared filter turned into a concrete one, for effects that keep it after the declaration is gone (lasting locks).</summary>
    private CardFilter ConcreteDeclared(CardFilter filter) => filter is DeclaredFilter && resolvingDeclaration is { } declared
        ? declared.Kind switch
        {
            DeclarationKind.CardName or DeclarationKind.MonsterCardName =>
                new CardIdFilter(catalog.Cards.Where(c => c.Name == catalog[(uint)declared.Value].Name).Select(c => c.GameCardId)),
            DeclarationKind.MonsterType or DeclarationKind.FaceUpMonsterType => new MonsterTypeFilter([(uint)declared.Value]),
            DeclarationKind.Attribute => new MonsterAttributeFilter((uint)declared.Value),
            DeclarationKind.CardType => new CardCategoryFilter((CardCategory)declared.Value),
            _ => filter
        }
        : filter;
}
