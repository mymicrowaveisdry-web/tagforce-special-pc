namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelState RemoveFieldSpell(DuelState next, SpellTrapTarget target, DuelEvent cause,
        List<DuelEvent> events, bool activationNegated = false)
    {
        var player = next.GetPlayer(target.Player);
        var zones = (target.IsPendulumZone ? player.PendulumZones : player.SpellTrapFields).ToArray();
        zones[target.Zone] = null;
        next = next.With(player: target.IsPendulumZone ? player.With(pendulumZones: zones) : player.With(spellTraps: zones));
        return SendFieldCardToGraveyard(next, target.Card, cause, events, activationNegated);
    }
    // The single field-departure path. Caller removes the field placement. Destination and event semantics are
    // chosen together, never inferred from a final GY diff. A Token goes to no zone: its cause event (e.g.
    // MonsterDestroyed, MonsterTributed) is still emitted, marked Removed, then TokenRemoved.
    private DuelState SendFieldCardToGraveyard(DuelState next, CardInstance card, DuelEvent cause,
        List<DuelEvent> events, bool activationNegated = false)
    {
        if (IsTokenId(card.GameCardId))
        {
            events.Add(cause with { FieldDestination = FieldCardDestination.Removed });
            events.Add(new TokenRemoved(card.Owner, card) { TimingGroup = cause.TimingGroup, FieldDestination = FieldCardDestination.Removed });
            return next;
        }
        bool extra = !activationNegated && pendulumCards.ContainsKey(card.GameCardId);
        var owner = next.GetPlayer(card.Owner);
        next = next.With(player: extra ? owner.With(faceUpExtraDeck: owner.FaceUpExtraDeck.Append(card))
            : owner.With(graveyard: owner.Graveyard.Append(card)));
        var destination = extra ? FieldCardDestination.FaceUpExtraDeck : FieldCardDestination.Graveyard;
        DuelEvent routed = extra ? cause switch
        {
            SummonMaterialSentToGraveyard m => new SummonMaterialSentToFaceUpExtraDeck(m.Material, m.ProcedureId) { TimingGroup = m.TimingGroup },
            FusionMaterialSentToGraveyard m => new FusionMaterialSentToFaceUpExtraDeck(m.Material, m.EffectId) { TimingGroup = m.TimingGroup },
            _ => cause
        } : cause;
        events.Add(routed with { FieldDestination = destination });
        if (extra) events.Add(new CardSentToFaceUpExtraDeck(card) { TimingGroup = cause.TimingGroup, FieldDestination = destination });
        return next;
    }

    private DuelActionResult ActivateScale(ActivatePendulumScale command)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (command.PendulumZone is < 0 or > 1) return Reject(DuelError.InvalidPendulumZone);
        var player = State.GetPlayer(command.Player);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!pendulumCards.ContainsKey(card.GameCardId)) return Reject(DuelError.UnsupportedPendulumCard);
        if (player.PendulumZones[command.PendulumZone] is not null) return Reject(DuelError.PendulumZoneOccupied);
        var zones = player.PendulumZones.ToArray(); long placement = checked(State.Revision + 1);
        zones[command.PendulumZone] = new(card, true, placement);
        var receipt = new CommittedActivation(player.Id, "pendulum-scale", card, command.PendulumZone, null, null);
        var link = new ChainLink(1, receipt, placement, ChainOperation.ActivatePendulumScale, IsPendulumScale: true);
        var chain = new PendingChain(placement, [link], Other(player.Id));
        return Accept(State.With(player: player.With(hand: player.Hand.Where(c => c != card), pendulumZones: zones))
            .WithCommittedActivation(receipt).WithChain(chain), new PendulumScaleActivated(player.Id, card, command.PendulumZone),
            new ActivationCommitted(receipt), new ChainLinkAdded(link), new ChainResponseRequested(chain.Responder, chain.DecisionId));
    }

    private DuelActionResult ExecutePendulumSummon(PendulumSummon command)
    {
        if (command.ExpectedRevision != State.Revision) return Reject(DuelError.StaleSummonSelection);
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(command.Player);
        if (player.LastPendulumSummonTurn == State.TurnNumber) return Reject(DuelError.PendulumSummonLimitReached);
        if (player.PendulumZones[0] is not { } left || player.PendulumZones[1] is not { } right)
            return Reject(DuelError.PendulumScalesUnavailable);
        uint first = pendulumCards[left.Card.GameCardId].LeftScale, second = pendulumCards[right.Card.GameCardId].RightScale;
        uint low = Math.Min(first, second), high = Math.Max(first, second);
        var choices = command.Selections;
        if (choices.Count is < 1 or > 5 || choices.Any(c => c is null) ||
            choices.Select(c => c.CardInstanceId).Distinct().Count() != choices.Count ||
            choices.Select(c => c.MonsterZone).Distinct().Count() != choices.Count)
            return Reject(DuelError.InvalidSummonMaterials);
        List<PendingSummon> group = [];
        foreach (var choice in choices.OrderBy(c => c.MonsterZone))
        {
            if (choice.Source is not (DecisionCardSource.Hand or DecisionCardSource.FaceUpExtraDeck)) return Reject(DuelError.SpecialSummonSourceUnavailable);
            var card = SpecialSourceCards(player, choice.Source).SingleOrDefault(c => c.InstanceId == choice.CardInstanceId);
            if (card is null) return Reject(DuelError.SpecialSummonSourceUnavailable);
            if (!rules.TryGetValue(card.GameCardId, out var rule) || !rule.AllowsPendulumSummon ||
                choice.Source == DecisionCardSource.FaceUpExtraDeck && !pendulumCards.ContainsKey(card.GameCardId) || IsXyzCard(card))
                return Reject(DuelError.UnsupportedPendulumCard);
            uint level = catalog[card.GameCardId].Statistics.Level;
            if (level == 0 || level <= low || level >= high) return Reject(DuelError.UnsupportedMonsterLevel);
            var error = CheckSpecialSummon(State, player.Id, card, choice.MonsterZone, choice.Position, choice.Source, "pendulum");
            if (error != DuelError.None) return Reject(error);
            group.Add(new(checked(State.Revision + 1), checked(State.Revision + 1), player.Id, card, choice.MonsterZone,
                Other(player.Id), IsSpecialSummon: true, Source: choice.Source, Position: choice.Position, ProcedureId: "pendulum"));
        }
        var next = State;
        foreach (var entry in group) next = next.With(player: RemoveSpecialSource(next.GetPlayer(player.Id), entry.Card, entry.Source));
        next = next.With(player: next.GetPlayer(player.Id).With(lastPendulumSummonTurn: State.TurnNumber));
        var pending = group[0] with { PendulumGroup = Array.AsReadOnly(group.ToArray()) };
        var events = group.Select(p => (DuelEvent)new SummonAttempted(p) { TimingGroup = 1 }).ToList();
        events.Add(new SummonResponseRequested(pending));
        return Accept(next.WithSummon(pending), events.ToArray());
    }
}
