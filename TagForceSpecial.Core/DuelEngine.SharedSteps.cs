namespace TagForceSpecial.Core;

/// <summary>Small effect steps many cards share: stat changes that end with the turn, flips by effect and extra Normal Summons.</summary>
public sealed partial class DuelEngine
{
    /// <summary>Adds a stat change that ends when the End Phase begins.</summary>
    private DuelState ModifyUntilEnd(DuelState state, FieldCardTarget target, ModifyTargetsUntilEndStep stats, string effectId, List<DuelEvent> events)
    {
        var effects = state.EventEffects;
        var modifier = new StatModifier(effectId, null, new(target.Player, target.Zone, target.Card, target.PlacementId), stats.Attack, stats.Defense,
            checked(State.Revision + 1), State.TurnNumber);
        events.Add(new MonsterModifierCreated(modifier));
        return state.WithEventEffects(effects.WithModifiers(effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
    }

    /// <summary>Turns a face-up monster face-down; Tokens and Xyz Monsters cannot be. Counters and a Gemini's effects are lost.</summary>
    private DuelState FlipFaceDown(DuelState state, FieldCardTarget target, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (player.MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense } monster || IsTokenId(monster.Card.GameCardId) || IsXyzCard(monster.Card))
            return state;
        var zones = player.MonsterZones.ToArray();
        zones[target.Zone] = monster with { Position = BattlePosition.FaceDownDefense, Counters = 0, GeminiActive = false };
        events.Add(new MonsterPositionChanged(player.Id, target.Zone, monster.Position, BattlePosition.FaceDownDefense));
        return state.With(player: player.With(monsters: zones));
    }

    /// <summary>Flips a face-down monster face-up by an effect, which counts as flipped for Flip effects but is not a Flip Summon.</summary>
    private static DuelState FlipFaceUp(DuelState state, FieldCardTarget target, FlipTargetsFaceUpStep step, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (player.MonsterZones[target.Zone] is not { Position: BattlePosition.FaceDownDefense } monster) return state;
        var zones = player.MonsterZones.ToArray();
        zones[target.Zone] = monster with { Position = step.Position };
        events.Add(new MonsterPositionChanged(player.Id, target.Zone, BattlePosition.FaceDownDefense, step.Position));
        events.Add(new MonsterFlippedByEffect(new(target.Player, target.Zone, target.Card, target.PlacementId)));
        return state.With(player: player.With(monsters: zones));
    }

    /// <summary>Allows one more Normal Summon or Set this turn; further grants in the same turn add nothing.</summary>
    private static DuelState GrantNormalSummon(DuelState state, string effectId, List<DuelEvent> events)
    {
        if (state.Bookkeeping.ExtraNormalSummonTurn == state.TurnNumber) return state;
        events.Add(new ExtraNormalSummonGranted(state.ActivePlayer, effectId));
        return state.WithBookkeeping(state.Bookkeeping with { ExtraNormalSummonTurn = state.TurnNumber, ExtraNormalSummonUsed = false });
    }

    private static bool NormalSummonAvailable(DuelState state) =>
        !state.NormalSummonUsed || state.Bookkeeping.ExtraNormalSummonTurn == state.TurnNumber && !state.Bookkeeping.ExtraNormalSummonUsed;

    /// <summary>Spends the turn's Normal Summon, or the extra one once that is gone.</summary>
    private static DuelState SpendNormalSummon(DuelState state) => state.NormalSummonUsed
        ? state.WithBookkeeping(state.Bookkeeping with { ExtraNormalSummonUsed = true })
        : state.With(normalSummonUsed: true);

    /// <summary>
    /// The state with every card that could be discarded already in the Graveyard, so a card about to be discarded counts
    /// as a possible target. Null when the cost cannot supply the targets by itself.
    /// </summary>
    private DuelState? DiscardPreview(PlayerId playerId, CardInstance source, EffectDefinition definition)
    {
        if (definition.Cost is not DiscardCost discard) return null;
        int needed = definition.FieldTargets.OfType<ZoneTarget>().Where(z => z.Zone == CardZone.Graveyard).Sum(z => z.MinCount);
        if (needed == 0 || needed > discard.Count) return null;
        var player = State.GetPlayer(playerId);
        return State.With(player: player.With(graveyard: player.Graveyard.Concat(player.Hand.Where(c => c != source && DiscardQualifies(c, discard.Filter)))));
    }

    private bool DiscardQualifies(CardInstance card, CardFilter? filter) => filter switch
    {
        null => true,
        CardIdFilter ids => ids.CardIds.Contains(card.GameCardId),
        _ => MatchesFilter(card, filter)
    };
}
