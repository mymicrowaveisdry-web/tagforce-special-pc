using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Summon Trap family wiring through the production definitions and the summon-response window.</summary>
public sealed class SummonTrapFamilyTests
{
    private const uint A = 901, B = 902, C = 903, D = 904, TrapHole = 4836, Eatgaboon = 4688, Tape = 4687,
        Bottomless = 5400, Torrential = 5114;

    private static CardCatalog Catalog() => new(new(null, "synthetic-summon-trap"),
        SummonTrapFamily.Cards.Select(c => c.CardId).Concat([A, B, C, D])
            .Select(id => new CardDefinition(id, $"Probe {id}", "", id switch
            {
                A => new(1000, 1000, 4), B => new(1500, 2000, 4), C => new(1800, 500, 4), D => new(400, 400, 4), _ => new(0, 0, 1)
            })));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = SummonTrapFamily.Rules.ToDictionary();
        foreach (uint monster in new[] { A, B, C, D })
            rules[monster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        return new(Catalog(), rules, first, second, effectDefinitions: SummonTrapFamily.Definitions);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(A, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static DuelActionResult Summon(DuelEngine e, uint id, int zone, bool set = false) =>
        Ok(e, set ? new SetMonster(Turn(e), Hand(e, id), zone) : new NormalSummon(Turn(e), Hand(e, id), zone));
    private static FieldMonster? Monster(DuelEngine e, PlayerId p, int zone) => e.State.GetPlayer(p).MonsterZones[zone];

    /// <summary>Player One sets <paramref name="trap"/> in zone 0, then Player Two's turn begins.</summary>
    private static DuelEngine Prepared(uint trap, uint[] second, Action<DuelEngine>? first = null)
    {
        var e = Engine(Deck(trap, A), second);
        EnterMain(e); first?.Invoke(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, trap), 0)); Next(e);
        return e;
    }

    private static List<DuelEvent> Spring(DuelEngine e)
    {
        var window = e.State.OpenWindow!;
        Assert.NotNull(window.Summoned);
        Assert.Equal(PlayerId.One, window.Responder);
        List<DuelEvent> events = [.. Ok(e, new ActivateChainResponse(PlayerId.One, window.DecisionId, 0)).Events];
        Assert.Null(e.State.OpenWindow);
        events.AddRange(Resolve(e));
        return events;
    }

    [Fact]
    public void TrapHoleDestroysANormalSummonWithEnoughAttack()
    {
        var e = Prepared(TrapHole, Deck(B));
        Assert.Contains(Summon(e, B, 0).Events, ev => ev is SummonResponseWindowRequested { Responder: PlayerId.One });
        Assert.Contains(Spring(e), ev => ev is MonsterDestroyed { Player: PlayerId.Two, Zone: 0 });
        Assert.Null(Monster(e, PlayerId.Two, 0));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == B);
    }

    [Fact]
    public void NoWindowOpensWithoutAQualifyingSummon()
    {
        var weak = Prepared(TrapHole, Deck(D));
        Assert.DoesNotContain(Summon(weak, D, 0).Events, ev => ev is SummonResponseWindowRequested);
        Assert.Null(weak.State.OpenWindow);

        var set = Prepared(TrapHole, Deck(B));
        Summon(set, B, 0, set: true);
        Assert.Null(set.State.OpenWindow);

        // The Trap's controller never answers its own Summon.
        var e = Engine(Deck(TrapHole, B), Deck());
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, TrapHole), 0)); Next(e); Next(e);
        Summon(e, B, 0);
        Assert.Null(e.State.OpenWindow);
    }

    [Fact]
    public void PassingTheWindowKeepsTheMonsterAndThePhase()
    {
        var e = Prepared(TrapHole, Deck(B));
        Summon(e, B, 0);
        var events = Ok(e, new PassChainResponse(PlayerId.One, e.State.OpenWindow!.DecisionId)).Events;
        Assert.Contains(events, ev => ev is OpenWindowPassed);
        Assert.DoesNotContain(events, ev => ev is PhaseChanged);
        Assert.Null(e.State.OpenWindow);
        Assert.Equal(DuelPhase.Main1, e.State.Phase);
        Assert.NotNull(Monster(e, PlayerId.Two, 0));
    }

    [Fact]
    public void TrapHoleAnswersAFlipSummon()
    {
        var e = Prepared(TrapHole, Deck(A));
        Summon(e, A, 0, set: true); Next(e); Next(e);
        var events = Ok(e, new ChangeMonsterPosition(PlayerId.Two, 0, BattlePosition.FaceUpAttack)).Events;
        Assert.Contains(events, ev => ev is SummonResponseWindowRequested);
        Assert.Contains(Spring(e), ev => ev is MonsterDestroyed { Player: PlayerId.Two, Zone: 0 });
    }

    [Theory]
    [InlineData(Eatgaboon, D)]
    [InlineData(Tape, C)]
    public void LowStatTrapsDestroyTheirSummon(uint trap, uint monster)
    {
        var e = Prepared(trap, Deck(monster));
        Summon(e, monster, 0);
        Assert.Contains(Spring(e), ev => ev is MonsterDestroyed { Player: PlayerId.Two, Zone: 0 });
    }

    [Fact]
    public void BottomlessTrapHoleBanishesInsteadOfSendingToTheGraveyard()
    {
        var e = Prepared(Bottomless, Deck(B));
        Summon(e, B, 0);
        var events = Spring(e);
        Assert.Contains(events, ev => ev is MonsterDestroyed { Player: PlayerId.Two } d && d.FieldDestination == FieldCardDestination.Banished);
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Banished, c => c.GameCardId == B);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == B);
    }

    [Fact]
    public void TorrentialTributeDestroysEveryMonster()
    {
        var e = Prepared(Torrential, Deck(B), d => Summon(d, A, 0));
        Summon(e, B, 0);
        var destroyed = Spring(e).OfType<MonsterDestroyed>().ToArray();
        Assert.Equal(2, destroyed.Length);
        Assert.All(e.State.Players, p => Assert.All(p.MonsterZones, Assert.Null));
    }
}
