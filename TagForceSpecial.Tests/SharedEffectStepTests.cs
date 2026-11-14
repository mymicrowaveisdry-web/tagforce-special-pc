using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Timed stat changes, flips by effect, extra Normal Summons, filtered costs and combined filters.</summary>
public sealed class SharedEffectStepTests
{
    private const uint W = 901, Winged = 902, High = 903, FishLow = 904, FishFour = 905, WarriorLow = 906, Watcher = 907, Storm = 910, Negate = 911, DiscardTwo = 912, Sword = 4307,
        Rush = 4905, Moon = 5432, Taiyou = 5431, Double = 7219, TradeIn = 7248, Excavation = 5688, Icarus = 6709, Surface = 9666;

    private static CardNumericCodes Codes(uint type) => new(MonsterAttributes.Earth, type, 1, 1, 0, 0, 0);

    private static CardCatalog Catalog() => new(new(null, "synthetic-shared-steps"),
        TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(0, 0, 1), Codes(0))).Concat(
        [
            new(W, "Warrior", "", new(1000, 1000, 4), Codes(MonsterTypes.Warrior)),
            new(Winged, "Winged", "", new(1000, 1000, 4), Codes(MonsterTypes.WingedBeast)),
            new(High, "High", "", new(2500, 2000, 8), Codes(MonsterTypes.Warrior)),
            new(FishLow, "Fish Low", "", new(500, 500, 3), Codes(MonsterTypes.Fish)),
            new(FishFour, "Fish Four", "", new(500, 500, 4), Codes(MonsterTypes.Fish)),
            new(WarriorLow, "Warrior Low", "", new(500, 500, 3), Codes(MonsterTypes.Warrior)),
            new(Watcher, "Watcher", "", new(500, 500, 3), Codes(MonsterTypes.Warrior)),
            new(Storm, "Storm", "", new(0, 0, 1), Codes(0)),
            new(Negate, "Negate", "", new(0, 0, 1), Codes(0)),
            new(DiscardTwo, "Discard Two", "", new(0, 0, 1), Codes(0))
        ]));

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        DuelCardRules monster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in new[] { W, Winged, High, FishLow, FishFour, WarriorLow, Watcher }) rules[id] = monster;
        rules[Storm] = rules[DiscardTwo] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Negate] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(Catalog(), rules, first, second, experimentalTriggerEffects:
            [.. TemplateCardFamily.Family.Triggers, new TriggerEffectProfile("watch", Watcher, MonsterEventKind.FlippedFaceUp, EffectRelation.Self, TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100)],
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions,
                new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("discard-two", DiscardTwo, EffectKind.NormalSpell, [new DealDamageStep(100)], cost: new DiscardCost(Count: 2)),
                ResponseProbe.Negate("negate", Negate, ResponseNegation.Activation, ChainSpellSpeed.Three)], shuffleSeed: 5);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), zone));
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int? Atk(DuelEngine e, PlayerId p, int zone) => e.GetMonsterStatistics(p, zone)!.Attack;

    private static void Target(DuelEngine e, PlayerId owner, FieldLocation location, int zone)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Player == owner && o.Location == location && o.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, owner, location, zone, t.Card.InstanceId));
    }

    private static void Discard(DuelEngine e, uint id)
    {
        var p = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions.First(c => c.GameCardId == id).InstanceId));
    }

    [Fact]
    public void TimedBonusAppliesAtResolutionAndEndsWithTheEndPhase()
    {
        var e = Engine(Deck(Rush), Deck());
        EnterMain(e); Summon(e, W, 0);
        Activate(e, Rush);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
        Resolve(e);
        Assert.Equal(1700, Atk(e, PlayerId.One, 0));
        Next(e);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void TimedBonusNeedsAFaceUpMonster()
    {
        var e = Engine(Deck(Rush), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Rush), 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void NegatedTimedBonusDoesNotApply()
    {
        var e = Engine(Deck(Rush), Deck(Negate));
        EnterMain(e); Next(e);
        SetCard(e, Negate, 0); Next(e);
        Summon(e, W, 0);
        Activate(e, Rush);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Resolve(e);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void FlippingFaceDownEndsATimedBonusAndDropsAnEquip()
    {
        var e = Engine(Deck(Rush, Moon, Sword), Deck());
        EnterMain(e); Summon(e, W, 0);
        Activate(e, Sword, 3);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Activate(e, Rush, 2);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Activate(e, Moon, 1);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Equal(BattlePosition.FaceDownDefense, P(e, PlayerId.One).MonsterZones[0]!.Position);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Sword);
        Assert.Empty(e.State.EventEffects.TimedModifiers);
    }

    [Fact]
    public void FlipByEffectFiresFlipTriggersButIsNotAFlipSummon()
    {
        var e = Engine(Deck(Taiyou, Watcher), Deck());
        EnterMain(e);
        Ok(e, new SetMonster(PlayerId.One, Hand(e, Watcher), 0));
        Activate(e, Taiyou);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        EventEffectScenario.Drain(e);
        Assert.Equal(BattlePosition.FaceUpAttack, P(e, PlayerId.One).MonsterZones[0]!.Position);
        Assert.Equal(8100, P(e, PlayerId.One).LifePoints);
        Assert.DoesNotContain(e.State.SummonHistory, r => r.Card.GameCardId == Watcher && r.Method == SummonMethod.Flip);
    }

    [Fact]
    public void FlipFaceUpNeedsAFaceDownMonster()
    {
        var e = Engine(Deck(Taiyou), Deck());
        EnterMain(e); Summon(e, W, 0);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Taiyou), 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void ExtraNormalSummonAllowsExactlyOneMoreThisTurn()
    {
        var e = Engine(Deck(Double), Deck());
        EnterMain(e); Summon(e, W, 0);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, W), 1), DuelError.NormalSummonLimitReached);
        Activate(e, Double); Resolve(e);
        Summon(e, W, 1);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, W), 2), DuelError.NormalSummonLimitReached);
    }

    [Fact]
    public void ExtraNormalSummonLastsOnlyThisTurnAndDoesNotStack()
    {
        var e = Engine(Deck(Double, Double), Deck());
        EnterMain(e);
        Activate(e, Double, 3); Resolve(e);
        Activate(e, Double, 4); Resolve(e);
        Summon(e, W, 0); Summon(e, W, 1);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, W), 2), DuelError.NormalSummonLimitReached);
        Next(e); Next(e);
        Summon(e, W, 2);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, W), 3), DuelError.NormalSummonLimitReached);
    }

    [Fact]
    public void FilteredDiscardOffersOnlyMatchingCards()
    {
        var blocked = Engine(Deck(TradeIn), Deck());
        EnterMain(blocked);
        Reject(blocked, new ActivateSpell(PlayerId.One, Hand(blocked, TradeIn), 4), DuelError.DecisionUnavailable);

        var e = Engine(Deck(TradeIn, High), Deck());
        EnterMain(e);
        int hand = P(e, PlayerId.One).Hand.Count;
        Activate(e, TradeIn);
        Assert.Equal(High, Assert.Single(e.State.PendingDecision!.CardOptions).GameCardId);
        Discard(e, High);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == High);
        Resolve(e);
        Assert.Equal(hand, P(e, PlayerId.One).Hand.Count); // Spell and discard out, two drawn in.
    }

    [Fact]
    public void CountedDiscardNeedsEnoughCardsAndTakesThemAll()
    {
        var blocked = Engine(Deck(DiscardTwo, Storm, Storm, Storm, Storm), Deck());
        EnterMain(blocked);
        for (int zone = 0; zone < 3; zone++) SetCard(blocked, Storm, zone);
        Reject(blocked, new ActivateSpell(PlayerId.One, Hand(blocked, DiscardTwo), 4), DuelError.DecisionUnavailable);

        var e = Engine(Deck(DiscardTwo), Deck());
        EnterMain(e);
        Activate(e, DiscardTwo);
        Discard(e, W);
        Assert.Equal(4 - 1, e.State.PendingDecision!.CardOptions.Count);
        Discard(e, W);
        Resolve(e);
        Assert.Equal(2, P(e, PlayerId.One).Graveyard.Count(c => c.GameCardId == W));
        Assert.Equal(7900, P(e, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void TargetsChosenAfterADiscardCanIncludeTheDiscardedCard()
    {
        var e = Engine(Deck(Excavation, Storm, Storm), Deck());
        EnterMain(e); Summon(e, W, 0);
        Activate(e, Excavation);
        Discard(e, Storm);
        Discard(e, W);
        Target(e, PlayerId.One, FieldLocation.Graveyard, -1);
        Resolve(e);
        Assert.Contains(P(e, PlayerId.One).Hand, c => c.GameCardId == Storm);
    }

    [Fact]
    public void FilteredTributeOffersOnlyMatchingMonstersAndStaysPaidWhenNegated()
    {
        var blocked = Engine(Deck(Icarus), Deck(Storm));
        EnterMain(blocked); Summon(blocked, W, 0); SetCard(blocked, Icarus, 0); Next(blocked);
        SetCard(blocked, Storm, 0); Next(blocked);
        Reject(blocked, new ActivateSetSpell(PlayerId.One, 0), DuelError.DecisionUnavailable);

        var e = Engine(Deck(Icarus, Winged), Deck(Storm, Negate));
        EnterMain(e); Summon(e, Winged, 0); SetCard(e, Icarus, 0); Next(e);
        Summon(e, W, 0); SetCard(e, Negate, 0); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Assert.Single(e.State.PendingDecision!.FieldTargetOptions!);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Winged);
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Target(e, PlayerId.Two, FieldLocation.SpellTrapZone, 0);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Resolve(e);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Winged);
        Assert.NotNull(P(e, PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void CombinedFiltersRequireEveryConditionAndAnyOfTheTypes()
    {
        var e = Engine(Deck(Surface, FishLow, FishFour, WarriorLow), Deck(Storm, Storm, Storm));
        EnterMain(e); Summon(e, FishLow, 0); Next(e); Next(e);
        Summon(e, FishFour, 1); Next(e); Next(e);
        Summon(e, WarriorLow, 2); Next(e);
        Activate(e, Storm); Resolve(e); Next(e);
        Activate(e, Surface);
        Assert.Equal(FishLow, Assert.Single(e.State.PendingDecision!.FieldTargetOptions!).Card.GameCardId);
    }

    [Fact]
    public void FiltersAndCostsValidateTheirArguments()
    {
        Assert.Throws<ArgumentException>(() => new AnyOfFilter(new AnyMonsterFilter()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonsterLevelFilter(4, minLevel: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectDefinition("x", Storm, EffectKind.NormalSpell, [new DealDamageStep(1)], cost: new DiscardCost(Count: 6)));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Storm, EffectKind.NormalSpell, [new ModifyTargetsUntilEndStep(0)], fieldTargets: [new FieldTarget(FieldCardKind.Monster)]));
    }
}
