using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic battle permissions, attack expenditure, replay interruptions and mandatory attacks.</summary>
public sealed class BattleAbilityTests
{
    private static BattleAbilityProfile Self(BattleAbilities abilities, uint card = A, string id = "battle") => new(id, card, abilities);
    private static EffectDefinition Summoning() => ResponseProbe.SummonFromHand("new-defender", Summon, BattleTiming.AttackDeclaration, B);

    private static void Begin(DuelEngine engine)
    {
        if (engine.UsesStagedBattle) Ready(engine);
    }

    private static void Attack(DuelEngine engine, int? target)
    {
        if (engine.UsesStagedBattle) Close(engine);
        Declare(engine, target);
        if (engine.UsesStagedBattle) FinishAttack(engine);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void PiercingDamagesThroughEitherDefensePositionInBothBattlePaths(bool staged, bool faceDown)
    {
        var e = Engine(attack: 2500, faceDown: faceDown, defenderFaceUpDefense: !faceDown, staged: staged,
            battleAbilities: [Self(new(Piercing: true))]);
        Begin(e);
        if (staged)
        {
            Declare(e); ToDamage(e, DamageStep.Calculation);
            Assert.Equal(BattlePosition.FaceUpDefense, e.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Position);
            Close(e);
            Assert.Equal(500, Assert.Single(Advance(e).Events.OfType<BattleDamageDealt>()).Damage);
            FinishAttack(e);
        }
        else Attack(e, 0);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Null(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PiercingDoesNotReverseDamageFromHigherDefense(bool staged)
    {
        var e = Engine(faceDown: true, staged: staged, battleAbilities: [Self(new(Piercing: true))]);
        Begin(e); Attack(e, 0);
        Assert.Equal(7800, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.NotNull(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectPermissionBypassesMonstersButCannotBypassAnAttackLock(bool staged)
    {
        var e = Engine(staged: staged, battleAbilities: [Self(new(DirectAttack: true))]);
        Begin(e); Attack(e, null);
        Assert.Equal(6200, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.NotNull(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        var locked = Engine(staged: staged, battleAbilities: [Self(new(DirectAttack: true))],
            locks: [new("no-attack", A, new(LockedAction.Attack, Scope: LockScope.Self))]);
        Begin(locked); Reject(locked, new DeclareAttack(PlayerId.One, 0), DuelError.LockedByEffect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultipleAttackSourcesUseTheHighestCountAndPreviewsSpendNothing(bool staged)
    {
        var e = Engine(defender: false, staged: staged,
            battleAbilities: [Self(new(AttacksPerBattlePhase: 2)), Self(new(AttacksPerBattlePhase: 2), id: "second-source")]);
        Begin(e);
        var before = e.State;
        Assert.Equal(DuelError.None, e.CheckCommand(new DeclareAttack(PlayerId.One, 0)));
        Assert.Same(before, e.State);
        Attack(e, null); Attack(e, null);
        Assert.Equal(4400, e.State.GetPlayer(PlayerId.Two).LifePoints);
        if (staged) Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.AlreadyAttacked);
        Assert.Equal(0, before.GetPlayer(PlayerId.One).MonsterZones[0]!.LastAttackTurn);
    }

    [Fact]
    public void NegatingAnAttackConsumesOneOfTwoAttacks()
    {
        var e = Engine(battleAbilities: [Self(new(AttacksPerBattlePhase: 2))], profiles: [ResponseProbe.NegateAttack("negate", Negate)]);
        Ready(e); Declare(e); Respond(e, 0); DrainEffects(e); FinishAttack(e); Close(e);
        Declare(e); FinishAttack(e); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 0), DuelError.AlreadyAttacked);
        Assert.Equal(7400, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ReplaySpendsNoAdditionalAttackAndCanStillChooseADirectAttack()
    {
        var e = Engine(defender: false, battleAbilities: [Self(new(DirectAttack: true, AttacksPerBattlePhase: 2))], profiles: [Summoning()]);
        Ready(e); Declare(e, null);
        int card = e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(e, 2, card, 1); DrainEffects(e); Close(e);
        var replay = Ok(e, new ChooseAttackReplay(PlayerId.One, e.State.Battle!.Window.DecisionId));
        Assert.Empty(replay.Events.OfType<AttackDeclared>());
        FinishAttack(e); Attack(e, null); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.AlreadyAttacked);
        Assert.Equal(4400, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AttackAllVisitsEachMonsterOnceAndRemembersTheOriginalReplayTarget()
    {
        var e = Engine(battleAbilities: [Self(new(AttackAll: true))], profiles: [Summoning()]);
        Ready(e); Declare(e);
        int card = e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(e, 2, card, 1); DrainEffects(e); Close(e);
        Ok(e, new ChooseAttackReplay(PlayerId.One, e.State.Battle!.Window.DecisionId, 1));
        FinishAttack(e); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 0), DuelError.AlreadyAttacked);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 1), DuelError.AlreadyAttacked);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.AlreadyAttacked);
    }

    [Fact]
    public void AttackAllCanAttackANewMonsterButCannotRepeatAnEarlierTarget()
    {
        var e = Engine(faceDown: true, battleAbilities: [Self(new(AttackAll: true))], profiles: [Summoning()]);
        Ready(e); Declare(e);
        int card = e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(e, 2, card, 1); DrainEffects(e); Close(e);
        Ok(e, new ChooseAttackReplay(PlayerId.One, e.State.Battle!.Window.DecisionId, 0));
        FinishAttack(e); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 0), DuelError.AlreadyAttacked);
        Attack(e, 1); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 1), DuelError.AlreadyAttacked);
    }

    [Fact]
    public void ADeclaredDirectAttackPreventsAttackAllEvenAfterReplayToAMonster()
    {
        var e = Engine(defender: false, battleAbilities: [Self(new(AttackAll: true))], profiles: [Summoning()]);
        Ready(e); Declare(e, null);
        int card = e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == B).InstanceId;
        Respond(e, 2, card, 1); DrainEffects(e); Close(e);
        Ok(e, new ChooseAttackReplay(PlayerId.One, e.State.Battle!.Window.DecisionId, 1)); FinishAttack(e); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 1), DuelError.AlreadyAttacked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MustAttackPreventsSkippingBattleAndLeavingBeforeOneLegalAttack(bool staged)
    {
        var e = Engine(defender: false, staged: staged, enterBattle: false, battleAbilities: [Self(new(MustAttack: true, AttacksPerBattlePhase: 2))]);
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.MustAttack);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle)); Begin(e);
        if (staged) Reject(e, new AdvanceBattle(PlayerId.One, e.State.Battle!.Window.DecisionId), DuelError.MustAttack);
        else Reject(e, new ChangePhase(PlayerId.One, DuelPhase.Main2), DuelError.MustAttack);
        Attack(e, null);
        if (staged) { Close(e); Advance(e); Close(e); }
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main2));
    }

    [Fact]
    public void AttackLocksTakePriorityOverMustAttack()
    {
        var e = Engine(battleAbilities: [Self(new(MustAttack: true))],
            locks: [new("cannot-attack", A, new(LockedAction.Attack, Scope: LockScope.Self))]);
        Ready(e); Advance(e);
        Assert.Equal(BattleStep.End, e.State.Battle!.Step);
    }

    [Fact]
    public void ContinuousNegationRemovesDirectAndMultipleAttackPermissions()
    {
        var e = Engine(defenderFaceUpDefense: true, battleAbilities: [Self(new(DirectAttack: true, AttacksPerBattlePhase: 2))],
            negations: [new("negate-battle-source", B, EffectRelation.Opponent, [A])]);
        Ready(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.DirectAttackBlocked);
        Attack(e, 0); Close(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0, 0), DuelError.AlreadyAttacked);
    }

    [Fact]
    public void LosingDirectPermissionRequestsReplayWithoutChangingTheDefenderRoster()
    {
        var drain = new EffectDefinition("drain", Negate, EffectKind.ContinuousTrap, [],
            [new BattleTimingCondition(BattleTiming.AttackDeclaration)], negation: LastingNegation.FaceUpMonsters);
        var e = Engine(battleAbilities: [Self(new(DirectAttack: true))], profiles: [drain]);
        Ready(e); Declare(e, null); Respond(e, 0); DrainEffects(e);
        Assert.True(e.State.Battle!.Attack!.ReplayRequired);
        Close(e);
        Reject(e, new ChooseAttackReplay(PlayerId.One, e.State.Battle.Window.DecisionId), DuelError.DirectAttackBlocked);
        Ok(e, new ChooseAttackReplay(PlayerId.One, e.State.Battle.Window.DecisionId, 0));
        FinishAttack(e);
        Assert.Equal(7400, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void PiercingIsReadAtCalculationAfterItsSourceIsNegated()
    {
        var drain = new EffectDefinition("drain", Negate, EffectKind.ContinuousTrap, [],
            [new BattleTimingCondition(BattleTiming.BeforeCalculation)], negation: LastingNegation.FaceUpMonsters);
        var e = Engine(attack: 2500, faceDown: true, battleAbilities: [Self(new(Piercing: true))], profiles: [drain]);
        Ready(e); Declare(e); ToDamage(e, DamageStep.BeforeCalculation);
        var w = e.State.Battle!.Window;
        Ok(e, new PassBattleResponse(w.Responder, w.DecisionId));
        Respond(e, 0); DrainEffects(e); FinishAttack(e);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Null(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void DefensePositionAndNegationRemoveTheMandatoryAttackRequirement()
    {
        var e = Engine(enterBattle: false, battleAbilities: [Self(new(MustAttack: true))]);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense));
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        var negated = Engine(enterBattle: false, battleAbilities: [Self(new(MustAttack: true))],
            negations: [new("negate-must-attack", B, EffectRelation.Opponent, [A])]);
        Ok(negated, new ChangePhase(PlayerId.One, DuelPhase.End));
    }

    [Fact]
    public void BattleProfileRegistrationRejectsUnknownSourcesBadScopesAndInvalidCounts()
    {
        Assert.Throws<ArgumentException>(() => Engine(battleAbilities: [Self(new(AttacksPerBattlePhase: 0))]));
        Assert.Throws<ArgumentException>(() => Engine(battleAbilities: [Self(new(Piercing: true), 123456)]));
        Assert.Throws<ArgumentException>(() => Engine(battleAbilities: [Self(new(Piercing: true)), Self(new(DirectAttack: true))]));
        Assert.Throws<ArgumentException>(() => Engine(battleAbilities: [new("bad-equip", A, new(Piercing: true), LockScope.Equipped)]));
        Assert.Throws<ArgumentException>(() => Engine(battleAbilities: [new("bad-self", Negate, new(Piercing: true))]));
    }

    [Fact]
    public void NegatingTheSourceDuringItsSecondAttackCancelsThatAttack()
    {
        var drain = new EffectDefinition("drain", Negate, EffectKind.ContinuousTrap, [],
            [new BattleTimingCondition(BattleTiming.AttackDeclaration)], negation: LastingNegation.FaceUpMonsters);
        var e = Engine(defender: false, battleAbilities: [Self(new(AttacksPerBattlePhase: 2))], profiles: [drain]);
        Ready(e); Attack(e, null); Close(e); Declare(e, null); Respond(e, 0); DrainEffects(e);
        Assert.Equal(BattleAttackCancellation.AttackerUnavailable, e.State.Battle!.Attack!.Cancellation);
        FinishAttack(e);
        Assert.Equal(6200, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void LosingMultipleAttacksDuringDamageStepDoesNotStopTheCommittedBattle()
    {
        var drain = new EffectDefinition("drain", Negate, EffectKind.ContinuousTrap, [],
            [new BattleTimingCondition(BattleTiming.BeforeCalculation)], negation: LastingNegation.FaceUpMonsters);
        var e = Engine(defender: false, battleAbilities: [Self(new(AttacksPerBattlePhase: 2))], profiles: [drain]);
        Ready(e); Attack(e, null); Close(e); Declare(e, null); ToDamage(e, DamageStep.BeforeCalculation);
        var w = e.State.Battle!.Window;
        Ok(e, new PassBattleResponse(w.Responder, w.DecisionId));
        Respond(e, 0); DrainEffects(e);
        Assert.Null(e.State.Battle.Attack!.Cancellation);
        FinishAttack(e);
        Assert.Equal(4400, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AContinuousTrapCanGrantBattleAbilitiesToTheOpposingSide()
    {
        var marker = new EffectDefinition("marker", Negate, EffectKind.ContinuousTrap, [],
            [new BattleTimingCondition(BattleTiming.AttackDeclaration)], isMarker: true);
        var e = Engine(faceDown: true, profiles: [marker], battleAbilities:
            [new("grant", Negate, new(DirectAttack: true, AttacksPerBattlePhase: 2), LockScope.Side, TargetSide.Opponent)]);
        Ready(e); Declare(e); Respond(e, 0); DrainEffects(e); FinishAttack(e);
        Attack(e, null);
        Assert.Equal(6200, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ProofCardsCarryBattleProfilesAndReviewedReferences()
    {
        uint[] ids = [5142, 6545, 4403, 4428, 4494, 5137, 8098];
        foreach (uint id in ids)
        {
            var profile = Assert.Single(TemplateCardFamily.Family.BattleAbilities, p => p.CardId == id);
            Assert.Contains(profile.EffectId, TemplateCardFamily.Family.Reviewed[id].Effects.References);
            Assert.True(TemplateCardFamily.Family.Rules[id].AllowsBasicBattle);
        }
    }
}
