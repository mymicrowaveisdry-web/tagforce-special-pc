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
        DecisionCardSource.FaceUpExtraDeck => player.FaceUpExtraDeck,
        _ => Array.Empty<CardInstance>()
    };

    private static SpecialSummonSources SourceFlag(DecisionCardSource source) => source switch
    {
        DecisionCardSource.Hand => SpecialSummonSources.Hand,
        DecisionCardSource.Deck => SpecialSummonSources.Deck,
        DecisionCardSource.Graveyard => SpecialSummonSources.Graveyard,
        DecisionCardSource.Banished => SpecialSummonSources.Banished,
        DecisionCardSource.ExtraDeck => SpecialSummonSources.ExtraDeck,
        DecisionCardSource.FaceUpExtraDeck => SpecialSummonSources.FaceUpExtraDeck,
        _ => SpecialSummonSources.None
    };

    private static DuelPlayerState RemoveSpecialSource(DuelPlayerState player, CardInstance card, DecisionCardSource source) => source switch
    {
        DecisionCardSource.Hand => player.With(hand: player.Hand.Where(c => c != card)),
        DecisionCardSource.Deck => player.With(deck: player.Deck.Where(c => c != card)),
        DecisionCardSource.Graveyard => player.With(graveyard: player.Graveyard.Where(c => c != card)),
        DecisionCardSource.Banished => player.With(banished: player.Banished.Where(c => c != card)),
        DecisionCardSource.ExtraDeck => player.With(extraDeck: player.ExtraDeck.Where(c => c != card)),
        DecisionCardSource.FaceUpExtraDeck => player.With(faceUpExtraDeck: player.FaceUpExtraDeck.Where(c => c != card)),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private DuelError CheckSpecialSummon(DuelState state, PlayerId playerId, CardInstance card, int zone,
        BattlePosition position, DecisionCardSource source, string? procedureId = null, CardInstance? lockExemptSource = null,
        PlayerId? sourcePlayer = null, bool allowFaceDown = false)
    {
        if (zone is < 0 or >= 5) return DuelError.InvalidMonsterZone;
        if (position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense) && !(allowFaceDown && position == BattlePosition.FaceDownDefense))
            return DuelError.InvalidBattlePosition;
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category != CardCategory.Monster)
            return DuelError.UnsupportedSpecialSummon;
        if (rule.CannotBeSpecialSummoned) return DuelError.UnsupportedSpecialSummon;
        if (IsSummonLocked(state, playerId, lockExemptSource)) return DuelError.SummonLocked;
        var player = state.GetPlayer(playerId);
        if (player.MonsterZones[zone] is not null) return DuelError.MonsterZoneOccupied;
        // A card may come from the other player's zone (e.g. their Graveyard); it keeps its owner.
        var origin = state.GetPlayer(sourcePlayer ?? playerId);
        if (card.Owner != origin.Id || !SpecialSourceCards(origin, source).Contains(card))
            return DuelError.SpecialSummonSourceUnavailable;
        if (specialSummonCards.TryGetValue(card.GameCardId, out var policy))
        {
            if (procedureId is null && !policy.EffectSources.HasFlag(SourceFlag(source)))
                return DuelError.UnsupportedSpecialSummon;
            if (policy.RequiredProperProcedureId is { } required &&
                !(source is DecisionCardSource.Hand or DecisionCardSource.ExtraDeck && procedureId == required) &&
                !(source is DecisionCardSource.Graveyard or DecisionCardSource.Banished or DecisionCardSource.FaceUpExtraDeck &&
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
        long? attemptId = null, string? properProcedureId = null, bool sourceAlreadyRemoved = false, int? timingGroup = null,
        IReadOnlyList<CardInstance>? xyzMaterials = null)
    {
        var player = next.GetPlayer(playerId);
        if (!sourceAlreadyRemoved) player = RemoveSpecialSource(player, card, source);
        var monsters = player.MonsterZones.ToArray();
        var monster = NewSummonedMonster(next, card, position);
        if (xyzMaterials is not null) monster = monster with { XyzMaterials = Array.AsReadOnly(xyzMaterials.ToArray()) };
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
        if (ProcedureCostCards(player, procedure, card, command.CostInstanceIds) is not { } costCards)
            return Reject(DuelError.InvalidSummonMaterials);
        List<DuelEvent> events = [];
        var paid = State;
        if (costCards.Length != 0)
        {
            var owner = paid.GetPlayer(player.Id);
            paid = paid.With(player: owner.With(graveyard: owner.Graveyard.Where(c => !costCards.Contains(c)), banished: owner.Banished.Concat(costCards)));
            foreach (var cost in costCards) events.Add(new CardBanishedFromGraveyard(player.Id, cost, procedure.ProcedureId));
        }
        string? proper = procedure.Source == DecisionCardSource.Hand ? procedure.ProcedureId : null;
        var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id,
            card, command.MonsterZone, Other(player.Id), IsSpecialSummon: true, Source: procedure.Source,
            Position: command.Position, ProcedureId: procedure.ProcedureId, ProperProcedureId: proper);
        var attempted = paid.With(player: RemoveSpecialSource(paid.GetPlayer(player.Id), card, procedure.Source)).WithSummon(pending);
        if (OpensSummonWindow(attempted, card.GameCardId))
            return Accept(attempted, [.. events, new SummonAttempted(pending), new SummonResponseRequested(pending)]);
        var next = PlaceSpecialSummon(paid, player.Id, card, command.MonsterZone, command.Position, procedure.Source,
            events, procedure.ProcedureId, properProcedureId: proper);
        return Accept(next, events.ToArray());
    }

    /// <summary>The chosen Graveyard cards, one per cost filter; null when the selection does not pay the cost exactly.</summary>
    private CardInstance[]? ProcedureCostCards(DuelPlayerState player, SpecialSummonProcedureProfile procedure, CardInstance summoned, IReadOnlyList<int>? selection)
    {
        var filters = procedure.GraveyardBanishCost;
        var ids = selection ?? [];
        if (ids.Count != filters.Count || ids.Distinct().Count() != ids.Count) return null;
        var cards = new CardInstance[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (player.Graveyard.SingleOrDefault(c => c.InstanceId == id) is not { } card || card == summoned) return null;
            cards[i] = card;
        }
        return SlotsMatch(cards, filters.Count, (card, slot) => MatchesFilter(card, filters[slot], inGraveyard: true)) ? cards : null;
    }

    /// <summary>True when every card fills a distinct slot it fits.</summary>
    private static bool SlotsMatch<T>(IReadOnlyList<T> items, int slots, Func<T, int, bool> fits)
    {
        if (items.Count != slots) return false;
        var used = new bool[slots];
        bool Place(int index)
        {
            if (index == items.Count) return true;
            for (int slot = 0; slot < slots; slot++)
                if (!used[slot] && fits(items[index], slot))
                {
                    used[slot] = true;
                    if (Place(index + 1)) return true;
                    used[slot] = false;
                }
            return false;
        }
        return Place(0);
    }

    /// <summary>Every Special Summon command worth offering a player now; the engine still validates each one.</summary>
    internal IEnumerable<(DuelCommand Command, int Card, int Zone, BattlePosition Position, int Tag)> SummonProcedureOptions(DuelState state, PlayerId playerId)
    {
        var player = state.GetPlayer(playerId);
        int tag = 0;
        int? free = Enumerable.Range(0, 5).Where(z => player.MonsterZones[z] is null).Select(z => (int?)z).FirstOrDefault();
        foreach (var procedure in specialSummonProcedures.Values.OrderBy(p => p.ProcedureId, StringComparer.Ordinal))
            foreach (var card in SpecialSourceCards(player, procedure.Source).Where(c => procedure.CardIds.Contains(c.GameCardId)))
                foreach (var cost in CostChoices(player, procedure.GraveyardBanishCost).Take(32))
                    if (free is int zone)
                        foreach (var position in procedure.Positions)
                            yield return (new SpecialSummonByProcedure(playerId, state.Revision, procedure.ProcedureId, card.InstanceId, zone, position, cost.Length == 0 ? null : cost), card.InstanceId, zone, position, tag++);
        var own = Enumerable.Range(0, 5).Where(z => player.MonsterZones[z] is not null)
            .Select(z => new MonsterBinding(playerId, z, player.MonsterZones[z]!.Card, player.MonsterZones[z]!.PlacementId)).ToArray();
        foreach (var profile in materialProcedures.Values.OrderBy(p => p.ProcedureId, StringComparer.Ordinal))
            foreach (var target in player.ExtraDeck.Where(c => profile.TargetCardIds.Contains(c.GameCardId)))
                foreach (var materials in Subsets(own, 5).Where(s => s.Length >= 2 || profile.MaterialCount == 1))
                {
                    int zone = free ?? materials[0].Zone;
                    foreach (var position in new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense })
                        yield return (new SpecialSummonWithMaterials(playerId, state.Revision, profile.ProcedureId, target.InstanceId, zone, materials, position), target.InstanceId, zone, position, tag++);
                }
    }

    private IEnumerable<int[]> CostChoices(DuelPlayerState player, IReadOnlyList<CardFilter> filters)
    {
        IEnumerable<int[]> Choose(int slot, int[] chosen)
        {
            if (slot == filters.Count) { yield return chosen; yield break; }
            foreach (var card in player.Graveyard.Where(c => !chosen.Contains(c.InstanceId) && MatchesFilter(c, filters[slot], inGraveyard: true)))
                foreach (var result in Choose(slot + 1, [.. chosen, card.InstanceId])) yield return result;
        }
        return Choose(0, []);
    }

    /// <summary>Non-empty subsets of up to <paramref name="limit"/> items, smallest first.</summary>
    internal static IEnumerable<T[]> Subsets<T>(IReadOnlyList<T> items, int limit)
    {
        IEnumerable<T[]> Pick(int start, int count)
        {
            if (count == 0) { yield return []; yield break; }
            for (int i = start; i <= items.Count - count; i++)
                foreach (var rest in Pick(i + 1, count - 1)) yield return [items[i], .. rest];
        }
        for (int size = 1; size <= Math.Min(limit, items.Count); size++)
            foreach (var subset in Pick(0, size)) yield return subset;
    }
}
