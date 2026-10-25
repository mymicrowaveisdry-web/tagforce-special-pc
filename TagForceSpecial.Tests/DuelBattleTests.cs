using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic rule tests, independent of the PPSSPP reference comparisons.</summary>
public sealed class DuelBattleTests
{
    private static DuelEngine Engine(int? attack = 1800, int? defense = 1000,
        int? opposingAttack = 1200, int? opposingDefense = 2000, bool basicBattle = true)
    {
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-battle"),
        [
            Card(1, attack, defense), Card(2, opposingAttack, opposingDefense),
            Card(3, 0, 0), Card(4, 0, 0), Card(5, 0, 0)
        ]);
        var rules = new Dictionary<uint, DuelCardRules>
        {
            [1] = new(CardCategory.Monster, true, basicBattle),
            [2] = new(CardCategory.Monster, true, true),
            [3] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
            [4] = new(CardCategory.Trap, AllowsSpellTrapSet: true),
            [5] = new(CardCategory.Spell)
        };
        uint[] first = [1, 3, 4, 1, 5, .. Enumerable.Repeat(1u, 35)];
        return new(catalog, rules, first, Enumerable.Repeat(2u, 40));
    }

    private static CardDefinition Card(uint id, int? attack, int? defense) => new(id, $"Synthetic {id}", "",
        new(attack, defense, 4), new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 4u << 11, 0));

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Empty(result.Events);
    }

    private static void EnterMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(player));
        Ok(engine, new ChangePhase(player, DuelPhase.Main1));
    }

    private static void NextMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        if (engine.State.Phase == DuelPhase.Battle) Ok(engine, new ChangePhase(player, DuelPhase.Main2));
        Ok(engine, new ChangePhase(player, DuelPhase.End));
        while (engine.State.GetPlayer(player).Hand.Count > 6)
            Ok(engine, new DiscardForHandLimit(player, engine.State.GetPlayer(player).Hand[^1].InstanceId));
        Ok(engine, new EndTurn(player));
        EnterMain(engine);
    }

    private static void Play(DuelEngine engine, bool set = false, int zone = 0)
    {
        var player = engine.State.ActivePlayer;
        var card = engine.State.GetPlayer(player).Hand.First(c => c.GameCardId is 1 or 2);
        Ok(engine, set ? new SetMonster(player, card.InstanceId, zone) : new NormalSummon(player, card.InstanceId, zone));
    }

    // Player two attacks on turn two, using independently chosen opposing stats.
    private static DuelEngine Battle(int attack, int defense, int targetAttack, int targetDefense, bool set = false)
    {
        var engine = Engine(targetAttack, targetDefense, attack, defense);
        EnterMain(engine); Play(engine, set); NextMain(engine); Play(engine);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        return engine;
    }

    [Fact]
    public void MonsterSetSharesSummonLimitAndPreservesSnapshots()
    {
        var engine = Engine(); EnterMain(engine);
        var before = engine.State;
        var card = before.GetPlayer(PlayerId.One).Hand[0];
        var result = Ok(engine, new SetMonster(PlayerId.One, card.InstanceId, 2));
        Assert.IsType<MonsterSet>(Assert.Single(result.Events));
        var set = engine.State.GetPlayer(PlayerId.One).MonsterZones[2]!;
        Assert.Equal(BattlePosition.FaceDownDefense, set.Position);
        Assert.Equal(1, set.SummonedOnTurn);
        Assert.Null(before.GetPlayer(PlayerId.One).MonsterZones[2]);
        Assert.Equal(5, before.GetPlayer(PlayerId.One).Hand.Count);
        Reject(engine, new NormalSummon(PlayerId.One, 4, 0), DuelError.NormalSummonLimitReached);
        Reject(engine, new SetMonster(PlayerId.One, 4, 2), DuelError.MonsterZoneOccupied);
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 2, BattlePosition.FaceUpAttack), DuelError.PositionChangeNotAllowed);
        AssertConservation(engine.State);
    }

    [Fact]
    public void BackRowSetsDoNotConsumeSummonAndPersistAcrossTransitions()
    {
        var engine = Engine(); EnterMain(engine);
        Reject(engine, new SetSpellTrap(PlayerId.One, 1, 0), DuelError.NotSpellOrTrap);
        Reject(engine, new SetSpellTrap(PlayerId.One, 2, 5), DuelError.InvalidSpellTrapZone);
        Reject(engine, new SetSpellTrap(PlayerId.One, 99, 0), DuelError.CardNotInHand);
        Reject(engine, new SetSpellTrap(PlayerId.One, 5, 0), DuelError.SpellTrapSetNotAllowed);
        var first = engine.State;
        Ok(engine, new SetSpellTrap(PlayerId.One, 2, 0));
        Reject(engine, new SetSpellTrap(PlayerId.One, 3, 0), DuelError.SpellTrapZoneOccupied);
        Ok(engine, new SetSpellTrap(PlayerId.One, 3, 4));
        Play(engine);
        NextMain(engine);
        Assert.Null(first.GetPlayer(PlayerId.One).SpellTrapZones[0]);
        Assert.Equal(3u, engine.State.GetPlayer(PlayerId.One).SpellTrapZones[0]!.GameCardId);
        Assert.Equal(4u, engine.State.GetPlayer(PlayerId.One).SpellTrapZones[4]!.GameCardId);
        AssertConservation(engine.State);
    }

    [Fact]
    public void PositionsChangeOncePerTurnAndFlipSummonDoesNotConsumeNormalSummon()
    {
        var engine = Engine(); EnterMain(engine); Play(engine, set: true);
        NextMain(engine); NextMain(engine);
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense), DuelError.InvalidBattlePosition);
        var result = Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Assert.IsType<MonsterFlipSummoned>(Assert.Single(result.Events));
        Assert.False(engine.State.NormalSummonUsed);
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
        Play(engine, zone: 1);
        NextMain(engine); NextMain(engine);
        Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense));
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceDownDefense), DuelError.InvalidBattlePosition);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Reject(engine, new DeclareAttack(PlayerId.One, 0), DuelError.AttackerNotInAttackPosition);
        NextMain(engine); NextMain(engine);
        Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.One, 0)); // Position changes do not prevent attacks.
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
        AssertConservation(engine.State);
    }

    [Theory]
    [InlineData(1800, 1200, 7400, 8000, false, true)]
    [InlineData(1200, 1800, 8000, 7400, true, false)]
    [InlineData(1200, 1200, 8000, 8000, true, true)]
    [InlineData(0, 0, 8000, 8000, false, false)]
    public void AttackPositionBattleResolvesDamageAndDestruction(int attack, int targetAttack,
        int targetLp, int attackerLp, bool attackerDestroyed, bool targetDestroyed)
    {
        var engine = Battle(attack, 1000, targetAttack, 1000);
        var before = engine.State;
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.IsType<AttackDeclared>(result.Events[0]);
        Assert.Equal(targetLp, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(attackerLp, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(attackerDestroyed, engine.State.GetPlayer(PlayerId.Two).MonsterZones[0] is null);
        Assert.Equal(targetDestroyed, engine.State.GetPlayer(PlayerId.One).MonsterZones[0] is null);
        Assert.Equal(attackerDestroyed ? 1 : 0, engine.State.GetPlayer(PlayerId.Two).Graveyard.Count);
        Assert.Equal(targetDestroyed ? 1 : 0, engine.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Equal(8000, before.GetPlayer(PlayerId.One).LifePoints);
        Assert.NotNull(before.GetPlayer(PlayerId.One).MonsterZones[0]);
        if (!attackerDestroyed) Reject(engine, new DeclareAttack(PlayerId.Two, 0), DuelError.AlreadyAttacked);
        AssertConservation(engine.State);
    }

    [Theory]
    [InlineData(1800, 1000, 8000, true)]
    [InlineData(1000, 1800, 7200, false)]
    [InlineData(1000, 1000, 8000, false)]
    [InlineData(0, 0, 8000, false)]
    public void FaceDownDefenseRevealsWithoutPiercingOrDestroyingAttacker(int attack, int defense,
        int attackerLp, bool targetDestroyed)
    {
        var engine = Battle(attack, 0, 0, defense, set: true);
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.IsType<MonsterRevealed>(result.Events[1]);
        Assert.Equal(8000, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(attackerLp, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        Assert.Equal(targetDestroyed, engine.State.GetPlayer(PlayerId.One).MonsterZones[0] is null);
        if (!targetDestroyed)
            Assert.Equal(BattlePosition.FaceUpDefense, engine.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
        AssertConservation(engine.State);
    }

    [Fact]
    public void FaceUpDefenseUsesDefenseAndDoesNotEmitAnotherReveal()
    {
        var engine = Engine(1800, 2000, 1200, 0);
        EnterMain(engine); Play(engine); NextMain(engine); NextMain(engine);
        Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense));
        NextMain(engine); Play(engine);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.DoesNotContain(result.Events, e => e is MonsterRevealed or MonsterDestroyed);
        Assert.Equal(7200, engine.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void InvalidAttacksAndPositionsAreAtomic()
    {
        var engine = Battle(1800, 1000, 1200, 1000);
        Reject(engine, new DeclareAttack(PlayerId.One, 0, 0), DuelError.NotActivePlayer);
        Reject(engine, new DeclareAttack(PlayerId.Two, -1), DuelError.InvalidMonsterZone);
        Reject(engine, new DeclareAttack(PlayerId.Two, 0, 5), DuelError.InvalidMonsterZone);
        Reject(engine, new DeclareAttack(PlayerId.Two, 1, 0), DuelError.MonsterZoneEmpty);
        Reject(engine, new DeclareAttack(PlayerId.Two, 0, 1), DuelError.MonsterZoneEmpty);
        Reject(engine, new DeclareAttack(PlayerId.Two, 0), DuelError.DirectAttackBlocked);
        Reject(engine, new ChangeMonsterPosition(PlayerId.Two, 0, BattlePosition.FaceUpDefense), DuelError.WrongPhase);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Main2));
        Reject(engine, new DeclareAttack(PlayerId.Two, 0, 0), DuelError.WrongPhase);
        Reject(engine, new ChangeMonsterPosition(PlayerId.Two, 5, BattlePosition.FaceUpDefense), DuelError.InvalidMonsterZone);
        Reject(engine, new ChangeMonsterPosition(PlayerId.Two, 1, BattlePosition.FaceUpDefense), DuelError.MonsterZoneEmpty);
        Reject(engine, new ChangeMonsterPosition(PlayerId.Two, 0, (BattlePosition)99), DuelError.InvalidBattlePosition);
    }

    [Fact]
    public void UnknownStatsAndUnsupportedEffectsRejectBeforeRevealingOrConsumingAttack()
    {
        var engine = Engine(attack: null); EnterMain(engine); Play(engine);
        NextMain(engine); Play(engine);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Reject(engine, new DeclareAttack(PlayerId.Two, 0, 0), DuelError.UnsupportedBattleStatistics);
        engine = Engine(defense: null); EnterMain(engine); Play(engine, set: true);
        NextMain(engine); Play(engine);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Reject(engine, new DeclareAttack(PlayerId.Two, 0, 0), DuelError.UnsupportedBattleStatistics);
        Assert.Equal(BattlePosition.FaceDownDefense, engine.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
        engine = Engine(basicBattle: false); EnterMain(engine); Play(engine);
        NextMain(engine); Play(engine);
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Reject(engine, new DeclareAttack(PlayerId.Two, 0, 0), DuelError.UnsupportedBattleCard);
    }

    [Fact]
    public void DirectAttacksResetEachTurnAndLpDefeatStopsFurtherActions()
    {
        var engine = Engine(opposingAttack: 3000); EnterMain(engine); NextMain(engine); Play(engine);
        for (int i = 0; i < 3; i++)
        {
            Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
            var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0));
            Assert.Equal(Math.Max(0, 8000 - 3000 * (i + 1)), engine.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Contains(result.Events, e => e is BattleDamageDealt { Damage: 3000 });
            if (i < 2)
            {
                Reject(engine, new DeclareAttack(PlayerId.Two, 0), DuelError.AlreadyAttacked);
                NextMain(engine); NextMain(engine);
            }
        }
        Assert.Equal(new DuelOutcome(PlayerId.Two, PlayerId.One, DuelEndReason.LifePointsDepleted), engine.State.Outcome);
        Reject(engine, new DeclareAttack(PlayerId.Two, 0), DuelError.DuelFinished);
        Reject(engine, new ChangePhase(PlayerId.Two, DuelPhase.Main2), DuelError.DuelFinished);
        AssertConservation(engine.State);
    }

    [Fact]
    public void MultipleAttackersCanFinishDuelAndLethalDamageEndsBeforeDestruction()
    {
        var engine = Engine(attack: 5000, opposingAttack: 0);
        EnterMain(engine); Play(engine); NextMain(engine); NextMain(engine); Play(engine, zone: 1);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.One, 0)); // Opponent has 3000 LP.
        NextMain(engine); Play(engine); NextMain(engine);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.One, 1, 0));
        Assert.Equal(0, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.IsType<DuelEnded>(result.Events[^1]);
        Assert.DoesNotContain(result.Events, e => e is MonsterDestroyed);
        Assert.NotNull(engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
        AssertConservation(engine.State);
    }

    [Fact]
    public void EachMonsterHasItsOwnAttackAndAttackingPreventsMain2PositionChange()
    {
        var engine = Engine(); EnterMain(engine); Play(engine);
        NextMain(engine); NextMain(engine); Play(engine, zone: 1);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.One, 0));
        Ok(engine, new DeclareAttack(PlayerId.One, 1));
        Assert.Equal(4400, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Reject(engine, new DeclareAttack(PlayerId.One, 0), DuelError.AlreadyAttacked);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
        NextMain(engine); NextMain(engine);
        Ok(engine, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense));
        AssertConservation(engine.State);
    }

    [Fact]
    public void BattleCommandsReplayDeterministically()
    {
        var first = Battle(1800, 0, 1200, 1000, set: true);
        var second = Battle(1800, 0, 1200, 1000, set: true);
        var a = Ok(first, new DeclareAttack(PlayerId.Two, 0, 0));
        var b = Ok(second, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Equal(a.Events, b.Events);
        Assert.Equal(JsonSerializer.Serialize(a.State), JsonSerializer.Serialize(b.State));
    }

    private static void AssertConservation(DuelState state)
    {
        var all = state.Players.SelectMany(p => p.Deck.Concat(p.Hand).Concat(p.Graveyard)
            .Concat(p.MonsterZones.OfType<FieldMonster>().Select(m => m.Card))
            .Concat(p.SpellTrapZones.OfType<CardInstance>())).ToArray();
        Assert.Equal(80, all.Length);
        Assert.Equal(80, all.Select(c => c.InstanceId).Distinct().Count());
        Assert.All(state.Players, player => Assert.Equal(40, all.Count(c => c.Owner == player.Id)));
    }
}
