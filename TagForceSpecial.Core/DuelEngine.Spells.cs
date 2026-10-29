namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult Activate(int instanceId, int zone)
    {
        if (State.Phase != DuelPhase.Main1) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (activationContracts.TryGetValue(card.GameCardId, out var contract)) return ActivateContract(card, zone, contract);
        if (!spellEffects.TryGetValue(card.GameCardId, out var effect)) return Reject(DuelError.UnsupportedSpellEffect);
        if (player.SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (Enumerable.Range(0, effect.Decisions.Steps.Count).Any(i => CreateDecision(effect.Decisions, i, []) is null))
            return Reject(DuelError.DecisionUnavailable);
        var decision = CreateDecision(effect.Decisions, 0, [])!;
        var activation = new PendingSpellActivation(player.Id, effect.EffectId, card, zone);
        var slots = player.SpellTrapFields.ToArray();
        slots[zone] = new(card, IsFaceUp: true, PlacementId: checked(State.Revision + 1));
        var next = State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId), spellTraps: slots))
            .WithActivation(activation).WithDecision(decision);
        return Accept(next, new SpellActivated(player.Id, effect.EffectId, card, zone), new DecisionRequested(decision));
    }

    private DuelActionResult ResolveSpell(DecisionSequenceResult result)
    {
        var activation = State.PendingSpellActivation!;
        var player = State.GetPlayer(State.ActivePlayer);
        var effect = spellEffects[activation.Spell.GameCardId];
        if (result.Answers.Count != 3 || result.Answers[0].Kind != DecisionKind.Card ||
            result.Answers[1].Position != BattlePosition.FaceUpAttack || result.Answers[2].MonsterZone is not int zone ||
            zone is < 0 or >= 5 || activation.Player != player.Id || activation.EffectId != effect.EffectId)
            return Reject(DuelError.InvalidEffectState);
        var card = player.Deck.SingleOrDefault(c => c.InstanceId == result.Answers[0].CardInstanceId);
        var spell = player.SpellTrapFields[activation.SpellTrapZone];
        if (card is null || card.Owner != player.Id || !effect.EligibleMonsterIds.Contains(card.GameCardId) ||
            spell is null || !spell.IsFaceUp || spell.Card != activation.Spell ||
            player.MonsterZones[zone] is not null)
            return Reject(DuelError.InvalidEffectState);
        var monsters = player.MonsterZones.ToArray();
        var summoned = new FieldMonster(card, BattlePosition.FaceUpAttack, SummonedOnTurn: State.TurnNumber, PlacementId: checked(State.Revision + 1));
        monsters[zone] = summoned;
        var slots = player.SpellTrapFields.ToArray();
        slots[activation.SpellTrapZone] = null;
        var next = State.With(player: player.With(deck: player.Deck.Where(c => c.InstanceId != card.InstanceId),
            monsters: monsters, spellTraps: slots, graveyard: player.Graveyard.Append(activation.Spell)))
            .WithDecision(null, result, researchSliceComplete: !effect.BanishAtEndPhase).WithActivation(null);
        List<DuelEvent> events = [new MonsterSpecialSummoned(player.Id, summoned, zone, DecisionCardSource.Deck),
            new SpellSentToGraveyard(player.Id, activation.Spell, activation.SpellTrapZone),
            new EffectResolved(player.Id, effect.EffectId, activation.Spell)];
        if (effect.BanishAtEndPhase)
        {
            var delayed = new PendingEndPhaseBanishment(player.Id, effect.EffectId, activation.Spell,
                card, zone, State.TurnNumber);
            next = next.WithEndPhaseBanishments([delayed]);
            events.Add(new EndPhaseBanishmentScheduled(delayed));
        }
        return Accept(next, events.ToArray());
    }

    private DuelActionResult ResolveEndPhaseBanishment()
    {
        // The placement may survive or have been destroyed by supported basic battle.
        // Other movement, resets, control changes and effect exceptions are unsupported.
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2))
            return Reject(DuelError.InvalidPhaseTransition);
        if (State.PendingEndPhaseBanishments.Count != 1)
            return Reject(DuelError.InvalidEffectState);
        var delayed = State.PendingEndPhaseBanishments[0];
        var player = State.GetPlayer(State.ActivePlayer);
        if (delayed.Player != player.Id || delayed.DueTurn != State.TurnNumber ||
            delayed.MonsterZone is < 0 or >= 5)
            return Reject(DuelError.InvalidEffectState);
        var monster = player.MonsterZones[delayed.MonsterZone];
        if (monster is null && player.Graveyard.Contains(delayed.Monster))
        {
            var expired = State.With(phase: DuelPhase.End).WithEndPhaseBanishments([])
                .WithDecision(null, State.DecisionResult, researchSliceComplete: true);
            return Accept(expired, new PhaseChanged(State.Phase, DuelPhase.End),
                new EndPhaseBanishmentExpired(player.Id, delayed.Monster, delayed.EffectId));
        }
        if (monster is not { Position: BattlePosition.FaceUpAttack } || monster.Card != delayed.Monster ||
            monster.SummonedOnTurn != delayed.DueTurn)
            return Reject(DuelError.InvalidEffectState);
        var zones = player.MonsterZones.ToArray();
        zones[delayed.MonsterZone] = null;
        var next = State.With(player: player.With(monsters: zones, banished: player.Banished.Append(monster.Card)),
                phase: DuelPhase.End)
            .WithEndPhaseBanishments([]).WithDecision(null, State.DecisionResult, researchSliceComplete: true);
        return Accept(next, new PhaseChanged(State.Phase, DuelPhase.End),
            new MonsterBanished(player.Id, monster.Card, delayed.MonsterZone, delayed.EffectId));
    }
}
