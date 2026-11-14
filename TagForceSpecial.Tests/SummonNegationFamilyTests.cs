using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Summon negation family wiring through the production definitions and the would-be-Summoned window.</summary>
public sealed class SummonNegationFamilyTests
{
    private const uint A = 901, B = 902, S = 905, Horn = 4864, BlackHorn = 6918, Solemn = 4861, TrapHole = 4836, DarkHole = 4342;
    private const string Procedure = "synthetic-hand-procedure";

    private static CardCatalog Catalog() => new(new(null, "synthetic-summon-negation"),
        SummonNegationFamily.Cards.Select(c => c.CardId).Concat([A, B, S, TrapHole, DarkHole])
            .Select(id => new CardDefinition(id, $"Probe {id}", "", id switch
            {
                A => new(1000, 1000, 4), B => new(1500, 2000, 4), S => new(2000, 1000, 7), _ => new(0, 0, 1)
            })));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = SummonNegationFamily.Rules.ToDictionary();
        foreach (uint monster in new[] { A, B })
            rules[monster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        rules[S] = new(CardCategory.Monster, AllowsBasicBattle: true);
        rules[TrapHole] = SummonTrapFamily.Rules[TrapHole];
        rules[DarkHole] = RemovalFamily.Rules[DarkHole];
        return new(Catalog(), rules, first, second, effectDefinitions: [.. SummonNegationFamily.Definitions,
                .. SummonTrapFamily.Definitions.Where(d => d.CardId == TrapHole), .. RemovalFamily.Definitions.Where(d => d.CardId == DarkHole)],
            experimentalSpecialSummonCards: [new(S)], experimentalSpecialSummonProcedures: [new(Procedure, [S])]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(A, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Set(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id), zone));
    private static FieldMonster? Monster(DuelEngine e, PlayerId p, int zone) => e.State.GetPlayer(p).MonsterZones[zone];

    /// <summary>Player One summons A unless <paramref name="noMonster"/>, sets the traps in zones 0.., then Player Two's turn begins.</summary>
    private static DuelEngine Prepared(uint[] traps, uint[] second, bool noMonster = false)
    {
        var e = Engine(Deck([.. traps, A]), second);
        EnterMain(e);
        if (!noMonster) Ok(e, new NormalSummon(PlayerId.One, Hand(e, A), 0));
        for (int i = 0; i < traps.Length; i++) Set(e, traps[i], i);
        Next(e);
        return e;
    }

    private static List<DuelEvent> Negate(DuelEngine e, int zone = 0)
    {
        var pending = e.State.PendingSummon!;
        Assert.Equal(PlayerId.One, pending.Responder);
        List<DuelEvent> events = [.. Ok(e, new ActivateSummonNegation(PlayerId.One, pending.DecisionId, zone)).Events];
        if (e.State.PendingDecision is { } tribute)
        {
            var own = tribute.FieldTargetOptions!.First();
            events.AddRange(Ok(e, new ChooseDecisionFieldTarget(PlayerId.One, tribute.DecisionId, own.Player, own.Location, own.Zone, own.Card.InstanceId)).Events);
        }
        events.AddRange(Resolve(e));
        return events;
    }

    [Fact]
    public void HornOfHeavenTributesAndNegatesANormalSummon()
    {
        var e = Prepared([Horn], Deck(B));
        Assert.Contains(Ok(e, new NormalSummon(PlayerId.Two, Hand(e, B), 0)).Events, ev => ev is SummonResponseRequested);
        var events = Negate(e);
        Assert.Contains(events, ev => ev is MonsterTributed { Monster.Player: PlayerId.One });
        Assert.Contains(events, ev => ev is SummonNegated);
        Assert.Null(Monster(e, PlayerId.Two, 0));
        Assert.Null(Monster(e, PlayerId.One, 0));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == B);
        Assert.Null(e.State.PendingSummon);
    }

    [Fact]
    public void HornOfHeavenNeedsAMonsterToTribute()
    {
        var e = Prepared([Horn], Deck(B), noMonster: true);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, B), 0));
        Assert.Null(e.State.PendingSummon);
        Assert.NotNull(Monster(e, PlayerId.Two, 0));
    }

    [Fact]
    public void BlackHornOfHeavenAnswersOnlyTheOpponentsSpecialSummon()
    {
        var e = Prepared([BlackHorn], Deck(B, S));
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, B), 0));
        Assert.Null(e.State.PendingSummon);
        Ok(e, new SpecialSummonByProcedure(PlayerId.Two, e.State.Revision, Procedure, Hand(e, S), 1));
        Assert.True(e.State.PendingSummon!.IsSpecialSummon);
        Assert.Contains(Negate(e), ev => ev is SummonNegated);
        Assert.Null(Monster(e, PlayerId.Two, 1));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == S);
    }

    [Fact]
    public void SolemnJudgmentPaysHalfAndNegatesASummon()
    {
        var e = Prepared([Solemn], Deck(B));
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, B), 0));
        Assert.Contains(Negate(e), ev => ev is LifePointsPaid { Amount: 4000 });
        Assert.Equal(4000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Null(Monster(e, PlayerId.Two, 0));
    }

    [Fact]
    public void SolemnJudgmentNegatesASpellActivation()
    {
        var e = Prepared([Solemn], Deck(DarkHole));
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, DarkHole), 0));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0));
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Number: 1, Reason: ChainSkipReason.ActivationNegated });
        Assert.NotNull(Monster(e, PlayerId.One, 0));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == DarkHole);
    }

    [Fact]
    public void APassedSummonCompletesAndThenOffersTrapHole()
    {
        var e = Prepared([Solemn, TrapHole], Deck(B));
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, B), 0));
        var pending = e.State.PendingSummon!;
        Ok(e, new PassSummonResponse(PlayerId.One, pending.DecisionId));
        var events = Ok(e, new PassSummonResponse(PlayerId.Two, e.State.PendingSummon!.DecisionId)).Events;
        Assert.Contains(events, ev => ev is MonsterNormalSummoned { Player: PlayerId.Two });
        Assert.Contains(events, ev => ev is SummonResponseWindowRequested { Responder: PlayerId.One });
        Assert.NotNull(e.State.OpenWindow!.Summoned);
    }

    [Fact]
    public void TheActionAdapterOffersTheSummonWindowToItsResponder()
    {
        var e = Prepared([BlackHorn, Solemn], Deck(B));
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, B), 0));
        var source = new DuelActionSource(e, Catalog());
        var view = source.Observe(PlayerId.One);
        Assert.Equal(ActionAvailability.Ready, view.Availability);
        Assert.Equal("SummonWindow", view.Decision!.Kind);
        // Black Horn answers only Special Summons, so only Solemn Judgment (zone index 1) and Pass are legal.
        Assert.Equal([ActionKind.ChainResponse, ActionKind.ChainPass], view.Actions.Select(a => a.Kind));
        Assert.Equal(1, view.Actions[0].Zone);
        Assert.Equal(ActionAvailability.Waiting, source.Observe(PlayerId.Two).Availability);
    }
}