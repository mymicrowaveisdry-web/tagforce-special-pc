using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Battle Trap family wiring through the production definitions and the staged battle flow.</summary>
public sealed class BattleTrapFamilyTests
{
    private const uint A = 901, B = 902, C = 903, MirrorForce = 4887, Cylinder = 5124, Prison = 7400, Draining = 5994,
        Sakuretsu = 5799, Ruin = 4692, Catastrophe = 5493, DarkMirror = 5858;

    private static CardCatalog Catalog() => new(new(null, "synthetic-battle-trap"),
        BattleTrapFamily.Cards.Select(c => c.CardId).Concat([A, B, C])
            .Select(id => new CardDefinition(id, $"Probe {id}", "", id switch
            {
                A => new(1000, 1000, 4), B => new(1500, 2000, 4), C => new(1800, 500, 4), _ => new(0, 0, 1)
            })));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = BattleTrapFamily.Rules.ToDictionary();
        foreach (uint monster in new[] { A, B, C })
            rules[monster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        return new(Catalog(), rules, first, second, effectDefinitions: BattleTrapFamily.Definitions, experimentalBattleFlow: true);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(A, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone, bool set = false) =>
        Ok(e, set ? new SetMonster(Turn(e), Hand(e, id), zone) : new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id), zone));
    private static FieldMonster? Monster(DuelEngine e, PlayerId p, int zone) => e.State.GetPlayer(p).MonsterZones[zone];

    /// <summary>Player One sets <paramref name="trap"/> in zone 0; Player Two summons B in zone 0 and declares an attack.</summary>
    private static DuelEngine Attack(uint trap, uint[]? second = null, Action<DuelEngine>? prepare = null, int attacker = 0)
    {
        var e = Engine(Deck(trap, trap), second ?? Deck(B));
        EnterMain(e); SetCard(e, trap, 0); Next(e);
        Summon(e, B, 0);
        if (prepare is not null) { Next(e); Next(e); prepare(e); }
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(Turn(e), attacker, Monster(e, PlayerId.One, 0) is null ? null : 0));
        Assert.Equal(BattleTiming.AttackDeclaration, e.State.Battle!.Window.Timing);
        return e;
    }

    private static List<DuelEvent> Trigger(DuelEngine e, int zone = 0)
    {
        List<DuelEvent> events = [.. S.Respond(e, zone).Events];
        events.AddRange(Resolve(e));
        return events;
    }

    [Fact]
    public void ConditionNeedsAnOpponentAttackDeclaration()
    {
        var e = Engine(Deck(Cylinder), Deck(B));
        EnterMain(e); SetCard(e, Cylinder, 0); Next(e); Next(e);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.ActivationConditionNotMet);

        var own = Attack(Sakuretsu);
        Assert.Equal(PlayerId.One, own.State.Battle!.Window.Responder);
        Reject(own, new ActivateBattleResponse(PlayerId.Two, own.State.Battle.Window.DecisionId, 0), DuelError.NotBattleResponder);
    }

    [Fact]
    public void MirrorForceDestroysOnlyAttackPositionMonsters()
    {
        var e = Attack(MirrorForce, Deck(B, C), d => Summon(d, C, 1, set: true));
        var events = Trigger(e);
        Assert.Single(events.OfType<MonsterDestroyed>(), d => d.Card.GameCardId == B);
        Assert.Null(Monster(e, PlayerId.Two, 0));
        Assert.NotNull(Monster(e, PlayerId.Two, 1));
        Assert.Contains(S.FinishAttack(e), ev => ev is AttackCancelled { Reason: BattleAttackCancellation.AttackerUnavailable });
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void MagicCylinderNegatesAndDealsTheAttackersAttack()
    {
        var e = Attack(Cylinder);
        var events = Trigger(e);
        Assert.Contains(events, ev => ev is AttackCancelled { Reason: BattleAttackCancellation.Negated });
        Assert.Equal(6500, e.State.GetPlayer(PlayerId.Two).LifePoints);
        S.FinishAttack(e);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.NotNull(Monster(e, PlayerId.Two, 0));
    }

    [Fact]
    public void MagicCylinderDoesNothingWhenTheAttackerLeftFirst()
    {
        var e = Engine(Deck(Cylinder, Sakuretsu), Deck(B));
        EnterMain(e); SetCard(e, Cylinder, 0); SetCard(e, Sakuretsu, 1); Next(e);
        Summon(e, B, 0); Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0));
        S.Respond(e, 0);
        var chain = e.State.PendingChain!;
        Ok(e, new PassChainResponse(PlayerId.Two, chain.DecisionId));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 1));
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is MonsterDestroyed { Player: PlayerId.Two });
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Number: 1, Reason: ChainSkipReason.TargetUnavailable });
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void DrainingShieldNegatesAndGainsTheAttackersAttack()
    {
        var e = Attack(Draining);
        Assert.Contains(Trigger(e), ev => ev is AttackCancelled { Reason: BattleAttackCancellation.Negated });
        Assert.Equal(9500, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void SakuretsuArmorDestroysTheAttacker()
    {
        var e = Attack(Sakuretsu);
        Assert.Contains(Trigger(e), ev => ev is MonsterDestroyed { Player: PlayerId.Two, Zone: 0 });
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == B);
        Assert.Contains(S.FinishAttack(e), ev => ev is AttackCancelled { Reason: BattleAttackCancellation.AttackerUnavailable });
    }

    [Fact]
    public void DimensionalPrisonBanishesTheAttacker()
    {
        var e = Attack(Prison);
        Assert.Contains(Trigger(e), ev => ev is MonsterBanished { Player: PlayerId.Two, Zone: 0 });
        Assert.Null(Monster(e, PlayerId.Two, 0));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Banished, c => c.GameCardId == B);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == B);
    }

    [Fact]
    public void WidespreadRuinDestroysTheHighestAttackAttackPositionMonster()
    {
        var e = Attack(Ruin, Deck(B, C), d => Summon(d, C, 1), attacker: 0);
        var events = Trigger(e);
        Assert.Single(events.OfType<MonsterDestroyed>(), d => d.Card.GameCardId == C);
        Assert.NotNull(Monster(e, PlayerId.Two, 0));
    }

    [Fact]
    public void MalevolentCatastropheDestroysEveryOtherSpellTrap()
    {
        var e = Attack(Catastrophe, Deck(B, MirrorForce), d => SetCard(d, MirrorForce, 2));
        var events = Trigger(e);
        Assert.Contains(events, ev => ev is SpellTrapDestroyed { Player: PlayerId.Two, Zone: 2 });
        Assert.DoesNotContain(events, ev => ev is SpellTrapDestroyed { Player: PlayerId.One });
        Assert.All(e.State.Players, p => Assert.All(p.SpellTrapFields, Assert.Null));
    }

    [Fact]
    public void DarkMirrorForceBanishesDefensePositionMonstersAndNeedsOne()
    {
        var none = Attack(DarkMirror);
        var w = none.State.Battle!.Window;
        Reject(none, new ActivateBattleResponse(PlayerId.One, w.DecisionId, 0), DuelError.DecisionUnavailable);

        var e = Attack(DarkMirror, Deck(B, C), d => Summon(d, C, 1, set: true));
        var events = Trigger(e);
        Assert.Single(events.OfType<MonsterBanished>(), b => b.Card.GameCardId == C);
        Assert.NotNull(Monster(e, PlayerId.Two, 0));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Banished, c => c.GameCardId == C);
    }

    [Fact]
    public void DefinitionsCannotActivateDuringTheDamageStep()
    {
        var e = Attack(Draining);
        S.ToDamage(e, DamageStep.Start);
        if (e.State.Battle!.Window.Responder == PlayerId.Two)
            Ok(e, new PassBattleResponse(PlayerId.Two, e.State.Battle.Window.DecisionId));
        Reject(e, new ActivateBattleResponse(PlayerId.One, e.State.Battle!.Window.DecisionId, 0), DuelError.ActivationTimingNotAllowed);
    }
}
