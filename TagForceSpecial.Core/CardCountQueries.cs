namespace TagForceSpecial.Core;

/// <summary>Counts card occupancy and recorded results without changing duel state.</summary>
internal static class CardCountQueries
{
    /// <summary>The optional matcher is invoked synchronously with the card and its graveyard scope; it owns effective-identity policy.</summary>
    internal static int Effect(DuelState state, PlayerId controller, EffectCount count, Func<CardInstance, bool, bool>? matches)
    {
        var own = state.GetPlayer(controller);
        var opponent = state.GetPlayer(controller == PlayerId.One ? PlayerId.Two : PlayerId.One);
        int Counted(IEnumerable<CardInstance> cards, bool inGraveyard = false) =>
            matches is null ? cards.Count() : cards.Count(c => matches(c, inGraveyard));
        IEnumerable<CardInstance> Monsters(DuelPlayerState p, bool faceUpOnly = false) =>
            p.MonsterZones.Where(m => m is not null && (!faceUpOnly || m.Position != BattlePosition.FaceDownDefense)).Select(m => m!.Card);
        return count switch
        {
            EffectCount.None => 1,
            EffectCount.OpponentMonsters => Counted(Monsters(opponent)),
            EffectCount.AllMonsters => Counted(Monsters(own).Concat(Monsters(opponent))),
            EffectCount.OwnMonsters => Counted(Monsters(own)),
            EffectCount.OwnFaceUpMonsters => Counted(Monsters(own, faceUpOnly: true)),
            EffectCount.OpponentHand => Counted(opponent.Hand),
            EffectCount.OpponentHandAndField => opponent.Hand.Count + Monsters(opponent).Count() +
                opponent.SpellTrapFields.Count(f => f is not null) + opponent.PendulumZones.Count(f => f is not null) + (opponent.FieldSpell is null ? 0 : 1),
            EffectCount.OpponentGraveyard => Counted(opponent.Graveyard, inGraveyard: true),
            EffectCount.OpponentBanished => Counted(opponent.Banished),
            EffectCount.DieRoll => state.Bookkeeping.LastRandom?.Die ?? 0,
            EffectCount.Heads => state.Bookkeeping.LastRandom?.Heads ?? 0,
            EffectCount.MilledLevel => state.Bookkeeping.LastRandom?.MilledLevel ?? 0,
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
    }

    internal static int Lasting(DuelState state, PlayerId controller, LastingCount count, Func<CardInstance, bool>? matches)
    {
        var player = state.GetPlayer(controller);
        return count switch
        {
            LastingCount.None => 1,
            LastingCount.OwnFaceUpMonsters => player.MonsterZones.Count(m => m is not null && m.Position != BattlePosition.FaceDownDefense &&
                (matches is null || matches(m.Card))),
            LastingCount.OwnSpellTraps => player.SpellTrapFields.Count(f => f is not null) + player.PendulumZones.Count(f => f is not null) + (player.FieldSpell is null ? 0 : 1),
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
    }

    internal static int ControlledMonsters(DuelState state, PlayerId player, TargetSide side, bool faceUpOnly, Func<CardInstance, bool> matches)
    {
        IEnumerable<DuelPlayerState> sides = side switch
        {
            TargetSide.Own => [state.GetPlayer(player)],
            TargetSide.Opponent => [state.GetPlayer(player == PlayerId.One ? PlayerId.Two : PlayerId.One)],
            _ => state.Players
        };
        return sides.Sum(p => p.MonsterZones.Count(m => m is not null && (!faceUpOnly || m.Position != BattlePosition.FaceDownDefense) && matches(m.Card)));
    }
}
