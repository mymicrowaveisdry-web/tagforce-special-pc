using System.Text.Json;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Invented profiles verify reusable activation commitments and use limits.</summary>
public sealed class TriggerCommitmentTests
{
    private static TriggerEffectProfile Targeted(int cost = 100, IEnumerable<uint>? discards = null) =>
        new("cost-and-target", A, MonsterEventKind.NormalSummoned, EffectRelation.Self, TriggerRequirement.Mandatory,
            TriggerOperation.DestroyTargetMonster, lifePointCost: cost, discardCardIds: discards,
            target: new(EffectRelation.Opponent), turnLimit: TriggerTurnLimit.SharedPerPlayer);

    private static DuelEngine Setup(TriggerEffectProfile profile, IEnumerable<ChainResponseProfile>? responses = null)
    {
        uint[] deck = [A, A, Filler, Trap, SummonSpell, .. Enumerable.Repeat(Filler, 35)];
        var engine = Engine(triggers: [profile], responses: responses, playerOneDeck: deck);
        EnterMain(engine); NextMain(engine); Set(engine, Trap); Summon(engine, B); NextMain(engine);
        Summon(engine, A); Order(engine);
        return engine;
    }

    [Fact]
    public void CostThenTargetCommitBeforeResponsesAndSnapshotsRemainImmutable()
    {
        var engine = Setup(Targeted(discards: [Filler]));
        var cost = engine.State.EventEffects.PendingCommitment!;
        Assert.Equal(TriggerCommitmentKind.DiscardCost, cost.Kind);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Null(engine.State.PendingChain);
        Assert.Empty(engine.State.EventEffects.UsedTriggers);
        string saved = JsonSerializer.Serialize(engine.State);
        var before = engine.State;
        var card = cost.CardOptions[0];
        var paid = Ok(engine, new ChooseTriggerCostCard(PlayerId.One, cost.DecisionId, card.InstanceId));
        Assert.Single(paid.Events.OfType<TriggerCostPaid>());
        Assert.Equal(7900, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Contains(card, engine.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(saved, JsonSerializer.Serialize(before));
        Assert.Empty(engine.State.EventEffects.UsedTriggers);
        var target = engine.State.EventEffects.PendingCommitment!;
        Assert.Equal(TriggerCommitmentKind.MonsterTarget, target.Kind);
        Assert.Equal(card, target.Trigger.DiscardedCost);
        var selected = Assert.Single(target.TargetOptions);
        Ok(engine, new ChooseTriggerMonsterTarget(PlayerId.One, target.DecisionId, selected.Player, selected.Zone, selected.Card.InstanceId));
        var link = Assert.Single(engine.State.PendingChain!.Links);
        Assert.Equal(selected, link.Trigger!.Target);
        Assert.Equal(card, link.Activation.DiscardedCost);
        Assert.Equal(100, link.Trigger.PaidLifePoints);
        Assert.Single(engine.State.EventEffects.UsedTriggers);
        Assert.Null(engine.State.EventEffects.PendingCommitment);
        Drain(engine);
        Assert.Null(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Assert.Equal(7900, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void InvalidCostAndTargetAnswersCannotPayTwiceOrAlterState()
    {
        var engine = Setup(Targeted(discards: [Filler]));
        var c = engine.State.EventEffects.PendingCommitment!;
        int card = c.CardOptions[0].InstanceId;
        Reject(engine, new ChooseTriggerCostCard(PlayerId.Two, c.DecisionId, card), DuelError.NotTriggerPlayer);
        Reject(engine, new ChooseTriggerCostCard(PlayerId.One, c.DecisionId - 1, card), DuelError.StaleTriggerDecision);
        Reject(engine, new ChooseTriggerCostCard(PlayerId.One, c.DecisionId, int.MaxValue), DuelError.InvalidTriggerCommitment);
        Reject(engine, new ChooseTriggerMonsterTarget(PlayerId.One, c.DecisionId, PlayerId.Two, 0, 41), DuelError.WrongTriggerCommitment);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.TriggerCommitmentPending);
        Ok(engine, new ChooseTriggerCostCard(PlayerId.One, c.DecisionId, card));
        var t = engine.State.EventEffects.PendingCommitment!;
        Reject(engine, new ChooseTriggerCostCard(PlayerId.One, c.DecisionId, card), DuelError.StaleTriggerDecision);
        Reject(engine, new ChooseTriggerCostCard(PlayerId.One, t.DecisionId, card), DuelError.WrongTriggerCommitment);
        Reject(engine, new ChooseTriggerMonsterTarget(PlayerId.One, t.DecisionId, PlayerId.One, 0, card), DuelError.InvalidTriggerCommitment);
        Reject(engine, new PassChainResponse(PlayerId.Two, t.DecisionId), DuelError.TriggerCommitmentPending);
        Assert.Equal(7900, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Drain(engine);
        Reject(engine, new ChooseTriggerCostCard(PlayerId.One, t.DecisionId, card), DuelError.NoPendingTriggerCommitment);
    }

    [Theory]
    [InlineData(ChainOperation.NegateActivation)]
    [InlineData(ChainOperation.NegateEffect)]
    public void NegationDoesNotRefundCommittedCostsOrOncePerTurnUse(ChainOperation operation)
    {
        var profile = new TriggerEffectProfile("paid-gain", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 500,
            lifePointCost: 100, discardCardIds: [Filler], turnLimit: TriggerTurnLimit.SharedPerPlayer);
        var engine = Setup(profile, [new("negate", Trap, operation, ChainSpellSpeed.Two, respondsToCategory: CardCategory.Monster)]);
        var c = engine.State.EventEffects.PendingCommitment!;
        var cost = c.CardOptions[0];
        Ok(engine, new ChooseTriggerCostCard(c.Player, c.DecisionId, cost.InstanceId));
        var chain = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(PlayerId.Two, chain.DecisionId, 0));
        var events = Drain(engine);
        Assert.Single(events.OfType<ChainLinkSkipped>());
        Assert.Equal(7900, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Contains(cost, engine.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Single(engine.State.EventEffects.UsedTriggers);
    }

    [Theory]
    [InlineData(8000, true)]
    [InlineData(7999, false)]
    public void CostsCannotSpendAllLifePointsAndUnaffordableEffectsConsumeNoUse(int cost, bool unavailable)
    {
        var profile = new TriggerEffectProfile("lp", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 1,
            lifePointCost: cost, turnLimit: TriggerTurnLimit.SharedPerPlayer);
        var engine = Engine(triggers: [profile]); EnterMain(engine); var summon = Summon(engine, A);
        if (unavailable)
        {
            Assert.Single(summon.Events.OfType<TriggerUnavailable>());
            Assert.Null(engine.State.EventEffects.PendingTriggers);
            Assert.Empty(engine.State.EventEffects.UsedTriggers);
            Assert.Equal(8000, engine.State.GetPlayer(PlayerId.One).LifePoints);
        }
        else
        {
            Drain(engine);
            Assert.Equal(2, engine.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Single(engine.State.EventEffects.UsedTriggers);
        }
    }

    [Fact]
    public void MissingTargetOrEligibleDiscardIsFilteredBeforeAnyCostIsPaid()
    {
        var target = Engine(triggers: [Targeted()]); EnterMain(target); var summon = Summon(target, A);
        Assert.Null(target.State.EventEffects.PendingTriggers);
        Assert.Single(summon.Events.OfType<TriggerUnavailable>());
        Assert.Equal(8000, target.State.GetPlayer(PlayerId.One).LifePoints);
        var discard = Engine(triggers: [new("discard", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100, discardCardIds: [Filler])]);
        EnterMain(discard); Summon(discard, A);
        Assert.Null(discard.State.EventEffects.PendingTriggers);
        Assert.Empty(discard.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void SharedUseChoosesOneCopyAndResetsOnTheNextTurn()
    {
        var profile = new TriggerEffectProfile("shared", A, MonsterEventKind.NormalSummoned, EffectRelation.Controller,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100,
            turnLimit: TriggerTurnLimit.SharedPerPlayer);
        var engine = Engine(triggers: [profile]); EnterMain(engine); Summon(engine, A); Drain(engine);
        Assert.Single(engine.State.EventEffects.UsedTriggers);
        NextMain(engine); Assert.Empty(engine.State.EventEffects.UsedTriggers); NextMain(engine);
        Summon(engine, A, 1);
        Assert.Equal(2, engine.State.EventEffects.PendingTriggers!.Options.Count);
        var chosen = Order(engine, reverse: true);
        Assert.Single(chosen.Events.OfType<TriggerUnavailable>());
        Assert.Equal(1, Assert.Single(engine.State.PendingChain!.Links).Trigger!.Source.Zone);
        Drain(engine);
        Assert.Equal(8200, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Single(engine.State.EventEffects.UsedTriggers);
    }

    [Theory]
    [InlineData(TriggerTurnLimit.PerPlacement, 8200, 2)]
    [InlineData(TriggerTurnLimit.SharedPerPlayer, 8100, 1)]
    public void ASharedKeyAcrossEffectsDistinguishesPerPlacementAndPerPlayerUse(TriggerTurnLimit limit, int lp, int uses)
    {
        TriggerEffectProfile Profile(string id, MonsterEventKind kind) => new(id, A, kind, EffectRelation.Controller,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100, turnLimit: limit, limitKey: "shared-key");
        var engine = Engine(triggers: [Profile("normal", MonsterEventKind.NormalSummoned), Profile("special", MonsterEventKind.SpecialSummoned)]);
        EnterMain(engine); Summon(engine, A); Drain(engine);
        SummonFromDeck(engine); Drain(engine);
        Assert.Equal(lp, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(uses, engine.State.EventEffects.UsedTriggers.Count);
    }

    internal static void SummonFromDeck(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ActivateSpell(actor, engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == SummonSpell).InstanceId, 4));
        var p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
        p = engine.State.PendingDecision!; Ok(engine, new ChooseDecisionPosition(p.Player, p.DecisionId, BattlePosition.FaceUpAttack));
        p = engine.State.PendingDecision!; Ok(engine, new ChooseDecisionZone(p.Player, p.DecisionId, 1));
    }

    [Fact]
    public void MultipleMandatoryEffectsCompetingForOneDiscardDoNotStrandTheBatch()
    {
        TriggerEffectProfile Profile(string id) => new(id, A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100, discardCardIds: [Trap]);
        var engine = Engine(triggers: [Profile("first"), Profile("second")]);
        EnterMain(engine); Summon(engine, A); Order(engine);
        var pending = engine.State.EventEffects.PendingCommitment!;
        Ok(engine, new ChooseTriggerCostCard(pending.Player, pending.DecisionId, pending.CardOptions[0].InstanceId));
        Assert.Single(engine.State.PendingChain!.Links);
        Assert.Null(engine.State.EventEffects.PendingCommitment);
        Drain(engine); Assert.Equal(8100, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Theory]
    [InlineData(true, false, 7800)]
    [InlineData(false, false, 7850)]
    [InlineData(true, true, 7800)]
    [InlineData(false, true, 7850)]
    public void MissingTargetsDoNotRetargetOrRefundCostsAndDependentFollowUpsRequireSuccess(bool dependent, bool simultaneous, int lp)
    {
        TriggerEffectStep[] steps = simultaneous
            ? [new([new(TriggerOperation.DestroyTargetMonster),
                new(TriggerOperation.GainLifePoints, lifePointGain: 50, requiresPreviousActionSuccess: dependent)])]
            : [new([new(TriggerOperation.DestroyTargetMonster)]),
                new([new(TriggerOperation.GainLifePoints, lifePointGain: 50)], requiresPreviousStepSuccess: dependent)];
        var lower = new TriggerEffectProfile("a-lower", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, steps, lifePointCost: 100, target: new(EffectRelation.Opponent));
        var higher = new TriggerEffectProfile("b-higher", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerOperation.DestroyTargetMonster, lifePointCost: 100, target: new(EffectRelation.Opponent));
        var engine = Engine(triggers: [lower, higher]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); NextMain(engine); Summon(engine, B, 1);
        NextMain(engine); Summon(engine, A); Order(engine);
        while (engine.State.EventEffects.PendingCommitment is { } p)
        {
            var target = p.TargetOptions.Single(t => t.Zone == 0);
            Ok(engine, new ChooseTriggerMonsterTarget(p.Player, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        }
        var bindings = engine.State.PendingChain!.Links.Select(l => l.Trigger!.Target).ToArray();
        Assert.Equal(bindings[0], bindings[1]);
        var events = Drain(engine);
        Assert.Single(events.OfType<TriggerSubjectUnavailable>());
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[1]);
        Assert.Null(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Assert.Equal(lp, engine.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void ANonTurnPlayerOwnsTheirTriggerCostAndTargetPrompts()
    {
        var profile = new TriggerEffectProfile("opponent-trigger", B, MonsterEventKind.NormalSummoned, EffectRelation.Opponent,
            TriggerRequirement.Mandatory, TriggerOperation.DestroyTargetMonster, discardCardIds: [Filler], target: new(EffectRelation.Opponent));
        var engine = Engine(triggers: [profile]);
        EnterMain(engine); NextMain(engine); Summon(engine, B); NextMain(engine); Summon(engine, A); Order(engine);
        Assert.Equal(PlayerId.One, engine.State.ActivePlayer);
        var cost = engine.State.EventEffects.PendingCommitment!;
        Assert.Equal(PlayerId.Two, cost.Player);
        Reject(engine, new ChooseTriggerCostCard(PlayerId.One, cost.DecisionId, cost.CardOptions[0].InstanceId), DuelError.NotTriggerPlayer);
        Ok(engine, new ChooseTriggerCostCard(cost.Player, cost.DecisionId, cost.CardOptions[0].InstanceId));
        var target = engine.State.EventEffects.PendingCommitment!;
        Assert.Equal(PlayerId.Two, target.Player);
        Assert.Equal(PlayerId.One, Assert.Single(target.TargetOptions).Player);
        Drain(engine);
        Assert.Null(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard);
    }

    [Fact]
    public void RegistrationCopiesCostAndTargetAllowlistsAndRejectsUnknownCatalogReferences()
    {
        uint[] costs = [Filler]; uint[] targets = [B];
        var profile = new TriggerEffectProfile("copied", A, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.DestroyTargetMonster, discardCardIds: costs,
            target: new(EffectRelation.Opponent, targets));
        costs[0] = Trap; targets[0] = A;
        Assert.Equal(Filler, Assert.Single(profile.DiscardCardIds));
        Assert.Equal(B, Assert.Single(profile.Target!.AllowedCardIds));
        Assert.Throws<ArgumentException>(() => Engine(triggers: [new("unknown-cost", A, MonsterEventKind.NormalSummoned,
            EffectRelation.Self, TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 1, discardCardIds: [17])]));
        Assert.Throws<ArgumentException>(() => Engine(triggers: [new("unknown-target", A, MonsterEventKind.NormalSummoned,
            EffectRelation.Self, TriggerRequirement.Optional, TriggerOperation.DestroyTargetMonster, target: new(EffectRelation.Any, [17]))]));
    }
}
