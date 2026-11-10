using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Continuous "cannot" rules and the actions that consult them.</summary>
public sealed class RuleLockTests
{
    private const uint W = 901, Low = 902, High = 903, Big = 904, DragonA = 905, Weak = 906, Burn = 907, QuickBurn = 908, TrapBurn = 909, Hole = 910, Storm = 911, Shield = 912, Snipe = 913, Imperial = 914, Drain = 915, Reviver = 916, Mach = 917, Potion = 4878,
        Bind = 5134, Bog = 5281, Mask = 5106, PowerFilter = 7787, Prison = 9953, MistBody = 6546, Gyroid = 6385, Shields = 7990, LordOfD = 4747, Sphinx = 6321, Invader = 5922, Warwolf = 6195, Rod = 11557, GuardDog = 6697, Feint = 5332;

    private static CardCatalog Catalog() => new(new(null, "synthetic-rule-locks"),
        TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", c.CardId == Sphinx ? new(1000, 2000, 4) : new(500, 2000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0))).Concat(
        [
            new(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(Low, "Low", "", new(1800, 1000, 3), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(High, "High", "", new(2000, 1000, 5), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(Big, "Big", "", new(2600, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(DragonA, "Dragon", "", new(1500, 1000, 4), new(1, MonsterTypes.Dragon, 1, 1, 0, 0, 0)),
            new(Weak, "Weak", "", new(900, 900, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(Mach, "Machine", "", new(1500, 1000, 4), new(1, MonsterTypes.Machine, 1, 1, 0, 0, 0))
        ]).Concat(new uint[] { Burn, QuickBurn, TrapBurn, Hole, Storm, Shield, Snipe, Imperial, Drain, Reviver }.Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));

    private static DuelEngine Engine(uint[] first, uint[] second, RuleLockProfile[]? locks = null)
    {
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        DuelCardRules monster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in new[] { W, Low, Big, DragonA, Weak, Mach }) rules[id] = monster;
        rules[High] = monster with { AllowsTributeSummon = true };
        foreach (uint id in new[] { Burn, QuickBurn, Hole, Storm, Reviver, Snipe }) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        foreach (uint id in new[] { TrapBurn, Shield, Imperial, Drain }) rules[id] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(Catalog(), rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions,
                new("burn", Burn, EffectKind.NormalSpell, [new DealDamageStep(100)]),
                new("quick-burn", QuickBurn, EffectKind.QuickPlaySpell, [new DealDamageStep(100)]),
                new("trap-burn", TrapBurn, EffectKind.NormalTrap, [new DealDamageStep(100)]),
                new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.SpellTrap)]),
                new("snipe", Snipe, EffectKind.NormalSpell, [new DestroyTargetsStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster)]),
                new("shield", Shield, EffectKind.ContinuousTrap, [], isMarker: true),
                new("imperial", Imperial, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells),
                new("drain", Drain, EffectKind.ContinuousTrap, [], negation: LastingNegation.FaceUpMonsters),
                new("reviver", Reviver, EffectKind.NormalSpell, [new SpecialSummonTargetStep()], fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter(), mustBeSummonable: true)])],
            shuffleSeed: 6, ruleLocks: [.. TemplateCardFamily.Family.RuleLocks, .. locks ?? []]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id), zone));
    private static void ToBattle(DuelEngine e) { Phase(e, DuelPhase.Battle); S.Ready(e); }
    private static void LeaveBattle(DuelEngine e) { S.Close(e); while (e.State.Battle!.Step != BattleStep.End) { S.Advance(e); S.Close(e); } }

    private static void Target(DuelEngine e, PlayerId owner, FieldLocation location, int zone)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Player == owner && o.Location == location && o.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, owner, location, zone, t.Card.InstanceId));
    }

    /// <summary>Player One activates a Continuous Spell; both players have had a turn so attacks are legal afterwards.</summary>
    private static DuelEngine WithSpell(uint spell, uint[]? first = null, uint[]? second = null, RuleLockProfile[]? locks = null)
    {
        var e = Engine(first ?? Deck(spell), second ?? Deck(), locks);
        EnterMain(e);
        Activate(e, spell, 3); Resolve(e);
        return e;
    }

    private static DuelEngine WithSetTrap(uint trap, uint[]? first = null, uint[]? second = null)
    {
        var e = Engine(first ?? Deck(trap), second ?? Deck());
        EnterMain(e); SetCard(e, trap, 0); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        return e;
    }

    [Fact]
    public void GravityBindStopsOnlyHighLevelAttacksAndEndsWithItsCard()
    {
        var e = Engine(Deck(Bind, W, Low), Deck(Storm));
        EnterMain(e); SetCard(e, Bind, 0); Summon(e, W, 0); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        Summon(e, Low, 1); Next(e); Next(e);
        ToBattle(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.LockedByEffect);
        Ok(e, new DeclareAttack(PlayerId.One, 1));
        S.FinishAttack(e); LeaveBattle(e);
        Next(e);
        Activate(e, Storm); Resolve(e); Next(e);
        ToBattle(e);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
    }

    [Fact]
    public void VengefulBogSpiritStopsOnlyMonstersSummonedThisTurn()
    {
        var e = WithSpell(Bog, Deck(Bog, W, W));
        Summon(e, W, 0); Next(e); Next(e);
        Summon(e, W, 1);
        ToBattle(e);
        Reject(e, new DeclareAttack(PlayerId.One, 1), DuelError.LockedByEffect);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
    }

    [Fact]
    public void MaskOfRestrictStopsTributes()
    {
        var e = WithSetTrap(Mask, Deck(Mask, W, High));
        Summon(e, W, 0); Next(e); Next(e);
        var monster = P(e, PlayerId.One).MonsterZones[0]!;
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, High), 0, [new MonsterBinding(PlayerId.One, 0, monster.Card, monster.PlacementId)], false, null), DuelError.TributeNotAllowed);
    }

    [Fact]
    public void PowerFilterStopsOnlyLowAttackSpecialSummons()
    {
        var e = WithSpell(PowerFilter, Deck(PowerFilter, Weak, Big, Reviver), Deck(Hole, Hole, Hole));
        Summon(e, Weak, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Reviver), 4), DuelError.DecisionUnavailable);
        Summon(e, Big, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Activate(e, Reviver);
    }

    [Fact]
    public void RoyalPrisonStopsSummonsFromTheGraveyard()
    {
        var e = WithSetTrap(Prison, Deck(Prison, Big, Reviver), Deck(Hole, Hole, Hole));
        Summon(e, Big, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Reviver), 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void AnEquippedMonsterIsNotDestroyedByBattleButDamageStillApplies()
    {
        var e = Engine(Deck(W, MistBody), Deck(Big));
        EnterMain(e); Summon(e, W, 0);
        Activate(e, MistBody);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e); Next(e);
        Summon(e, Big, 0);
        ToBattle(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        S.FinishAttack(e);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Equal(6900, P(e, PlayerId.One).LifePoints);
    }

    [Fact]
    public void OncePerTurnProtectionIsSpentAndReturnsNextTurn()
    {
        var e = Engine(Deck(Gyroid), Deck(Big, Big));
        EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, Gyroid), 0)); Next(e);
        Summon(e, Big, 0); ToBattle(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Contains(S.FinishAttack(e), ev => ev is DestructionPrevented);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        LeaveBattle(e); Next(e); Next(e);

        Summon(e, Big, 1); ToBattle(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Contains(S.FinishAttack(e), ev => ev is DestructionPrevented);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        S.Close(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 1, 0));
        Assert.DoesNotContain(S.FinishAttack(e), ev => ev is DestructionPrevented);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void AnEquipLockObeysItsFilterAndStopsOnlyTheEquippedMonster()
    {
        var e = Engine(Deck(Potion, W, Mach), Deck());
        EnterMain(e); Summon(e, W, 0); Next(e); Next(e);
        Summon(e, Mach, 1);
        Activate(e, Potion);
        Assert.DoesNotContain(e.State.PendingDecision!.FieldTargetOptions!, o => o.Card.GameCardId == Mach);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        ToBattle(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.LockedByEffect);
        Ok(e, new DeclareAttack(PlayerId.One, 1));
    }

    [Fact]
    public void ALockedTargetCannotBeChosenButAnEarlierTargetStillResolves()
    {
        var e = Engine(Deck(LordOfD, DragonA, Snipe), Deck());
        EnterMain(e); Summon(e, DragonA, 0); Next(e); Next(e);
        Summon(e, LordOfD, 1);
        Activate(e, Snipe);
        Assert.DoesNotContain(e.State.PendingDecision!.FieldTargetOptions!, o => o.Card.GameCardId == DragonA);

        RuleLockProfile shieldLock = new("shield-lock", Shield, new RuleLock(LockedAction.BeTargeted, Cards: new MonsterTypeFilter([MonsterTypes.Warrior])));
        var late = Engine(Deck(Snipe), Deck(Shield), [shieldLock]);
        EnterMain(late); Next(late);
        SetCard(late, Shield, 0); Summon(late, W, 0); Next(late);
        Activate(late, Snipe);
        Target(late, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Ok(late, new ActivateChainResponse(PlayerId.Two, late.State.PendingChain!.DecisionId, 0));
        Resolve(late);
        Assert.Null(P(late, PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void AFaceDownMonsterCannotBeAttackedWhileAnotherCan()
    {
        var e = Engine(Deck(Sphinx, W), Deck(Big));
        EnterMain(e); Summon(e, Sphinx, 0); Next(e); Next(e);
        Ok(e, new SetMonster(PlayerId.One, Hand(e, W), 1)); Next(e);
        Summon(e, Big, 0);
        ToBattle(e);
        Reject(e, new DeclareAttack(PlayerId.Two, 0, 1), DuelError.LockedByEffect);
        Reject(e, new DeclareAttack(PlayerId.Two, 0), DuelError.DirectAttackBlocked);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
    }

    [Fact]
    public void SpellActivationLocksRespectSideKindAndPhase()
    {
        var e = Engine(Deck(Invader), Deck(QuickBurn, Burn));
        EnterMain(e); Summon(e, Invader, 0); Next(e);
        Reject(e, new ActivateSpell(PlayerId.Two, Hand(e, QuickBurn), 4), DuelError.LockedByEffect);
        Activate(e, Burn); Resolve(e);

        var rod = Engine(Deck(Rod, Burn), Deck());
        EnterMain(rod); Summon(rod, Rod, 0);
        Reject(rod, new ActivateSpell(PlayerId.One, Hand(rod, Burn), 4), DuelError.LockedByEffect);
    }

    [Fact]
    public void TrapLocksApplyOnlyInTheirPhase()
    {
        var e = Engine(Deck(Warwolf), Deck(TrapBurn));
        EnterMain(e); Summon(e, Warwolf, 0); Next(e);
        SetCard(e, TrapBurn, 0); Next(e);
        Phase(e, DuelPhase.Battle);
        var window = e.State.Battle!.Window;
        if (window.Responder == PlayerId.One) { Ok(e, new PassBattleResponse(PlayerId.One, window.DecisionId)); window = e.State.Battle!.Window; }
        Reject(e, new ActivateBattleResponse(PlayerId.Two, window.DecisionId, 0), DuelError.LockedByEffect);
        LeaveBattle(e);
        Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.Two, 0));
    }

    [Fact]
    public void ANegatedMonsterAppliesNoLock()
    {
        var e = Engine(Deck(Rod, Drain, Burn), Deck());
        EnterMain(e); Summon(e, Rod, 0); SetCard(e, Drain, 0); Next(e); Next(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Burn), 4), DuelError.LockedByEffect);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        Activate(e, Burn);
    }

    [Fact]
    public void ANegatedSpellAppliesNoLock()
    {
        var e = Engine(Deck(Bog, Imperial, W), Deck());
        EnterMain(e); SetCard(e, Imperial, 0); Activate(e, Bog, 3); Resolve(e); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        Summon(e, W, 0);
        ToBattle(e);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
    }

    [Fact]
    public void ATurnLockAllowsADirectAttackPastUnattackableMonstersAndThenEnds()
    {
        var e = Engine(Deck(Feint, W), Deck());
        EnterMain(e); Summon(e, W, 0); SetCard(e, Feint, 0); Next(e);
        Ok(e, new SetMonster(PlayerId.Two, Hand(e, W), 0)); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        ToBattle(e);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
        S.FinishAttack(e); LeaveBattle(e);
        Next(e); Next(e);
        ToBattle(e);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.DirectAttackBlocked);
    }

    [Fact]
    public void AFlipEffectCanLockTheOpponent()
    {
        var e = Engine(Deck(GuardDog), Deck(Reviver, Big, Hole));
        EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, GuardDog), 0)); Next(e); Next(e);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Assert.Contains(EventEffectScenario.Drain(e), ev => ev is RuleLockApplied { Controller: PlayerId.One });
    }

    [Fact]
    public void AnEngineRejectsLocksThatCannotApply()
    {
        var deck = Enumerable.Repeat(W, 40).ToArray();
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", 999999, new RuleLock(LockedAction.Attack))]));
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", W, new RuleLock(LockedAction.None))]));
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", W, new RuleLock(LockedAction.Attack, UsesPerTurn: 1))]));
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", Burn, new RuleLock(LockedAction.Attack, Scope: LockScope.Self))]));
    }
}
