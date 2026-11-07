using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>
/// Synthetic per-card wiring of the Stage 10C-2 batch through the production definitions, including
/// interactions with an ordinary monster and a preview Spell. No PSP observation is claimed here.
/// </summary>
public sealed class ChainCardFamilyTests
{
    private const uint Filler = 4012, Big = 60, Burn = 4349, Krebons = 7716;
    private const uint Scapegoat = ChainCardFamily.Scapegoat, Jammer = ChainCardFamily.MagicJammer,
        Typhoon = ChainCardFamily.MysticalSpaceTyphoon, Vortex = ChainCardFamily.LightningVortex,
        Teleport = ChainCardFamily.EmergencyTeleport;

    private static CardCatalog Catalog() => new(new(null, "synthetic-chain-card-family"),
        new uint[] { Scapegoat, Jammer, Typhoon, Vortex, Teleport, Filler, Big, Burn, Krebons }.Concat(ChainCardFamily.SheepTokenIds)
            .Select(id => new CardDefinition(id, $"Probe {id}", "",
                new(id == Big ? 2400 : 1000, 1000, id == Big ? 6u : id == Krebons ? 2u : id == Filler ? 4u : 0u))));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = ChainCardFamily.Rules.ToDictionary();
        rules[Filler] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Big] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true);
        rules[Krebons] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        rules[Burn] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(Catalog(), rules, first, second,
            effectDefinitions: [.. ChainCardFamily.Definitions, .. PlayablePreview.Definitions.Where(d => d.CardId == Burn)],
            shuffleSeed: 3);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
    private static CardInstance Hand(DuelEngine e, PlayerId p, uint id) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);
    private static long ChainId(DuelEngine e) => e.State.PendingChain!.DecisionId;

    private static void Answer(DuelEngine e)
    {
        var p = e.State.PendingDecision!;
        if (p.Kind == DecisionKind.Card) Ok(e, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
        else Ok(e, new ChooseDecisionTarget(p.Player, p.DecisionId, p.TargetOptions[0].Player, p.TargetOptions[0].Zone,
            p.TargetOptions[0].Card.InstanceId, p.TargetOptions[0].IsPendulumZone));
    }

    [Fact]
    public void ScapegoatSummonsFourSheepAndLocksOtherSummonsButNotSetsForThatTurnOnly()
    {
        var e = Engine(Deck(Scapegoat, Filler, Filler, Big, Filler), Deck());
        EnterMain(e);
        var activated = Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Scapegoat).InstanceId, 0));
        Assert.Equal(PlayerId.One, Assert.Single(activated.Events.OfType<SummonLockApplied>()).Lock.Player);
        var events = Resolve(e);
        var tokens = events.OfType<TokenSpecialSummoned>().ToArray();
        Assert.Equal(ChainCardFamily.SheepTokenIds, tokens.Select(t => t.Monster.Card.GameCardId));
        Assert.Equal([0, 1, 2, 3], tokens.Select(t => t.Zone));
        Assert.All(tokens, t => Assert.Equal(BattlePosition.FaceUpDefense, t.Monster.Position));
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Filler).InstanceId, 4), DuelError.SummonLocked);
        Ok(e, new SetMonster(PlayerId.One, Hand(e, PlayerId.One, Filler).InstanceId, 4)); // Normal Set stays legal
        Next(e); Next(e);
        // The lock has expired; Sheep Tokens still cannot be Tributed for a Tribute Summon.
        var sheep = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!;
        var set = e.State.GetPlayer(PlayerId.One).MonsterZones[4]!;
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Big).InstanceId, 0,
            [new MonsterBinding(PlayerId.One, 0, sheep.Card, sheep.PlacementId)]), DuelError.TributeNotAllowed);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Big).InstanceId, 4,
            [new MonsterBinding(PlayerId.One, 4, set.Card, set.PlacementId)]));
    }

    [Fact]
    public void ScapegoatNeedsNoSummonThisTurnAndItsLockOutlastsNegation()
    {
        var summoned = Engine(Deck(Scapegoat), Deck());
        EnterMain(summoned);
        Ok(summoned, new NormalSummon(PlayerId.One, Hand(summoned, PlayerId.One, Filler).InstanceId, 0));
        Reject(summoned, new ActivateSpell(PlayerId.One, Hand(summoned, PlayerId.One, Scapegoat).InstanceId, 0), DuelError.ActivationConditionNotMet);

        var e = Engine(Deck(Scapegoat), Deck(Jammer));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, Jammer).InstanceId, 0));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Scapegoat).InstanceId, 0));
        Ok(e, new ActivateChainResponse(PlayerId.Two, ChainId(e), 0));
        Answer(e); // Magic Jammer's discard
        var events = Resolve(e);
        Assert.Empty(events.OfType<TokenSpecialSummoned>());
        Assert.Contains(events, ev => ev is ChainLinkNegated { Negation: ChainNegation.Activation });
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Scapegoat);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Filler).InstanceId, 0), DuelError.SummonLocked);
        Ok(e, new SetMonster(PlayerId.One, Hand(e, PlayerId.One, Filler).InstanceId, 0));
    }

    [Fact]
    public void EmergencyTeleportCannotSummonUnderAnotherCardsLock()
    {
        var e = Engine(Deck(Scapegoat, Teleport, Krebons), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Scapegoat).InstanceId, 0));
        Resolve(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Teleport).InstanceId, 1), DuelError.SummonLocked);
    }

    [Fact]
    public void TyphoonFromHandAnswersTheTurnPlayersChainAndOnlyTheTurnPlayerMayUseTheHand()
    {
        var e = Engine(Deck(Burn, Typhoon), Deck(Typhoon));
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Burn).InstanceId, 0));
        Reject(e, new ActivateHandChainResponse(PlayerId.Two, ChainId(e), Hand(e, PlayerId.Two, Typhoon).InstanceId, 0),
            DuelError.ActivationTimingNotAllowed);
        Ok(e, new PassChainResponse(PlayerId.Two, ChainId(e)));
        Reject(e, new ActivateHandChainResponse(PlayerId.One, ChainId(e), Hand(e, PlayerId.One, Typhoon).InstanceId, 0),
            DuelError.SpellTrapZoneOccupied);
        Ok(e, new ActivateHandChainResponse(PlayerId.One, ChainId(e), Hand(e, PlayerId.One, Typhoon).InstanceId, 1));
        var target = Assert.Single(e.State.PendingDecision!.TargetOptions);
        Assert.Equal(Burn, target.Card.GameCardId);
        Answer(e);
        Assert.Equal(2, e.State.PendingChain!.Links.Count);
        var events = Resolve(e);
        Assert.Single(events.OfType<SpellTrapDestroyed>());
        Assert.Equal(7800, e.State.GetPlayer(PlayerId.Two).LifePoints); // destroying the source does not negate it
        Assert.Equal([Burn, Typhoon], e.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
    }

    [Fact]
    public void TyphoonInTheOpenWindowDestroysASetCardAndThePhaseRequestLapses()
    {
        var e = Engine(Deck(Burn), Deck(Typhoon));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Burn).InstanceId, 0));
        Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, Typhoon).InstanceId, 0));
        Phase(e, DuelPhase.End);
        Ok(e, new EndTurn(PlayerId.Two));
        Ok(e, new ResolveDrawStep(PlayerId.One));
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main1));
        var window = e.State.OpenWindow!;
        Assert.Equal(PlayerId.Two, window.Responder);
        Ok(e, new ActivateChainResponse(PlayerId.Two, window.DecisionId, 0));
        Assert.Equal(PlayerId.Two, e.State.PendingDecision!.Player); // the non-turn player chooses the target
        Answer(e);
        Resolve(e);
        Assert.Null(e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]);
        Assert.Equal(DuelPhase.Standby, e.State.Phase);
        var again = Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main1));
        Assert.Equal(new PhaseChanged(DuelPhase.Standby, DuelPhase.Main1), Assert.Single(again.Events));
    }

    [Fact]
    public void MagicJammerNegatesAndDestroysAPreviewSpellWithoutRefundingEitherCost()
    {
        var e = Engine(Deck(Burn), Deck(Jammer));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, Jammer).InstanceId, 0));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Burn).InstanceId, 0));
        Ok(e, new ActivateChainResponse(PlayerId.Two, ChainId(e), 0));
        var cost = e.State.PendingDecision!.CardOptions[0];
        Answer(e);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is SpellTrapDestroyed { Card.GameCardId: Burn });
        Assert.Empty(events.OfType<EffectDamageDealt>());
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal([cost.GameCardId, Jammer], e.State.GetPlayer(PlayerId.Two).Graveyard.Select(c => c.GameCardId));
        Assert.Equal([Burn], e.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
    }

    [Fact]
    public void VortexDestroysOnlyFaceUpOpponentMonstersAndSheepTokensCeaseToExist()
    {
        var e = Engine(Deck(Vortex), Deck(Scapegoat));
        EnterMain(e); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Scapegoat).InstanceId, 0));
        Resolve(e);
        Ok(e, new SetMonster(PlayerId.Two, Hand(e, PlayerId.Two, Filler).InstanceId, 4));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Vortex).InstanceId, 0));
        Answer(e); // discard
        var events = Resolve(e);
        Assert.Equal(4, events.OfType<MonsterDestroyed>().Count());
        Assert.Equal(4, events.OfType<TokenRemoved>().Count());
        var opponent = e.State.GetPlayer(PlayerId.Two);
        Assert.Equal(Filler, opponent.MonsterZones[4]!.Card.GameCardId); // face-down survives
        Assert.All(opponent.MonsterZones.Take(4), Assert.Null);
        Assert.Equal([Scapegoat], opponent.Graveyard.Select(c => c.GameCardId)); // Tokens go to no zone
        Assert.Empty(opponent.Banished);
    }

    [Fact]
    public void EmergencyTeleportOffersHandAndDeckOnlyToItsControllerAndShufflesAfterADeckSummon()
    {
        var e = Engine(Deck(Teleport, Krebons, Filler, Filler, Filler, Filler, Krebons), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Teleport).InstanceId, 0));
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, ChainId(e)));
        Ok(e, new ResolveNextChainLink(PlayerId.One, ChainId(e)));
        var source = new DuelActionSource(e, Catalog());
        var own = source.Observe(PlayerId.One);
        Assert.Equal("DefinitionSummon", own.Decision!.Kind);
        Assert.Equal(2, own.Decision.Cards.Count);
        Assert.Equal(2 * 5 * 2, own.Actions.Count);
        Assert.All(own.Actions, a => Assert.Equal(ActionKind.ChooseCard, a.Kind));
        var other = source.Observe(PlayerId.Two);
        Assert.Equal(ActionAvailability.Waiting, other.Availability);
        Assert.Null(other.Decision);
        var choice = e.State.PendingChain!.DefinitionSummon!;
        var deck = choice.Options.Single(o => o.Source == DecisionCardSource.Deck).Card;
        var result = Ok(e, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, deck.InstanceId, 2, BattlePosition.FaceUpDefense));
        Assert.Single(result.Events.OfType<DeckShuffled>());
        Assert.Equal(deck, e.State.GetPlayer(PlayerId.One).MonsterZones[2]!.Card);
        var end = Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(deck, Assert.Single(end.Events.OfType<MonsterBanished>()).Card);
    }

    [Fact]
    public void BatchMetadataRulesAndDefinitionsAgree()
    {
        Assert.Equal(5, ChainCardFamily.Cards.Count);
        foreach (var card in ChainCardFamily.Cards)
        {
            var rule = ChainCardFamily.Rules[card.CardId];
            Assert.Equal(card.Kind == CardKind.Trap ? CardCategory.Trap : CardCategory.Spell, rule.Category);
            Assert.True(rule.AllowsSpellTrapSet);
            Assert.Equal(card.CardId, card.Definition.CardId);
            Assert.Equal([card.Definition.EffectId], ReviewedCardMetadata.Reviewed[card.CardId].Effects.References);
        }
        Assert.All(ChainCardFamily.SheepTokenIds, id =>
        {
            var rule = ChainCardFamily.Rules[id];
            Assert.True(rule.IsToken);
            Assert.False(rule.AllowsTributeForSummon);
            Assert.Contains(id, TfspTokens.Ids);
        });
        Assert.Equal(24, ChainCardFamily.LevelThreeOrLowerPsychicIds.Distinct().Count());
        Assert.DoesNotContain(10172u, ChainCardFamily.LevelThreeOrLowerPsychicIds);
        Assert.DoesNotContain(10466u, ChainCardFamily.LevelThreeOrLowerPsychicIds);
        Assert.Contains(Krebons, ChainCardFamily.LevelThreeOrLowerPsychicIds);
    }
}
