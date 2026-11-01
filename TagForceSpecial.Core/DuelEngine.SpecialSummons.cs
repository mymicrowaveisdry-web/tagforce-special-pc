namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private static IReadOnlyList<CardInstance> SpecialSourceCards(DuelPlayerState player, DecisionCardSource source) => source switch
    {
        DecisionCardSource.Hand => player.Hand,
        DecisionCardSource.Deck => player.Deck,
        DecisionCardSource.Graveyard => player.Graveyard,
        DecisionCardSource.Banished => player.Banished,
        DecisionCardSource.ExtraDeck => player.ExtraDeck,
        _ => Array.Empty<CardInstance>()
    };

    private static SpecialSummonSources SourceFlag(DecisionCardSource source) => source switch
    {
        DecisionCardSource.Hand => SpecialSummonSources.Hand,
        DecisionCardSource.Deck => SpecialSummonSources.Deck,
        DecisionCardSource.Graveyard => SpecialSummonSources.Graveyard,
        DecisionCardSource.Banished => SpecialSummonSources.Banished,
        DecisionCardSource.ExtraDeck => SpecialSummonSources.ExtraDeck,
        _ => SpecialSummonSources.None
    };

    private static DuelPlayerState RemoveSpecialSource(DuelPlayerState player, CardInstance card, DecisionCardSource source) => source switch
    {
        DecisionCardSource.Hand => player.With(hand: player.Hand.Where(c => c != card)),
        DecisionCardSource.Deck => player.With(deck: player.Deck.Where(c => c != card)),
        DecisionCardSource.Graveyard => player.With(graveyard: player.Graveyard.Where(c => c != card)),
        DecisionCardSource.Banished => player.With(banished: player.Banished.Where(c => c != card)),
        DecisionCardSource.ExtraDeck => player.With(extraDeck: player.ExtraDeck.Where(c => c != card)),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private DuelError CheckSpecialSummon(DuelState state, PlayerId playerId, CardInstance card, int zone,
        BattlePosition position, DecisionCardSource source, string? procedureId = null)
    {
        if (zone is < 0 or >= 5) return DuelError.InvalidMonsterZone;
        if (position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense)) return DuelError.InvalidBattlePosition;
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category != CardCategory.Monster)
            return DuelError.UnsupportedSpecialSummon;
        var player = state.GetPlayer(playerId);
        if (player.MonsterZones[zone] is not null) return DuelError.MonsterZoneOccupied;
        if (card.Owner != playerId || !SpecialSourceCards(player, source).Contains(card))
            return DuelError.SpecialSummonSourceUnavailable;
        if (specialSummonCards.TryGetValue(card.GameCardId, out var policy))
        {
            if (procedureId is null && !policy.EffectSources.HasFlag(SourceFlag(source)))
                return DuelError.UnsupportedSpecialSummon;
            if (policy.RequiredProperProcedureId is { } required &&
                !(source is DecisionCardSource.Hand or DecisionCardSource.ExtraDeck && procedureId == required) &&
                !(source is DecisionCardSource.Graveyard or DecisionCardSource.Banished &&
                  state.SummonHistory.Any(h => h.Card == card && h.ProperProcedureId == required)))
                return DuelError.ProperSummonRequired;
        }
        return DuelError.None;
    }

    private FieldMonster NewSummonedMonster(DuelState state, CardInstance card, BattlePosition position) =>
        new(card, position, SummonedOnTurn: state.TurnNumber, PlacementId: checked(State.Revision + 1));

    // Callers preflight before committing costs/choices and revalidate against resolution state.
    private DuelState PlaceSpecialSummon(DuelState next, PlayerId playerId, CardInstance card, int zone,
        BattlePosition position, DecisionCardSource source, List<DuelEvent> events, string? procedureId = null,
        long? attemptId = null, string? properProcedureId = null, bool sourceAlreadyRemoved = false, int? timingGroup = null)
    {
        var player = next.GetPlayer(playerId);
        if (!sourceAlreadyRemoved) player = RemoveSpecialSource(player, card, source);
        var monsters = player.MonsterZones.ToArray();
        var monster = NewSummonedMonster(next, card, position);
        monsters[zone] = monster;
        events.Add(new MonsterSpecialSummoned(playerId, monster, zone, source, procedureId, attemptId, properProcedureId)
            { TimingGroup = timingGroup });
        return next.With(player: player.With(monsters: monsters));
    }

    private DuelActionResult ExecuteSpecialProcedure(SpecialSummonByProcedure command)
    {
        if (command.ExpectedRevision != State.Revision) return Reject(DuelError.StaleSummonSelection);
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (command.ProcedureId is null || !specialSummonProcedures.TryGetValue(command.ProcedureId, out var procedure))
            return Reject(DuelError.UnsupportedSpecialSummon);
        var player = State.GetPlayer(command.Player);
        var card = SpecialSourceCards(player, procedure.Source).SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (card is null) return Reject(DuelError.SpecialSummonSourceUnavailable);
        if (!procedure.CardIds.Contains(card.GameCardId)) return Reject(DuelError.UnsupportedSpecialSummon);
        if (!procedure.Positions.Contains(command.Position)) return Reject(DuelError.InvalidBattlePosition);
        if (procedure.RequiresEmptyOwnField && player.MonsterZones.Any(m => m is not null) ||
            procedure.RequiresOpposingMonster && !State.GetPlayer(Other(player.Id)).MonsterZones.Any(m => m is not null))
            return Reject(DuelError.SpecialSummonConditionsNotMet);
        var error = CheckSpecialSummon(State, player.Id, card, command.MonsterZone, command.Position, procedure.Source, procedure.ProcedureId);
        if (error != DuelError.None) return Reject(error);
        string? proper = procedure.Source == DecisionCardSource.Hand ? procedure.ProcedureId : null;
        if (summonWindows.Contains(card.GameCardId))
        {
            var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id,
                card, command.MonsterZone, Other(player.Id), IsSpecialSummon: true, Source: procedure.Source,
                Position: command.Position, ProcedureId: procedure.ProcedureId, ProperProcedureId: proper);
            return Accept(State.With(player: RemoveSpecialSource(player, card, procedure.Source)).WithSummon(pending),
                new SummonAttempted(pending), new SummonResponseRequested(pending));
        }
        List<DuelEvent> events = [];
        var next = PlaceSpecialSummon(State, player.Id, card, command.MonsterZone, command.Position, procedure.Source,
            events, procedure.ProcedureId, properProcedureId: proper);
        return Accept(next, events.ToArray());
    }
}
