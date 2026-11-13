namespace TagForceSpecial.Core;

/// <summary>Activates a Pendulum Effect of the face-up scale in Pendulum Zone 0 or 1.</summary>
public sealed record ActivatePendulumEffect(PlayerId Player, int PendulumZone, int EffectIndex = 0) : DuelCommand(Player);

/// <summary>
/// Ends the effect unless its source scale was activated this turn. As a trigger's first step it is also needed to activate.
/// </summary>
public sealed record StopUnlessActivatedThisTurnStep : EffectStep;

public sealed partial class DuelEngine
{
    /// <summary>Scales whose activation resolved; their Pendulum Effects apply while they stay in place.</summary>
    private static IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> ActiveScales(DuelState state) =>
        state.Players.SelectMany(p => Enumerable.Range(0, 2).Select(zone => (Player: p.Id, Zone: zone, Field: p.PendulumZones[zone])))
            .Where(e => e.Field is { IsFaceUp: true, EffectActive: true }).Select(e => (e.Player, e.Zone, e.Field!));

    private static bool ScaleSourcePresent(DuelState state, MonsterBinding source) =>
        source.Zone is 0 or 1 && state.GetPlayer(source.Player).PendulumZones[source.Zone] is { IsFaceUp: true, EffectActive: true } scale &&
        scale.Card == source.Card && scale.PlacementId == source.PlacementId;

    private static bool ScaleActivatedThisTurn(DuelState state, PlayerId player, int zone, CardInstance card, long placement) =>
        zone is 0 or 1 && state.GetPlayer(player).PendulumZones[zone] is { } scale && scale.Card == card && scale.PlacementId == placement &&
        scale.ActivatedOnTurn == state.TurnNumber;

    /// <summary>The scale's activation resolved: its Pendulum Effects start to apply.</summary>
    private static DuelState ActivateScaleEffects(DuelState state, ChainLink link)
    {
        var player = state.GetPlayer(link.Activation.Player);
        int zone = link.Activation.SpellTrapZone;
        if (zone is not (0 or 1) || player.PendulumZones[zone] is not { } scale || scale.Card != link.Activation.Spell || scale.PlacementId != link.SourcePlacementId)
            return state;
        var zones = player.PendulumZones.ToArray();
        zones[zone] = scale with { EffectActive = true };
        return state.With(player: player.With(pendulumZones: zones));
    }

    /// <summary>The activated Pendulum Effects of this card.</summary>
    internal IEnumerable<int> PendulumEffectIndexes(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects)
        ? Enumerable.Range(0, effects.Length).Where(i => effects[i].Origin == EffectOrigin.PendulumZone && effects[i].IsFaceUpEffect) : [];

    private DuelActionResult BeginPendulumEffect(PlayerId playerId, int zone, int effectIndex)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is not (0 or 1)) return Reject(DuelError.InvalidPendulumZone);
        var scale = State.GetPlayer(playerId).PendulumZones[zone];
        if (scale is not { IsFaceUp: true, EffectActive: true } || !EffectAt(scale.Card.GameCardId, effectIndex, out var definition) ||
            definition.Origin != EffectOrigin.PendulumZone || !definition.IsFaceUpEffect)
            return Reject(DuelError.UnsupportedSpellEffect);
        if (definition.OncePerTurn && UsedThisTurn(scale.Card, definition, scale.PlacementId)) return Reject(DuelError.EffectAlreadyUsed);
        if (DefinitionActivationError(definition, ActivationContext.SpellTrapField, playerId, scale.Card, scale) is not DuelError.None and var error) return Reject(error);
        var next = definition.OncePerTurn ? RecordUse(State, scale.Card, definition, scale.PlacementId) : State;
        var (paid, head) = PayLifePointCost(next, playerId, definition, [new FieldEffectActivated(playerId, definition.EffectId, scale.Card, zone)]);
        return ContinueCommit(paid, head, playerId, scale.Card, zone, definition, scale.PlacementId, null);
    }

    /// <summary>Stat changes from the Pendulum Effects of active scales. A scale is a Spell Card, so Spell negation stops them.</summary>
    private void AddPendulumModifiers(DuelState state, (MonsterBinding Binding, FieldMonster Monster)[] monsters, List<StatModifier> result)
    {
        if (IsSpellsNegated(state)) return;
        foreach (var (player, zone, scale) in ActiveScales(state))
        {
            if (!effectsByCard.TryGetValue(scale.Card.GameCardId, out var effects)) continue;
            foreach (var definition in effects.Where(d => d.Origin == EffectOrigin.PendulumZone))
                foreach (var lasting in definition.LastingModifiers)
                {
                    int multiplier = LastingMultiplier(state, player, lasting.PerCount, lasting.CountFilter);
                    foreach (var (target, monster) in monsters)
                        if ((lasting.Side == TargetSide.Either || lasting.Side == TargetSide.Own == (target.Player == player)) &&
                            (lasting.Filter is null || MatchesFilter(monster.Card, lasting.Filter)))
                            result.Add(new(definition.EffectId, null, target, checked(lasting.Attack * multiplier), checked(lasting.Defense * multiplier),
                                SpellTrapSource: new(player, zone, scale.Card, scale.PlacementId, true)));
                }
        }
    }
}
