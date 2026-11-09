using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Graveyard family wiring through the production definitions; Graveyards are filled with Foolish Burial.</summary>
public sealed class GraveyardFamilyTests
{
    private const uint N1 = 4012, N2 = 4030, Effect = 7716, High = 4007, Burial = 5236, DarkHole = 4342,
        Reborn = 4842, Reincarnation = 6213, Factory = 6129, Avarice = 6511, Prophecy = 7010, Release = 4857,
        BurialDD = 6941, Rules = 6995;

    private static CardCatalog Catalog() => new(new(null, "synthetic-graveyard"),
        GraveyardFamily.Cards.Select(c => c.CardId).Concat([N1, N2, Effect, High, Burial, DarkHole])
            .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == High ? 6u : id is N1 or N2 or Effect ? 4u : 0u))));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = GraveyardFamily.Rules.ToDictionary();
        foreach (uint monster in new[] { N1, N2, Effect, High })
            rules[monster] = new(CardCategory.Monster, AllowsNormalSummon: monster != High, AllowsBasicBattle: true);
        rules[Burial] = DeckOperationFamily.Rules[Burial];
        rules[DarkHole] = RemovalFamily.Rules[DarkHole];
        return new(Catalog(), rules, first, second, effectDefinitions: [.. GraveyardFamily.Definitions,
            .. DeckOperationFamily.Definitions.Where(d => d.CardId == Burial), .. RemovalFamily.Definitions.Where(d => d.CardId == DarkHole)],
            shuffleSeed: 9);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(N1, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static CardInstance Hand(DuelEngine e, PlayerId p, uint id) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, Turn(e), id).InstanceId, zone));
    private static long ChainId(DuelEngine e) => e.State.PendingChain!.DecisionId;
    private static IReadOnlyList<CardInstance> Grave(DuelEngine e, PlayerId p) => e.State.GetPlayer(p).Graveyard;

    private static void PassAndResolveToChoice(DuelEngine e)
    {
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, ChainId(e)));
        Ok(e, new ResolveNextChainLink(Turn(e), ChainId(e)));
    }

    /// <summary>Foolish Burial sends the first Deck copy of <paramref name="id"/> to the Graveyard.</summary>
    private static void Bury(DuelEngine e, uint id)
    {
        Activate(e, Burial);
        PassAndResolveToChoice(e);
        var choice = e.State.PendingChain!.DefinitionCardChoice!;
        Ok(e, new ChooseDefinitionCard(Turn(e), choice.DecisionId, choice.Options.First(c => c.GameCardId == id).InstanceId));
    }

    private static void Target(DuelEngine e, PlayerId owner, uint id, int skip = 0)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.Where(o => o.Player == owner && o.Card.GameCardId == id).Skip(skip).First();
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, t.Player, t.Location, t.Zone, t.Card.InstanceId));
    }

    [Fact]
    public void MonsterRebornRevivesFromEitherGraveyardAndTheOwnerKeepsTheCard()
    {
        var e = Engine(Deck(Reborn, Reborn, DarkHole, Burial), Deck(Burial, N1, N1, N1, N1, N1, N1, N2)); // N2 stays in the Deck
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Reborn).InstanceId, 4), DuelError.DecisionUnavailable);
        Bury(e, N1);
        Activate(e, Reborn);
        Target(e, PlayerId.One, N1);
        PassAndResolveToChoice(e);
        var summon = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal(DecisionCardSource.Graveyard, Assert.Single(summon.Options).Source);
        var placed = Ok(e, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, summon.Options[0].Card.InstanceId, 0, BattlePosition.FaceUpDefense));
        Assert.Equal(DecisionCardSource.Graveyard, Assert.Single(placed.Events.OfType<MonsterSpecialSummoned>()).Source);
        Assert.DoesNotContain(Grave(e, PlayerId.One), c => c.GameCardId == N1);
        Next(e);
        Bury(e, N2);
        Next(e);
        Activate(e, Reborn);
        var options = e.State.PendingDecision!.FieldTargetOptions!;
        Assert.Contains(options, o => o.Player == PlayerId.Two && o.Card.GameCardId == N2 && o.Location == FieldLocation.Graveyard);
        var actions = new DuelActionSource(e, Catalog()).Observe(PlayerId.One).Actions;
        Assert.All(actions, a => Assert.NotNull(a.CardInstanceId)); // Graveyard cards are public
        Target(e, PlayerId.Two, N2);
        PassAndResolveToChoice(e);
        summon = e.State.PendingChain!.DefinitionSummon!;
        Ok(e, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, summon.Options[0].Card.InstanceId, 1, BattlePosition.FaceUpAttack));
        var revived = e.State.GetPlayer(PlayerId.One).MonsterZones[1]!;
        Assert.Equal((N2, PlayerId.Two), (revived.Card.GameCardId, revived.Card.Owner));
        Activate(e, DarkHole);
        Resolve(e);
        Assert.Contains(Grave(e, PlayerId.Two), c => c == revived.Card); // back to its owner's Graveyard
    }

    [Fact]
    public void MonsterReincarnationCanTargetTheMonsterItJustDiscarded()
    {
        var e = Engine(Deck(Reincarnation, Effect), Deck());
        EnterMain(e); // The Graveyard is empty: the card about to be discarded is the only possible target.
        Activate(e, Reincarnation);
        var cost = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(PlayerId.One, cost.DecisionId, cost.CardOptions.First(c => c.GameCardId == Effect).InstanceId));
        Target(e, PlayerId.One, Effect);
        var events = Resolve(e);
        Assert.Equal(Effect, Assert.Single(events.OfType<CardMovedToHand>()).Card.GameCardId);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Effect);
    }

    [Fact]
    public void DarkFactoryOffersOnlyReviewedNormalMonsters()
    {
        var e = Engine(Deck(Factory, Burial, Burial, Burial, N1, Effect, N2), Deck());
        EnterMain(e);
        Bury(e, N1); Bury(e, Effect);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Factory).InstanceId, 4), DuelError.DecisionUnavailable);
        Bury(e, N2);
        Activate(e, Factory);
        Assert.DoesNotContain(e.State.PendingDecision!.FieldTargetOptions!, o => o.Card.GameCardId == Effect);
        Target(e, PlayerId.One, N1); Target(e, PlayerId.One, N2);
        Assert.Equal(2, Resolve(e).OfType<CardMovedToHand>().Count());
    }

    [Fact]
    public void PotOfAvariceDrawsOnlyWhenAllFiveTargetsReturn()
    {
        uint[] deck = [Avarice, Burial, Burial, Burial, Burial, Burial, Burial, Burial, Burial, Burial, Burial, Burial];
        var e = Engine(Deck(deck), Deck(Prophecy));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, Prophecy).InstanceId, 0));
        Next(e);
        for (int i = 0; i < 5; i++) Bury(e, N1);
        int deckBefore = e.State.GetPlayer(PlayerId.One).Deck.Count;
        Activate(e, Avarice);
        for (int i = 0; i < 5; i++) Target(e, PlayerId.One, N1);
        var avarice = e.State.CommittedActivation!.FieldTargets!;
        // The opponent shuffles two of the five away first.
        Ok(e, new ActivateChainResponse(PlayerId.Two, ChainId(e), 0));
        foreach (var t in avarice.Take(2))
        {
            var p = e.State.PendingDecision!;
            Ok(e, new ChooseDecisionFieldTarget(PlayerId.Two, p.DecisionId, t.Player, t.Location, t.Zone, t.Card.InstanceId));
        }
        var events = Resolve(e);
        Assert.Equal(5, events.OfType<CardShuffledIntoDeck>().Count()); // 2 by the Prophecy, 3 by Avarice
        Assert.Empty(events.OfType<CardDrawn>());
        Assert.Equal(deckBefore + 5, e.State.GetPlayer(PlayerId.One).Deck.Count);
    }

    [Fact]
    public void PotOfAvariceShufflesFiveAndDrawsTwo()
    {
        var e = Engine(Deck(Avarice, Burial, Burial, Burial, Burial, Burial), Deck());
        EnterMain(e); Next(e); Next(e); // the turn-3 draw is the fifth Foolish Burial
        for (int i = 0; i < 5; i++) Bury(e, N1);
        int deck = e.State.GetPlayer(PlayerId.One).Deck.Count;
        Activate(e, Avarice);
        for (int i = 0; i < 5; i++) Target(e, PlayerId.One, N1);
        var events = Resolve(e);
        Assert.Single(events.OfType<DeckShuffled>());
        Assert.Equal(2, events.OfType<CardDrawn>().Count());
        Assert.Equal(deck + 3, e.State.GetPlayer(PlayerId.One).Deck.Count);
    }

    [Fact]
    public void SoulReleaseStopsOnDoneAndBurialFromADifferentDimensionReturnsThem()
    {
        var e = Engine(Deck(Release, BurialDD, Burial, Burial, Burial), Deck());
        EnterMain(e);
        Bury(e, N1); Bury(e, N1); Bury(e, N1);
        Activate(e, Release);
        Target(e, PlayerId.One, N1);
        Assert.True(e.State.PendingDecision!.CanFinish);
        Target(e, PlayerId.One, N1);
        var source = new DuelActionSource(e, Catalog());
        Assert.Contains(source.Observe(PlayerId.One).Actions, a => a.Kind == ActionKind.FinishChoice);
        Ok(e, new FinishDecision(PlayerId.One, e.State.PendingDecision!.DecisionId));
        Assert.Equal(2, e.State.CommittedActivation!.FieldTargets!.Count);
        Assert.Equal(2, Resolve(e).OfType<CardBanishedFromGraveyard>().Count());
        Assert.Equal(2, e.State.GetPlayer(PlayerId.One).Banished.Count);
        Activate(e, BurialDD);
        Target(e, PlayerId.One, N1); Target(e, PlayerId.One, N1); // the banished zone is now empty: the choices end
        Assert.Null(e.State.PendingDecision);
        Assert.Equal(2, Resolve(e).OfType<CardReturnedToGraveyard>().Count());
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Banished);
    }

    [Fact]
    public void TheTransmigrationProphecyShufflesCardsIntoEachOwnersDeck()
    {
        var e = Engine(Deck(Prophecy, Burial), Deck(Burial));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Prophecy).InstanceId, 0));
        Bury(e, N1);
        Next(e); Bury(e, N1); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Target(e, PlayerId.One, N1); Target(e, PlayerId.Two, N1);
        var events = Resolve(e);
        Assert.Equal([PlayerId.One, PlayerId.Two], events.OfType<DeckShuffled>().Select(s => s.Player));
        Assert.DoesNotContain(Grave(e, PlayerId.Two), c => c.GameCardId == N1);
    }

    [Fact]
    public void AncientRulesSummonsAHighLevelNormalMonsterFromTheHand()
    {
        var e = Engine(Deck(Rules, High), Deck());
        EnterMain(e);
        Activate(e, Rules);
        PassAndResolveToChoice(e);
        var choice = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal(High, Assert.Single(choice.Options).Card.GameCardId);
        Ok(e, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, choice.Options[0].Card.InstanceId, 2, BattlePosition.FaceUpAttack));
        Assert.Equal(High, e.State.GetPlayer(PlayerId.One).MonsterZones[2]!.Card.GameCardId);
    }

    [Fact]
    public void ZoneTargetsValidate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyCardFilter(), 2, 1));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new BanishTargetsStep()],
            fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyCardFilter(), 1, 3),
                new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyCardFilter())])); // only the last may vary
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new SpecialSummonTargetStep()],
            fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter())])); // must be summonable
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DestroyTargetsStep()],
            fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyCardFilter())])); // field step, zone target
        Assert.Equal(8, GraveyardFamily.Cards.Count);
    }
}
