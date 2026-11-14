using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Triggers on a change of control, and triggers and responses that answer an attack, a Summon or damage.</summary>
public sealed class TriggerWindowTests
{
    private const uint W = 1061, Rock = 1062, Big = 1063, Fish = 1064, Xyz = 1065, Dump = 1066, Burn = 1067, SpellOfPain = 5486, Barrel = 5543,
        Solemn = 4861, BattleFader = 8617, Gargoyle = 10898, Scarecrow = 9078, ChangeOfHeart = 4860, Yata = 5375, Ameba = 4490, Griggle = 4553, Kunoichi = 7725, Intercept = 7765, SplashCapture = 9941;
    private const string Rank4 = "trigger-windows-rank-4";

    private static DuelEngine Engine(uint[] first, uint[] second, uint[]? secondExtra = null)
    {
        CardCatalog catalog = new(new(null, "synthetic-trigger-windows"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Rock, "Rock", "", new(1200, 1000, 4), new(5, MonsterTypes.Rock, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Big, "Big", "", new(2400, 1000, 6), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Fish, "Fish", "", new(1000, 1000, 4), new(3, MonsterTypes.Fish, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Xyz, "Xyz", "", new(2000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Dump, "Dump", "", new(0, 0, 1)))
                .Append(new CardDefinition(Burn, "Burn", "", new(0, 0, 1)))
                .Append(new CardDefinition(Solemn, "Solemn Judgment", "", new(0, 0, 1))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[Solemn] = SummonNegationFamily.Rules[Solemn];
        rules[W] = rules[Rock] = rules[Fish] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true);
        rules[Big] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true, AllowsTributeForSummon: true);
        rules[Xyz] = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsExtraDeck: true);
        rules[Dump] = rules[Burn] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        EffectDefinition burn = new("test-burn", Burn, EffectKind.NormalSpell, [new DealDamageStep(1000)]);
        var fish = new MonsterTypeFilter([MonsterTypes.Fish]);
        EffectDefinition dump = new("test-dump", Dump, EffectKind.NormalSpell, [new SendFromDeckToGraveyardStep(fish), new SendFromDeckToGraveyardStep(fish)]);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, dump, burn, SummonNegationFamily.Definitions.Single(d => d.CardId == Solemn)], shuffleSeed: 7, ruleLocks: TemplateCardFamily.Family.RuleLocks,
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules,
            experimentalSpecialSummonCards: [new SpecialSummonCardProfile(Xyz, SpecialSummonSources.None, Rank4)], playerTwoExtraDeck: secondExtra,
            experimentalMaterialProcedures: [new ExtraDeckMaterialProcedureProfile(Rank4, [Xyz], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(4), anyMaterial: true)]);
    }

    private static MonsterBinding Bind(DuelEngine e, PlayerId p, int zone) => new(p, zone, P(e, p).MonsterZones[zone]!.Card, P(e, p).MonsterZones[zone]!.PlacementId);

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;

    /// <summary>Answers decisions with the first option and resolves every trigger and chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingDecision is { FieldTargetOptions: [var target, ..] } d
                    ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId)
                : e.State.PendingDecision is { } card ? new ChooseDecisionCard(card.Player, card.DecisionId, card.CardOptions[0].InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.DefinitionSummon is { } summon ? new ChooseDefinitionSummon(summon.Player, summon.DecisionId, summon.Options[0].Card.InstanceId, summon.MonsterZoneOptions[0], BattlePosition.FaceUpAttack)
                : chain.DefinitionCardChoice is { } choice ? new ChooseDefinitionCard(choice.Player, choice.DecisionId, choice.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Player Two Normal Summons (or Sets) <paramref name="opponent"/>; then it is Player One's Main Phase 1 of turn 3.</summary>
    private static DuelEngine Ready(uint[] first, uint opponent, bool set = false)
    {
        var e = Engine(first, Deck(opponent));
        EnterMain(e); Next(e);
        int card = Hand(e, PlayerId.Two, opponent);
        Ok(e, set ? new SetMonster(PlayerId.Two, card, 0) : new NormalSummon(PlayerId.Two, card, 0)); Next(e);
        return e;
    }

    private static void TakeWithChangeOfHeart(DuelEngine e)
    {
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, ChangeOfHeart), 0));
        Drive(e);
    }

    [Fact]
    public void ASpiritThatInflictsBattleDamageSkipsTheOpponentsNextDrawAndReturnsToTheHand()
    {
        var e = Engine(Deck(Yata), Deck());
        EnterMain(e); Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Yata), 0));
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.One, 0, null));
        S.FinishAttack(e);
        Assert.Equal(7000, P(e, PlayerId.Two).LifePoints);
        while (e.State.Battle!.Step != BattleStep.End) { S.Close(e); S.Advance(e); }
        S.Close(e);
        Phase(e, DuelPhase.Main2); Phase(e, DuelPhase.End);
        Drive(e);
        Assert.Contains(P(e, PlayerId.One).Hand, c => c.GameCardId == Yata);
        int deck = P(e, PlayerId.Two).Deck.Count;
        Ok(e, new EndTurn(PlayerId.One)); EnterMain(e);
        Assert.Equal(deck, P(e, PlayerId.Two).Deck.Count);
    }

    [Fact]
    public void TakingAmebaDamagesThePlayerWhoGainedItOnlyOnceWhileItStaysFaceUp()
    {
        var e = Ready(Deck(ChangeOfHeart), Ameba);
        TakeWithChangeOfHeart(e);
        Assert.Equal(6000, P(e, PlayerId.One).LifePoints);
        Phase(e, DuelPhase.End);
        Drive(e);
        Assert.Equal(Ameba, P(e, PlayerId.Two).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal((6000, 8000), (P(e, PlayerId.One).LifePoints, P(e, PlayerId.Two).LifePoints));
    }

    [Fact]
    public void TakingGriggleGivesItsFormerControllerLifePoints()
    {
        var e = Ready(Deck(ChangeOfHeart), Griggle);
        TakeWithChangeOfHeart(e);
        Assert.Equal((8000, 11000), (P(e, PlayerId.One).LifePoints, P(e, PlayerId.Two).LifePoints));
    }

    [Fact]
    public void EachNewControllerOfKunoichiDiscardsARandomCard()
    {
        var e = Ready(Deck(ChangeOfHeart), Kunoichi);
        int hand = P(e, PlayerId.One).Hand.Count;
        TakeWithChangeOfHeart(e);
        Assert.Equal(hand - 2, P(e, PlayerId.One).Hand.Count);
        int theirs = P(e, PlayerId.Two).Hand.Count;
        Phase(e, DuelPhase.End);
        var events = Drive(e);
        Assert.Contains(events, ev => ev is CardDiscarded { Player: PlayerId.Two });
        Assert.Equal(theirs - 1, P(e, PlayerId.Two).Hand.Count);
    }

    [Fact]
    public void ACounterTrapTakesAMonsterTributeSummonedWithOneTributeButNotOneSummonedWithout()
    {
        var e = Engine(Deck(Intercept), Deck(W, Big));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Intercept), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Assert.Null(e.State.OpenWindow);
        Next(e); Next(e);
        Ok(e, new TributeSummon(PlayerId.Two, e.State.Revision, Hand(e, PlayerId.Two, Big), 0, [Bind(e, PlayerId.Two, 0)]));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.OpenWindow!.DecisionId, 0));
        Drive(e);
        Assert.All(P(e, PlayerId.Two).MonsterZones, Assert.Null);
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Big);
    }

    [Fact]
    public void ATrapTakesAnXyzMonsterTheOpponentXyzSummonsAfterPayingItsCost()
    {
        var e = Engine(Deck(Dump, SplashCapture, W, W, W, W, Fish, Fish), Deck(W, W, W), secondExtra: [Xyz]);
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Dump), 0)); Drive(e);
        Assert.Equal(2, P(e, PlayerId.One).Graveyard.Count(c => c.GameCardId == Fish));
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, SplashCapture), 1)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Assert.Null(e.State.OpenWindow);
        Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 1));
        Assert.Null(e.State.OpenWindow);
        Ok(e, new SpecialSummonWithMaterials(PlayerId.Two, e.State.Revision, Rank4, P(e, PlayerId.Two).ExtraDeck.Single().InstanceId, 0,
            [Bind(e, PlayerId.Two, 0), Bind(e, PlayerId.Two, 1)]));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.OpenWindow!.DecisionId, 1));
        Drive(e);
        Assert.Equal(2, P(e, PlayerId.One).Banished.Count(c => c.GameCardId == Fish));
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Xyz);
    }

    /// <summary>Player Two Normal Summons a Warrior and enters the Battle Step on turn 2.</summary>
    private static DuelEngine OpponentReadyToAttack(uint[] first)
    {
        var e = Engine(first, Deck());
        EnterMain(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Phase(e, DuelPhase.Battle); S.Ready(e);
        return e;
    }

    [Fact]
    public void AMonsterInTheHandAnswersADirectAttackEndsTheBattlePhaseAndIsBanishedWhenItLeaves()
    {
        var e = OpponentReadyToAttack(Deck(BattleFader));
        Ok(e, new DeclareAttack(PlayerId.Two, 0));
        Drive(e);
        Assert.Equal(BattleFader, P(e, PlayerId.One).MonsterZones.Single(m => m is not null)!.Card.GameCardId);
        Assert.Equal(BattleStep.End, e.State.Battle!.Step);
        Assert.Equal(8000, P(e, PlayerId.One).LifePoints);
        S.Close(e); Next(e); Next(e);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, P(e, PlayerId.One).MonsterZones.ToList().FindIndex(m => m is not null)));
        var events = S.FinishAttack(e);
        Assert.Contains(events, ev => ev is CardBanishedOnLeavingField { Card.GameCardId: BattleFader });
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == BattleFader);
        Assert.DoesNotContain(P(e, PlayerId.One).Graveyard, c => c.GameCardId == BattleFader);
    }

    [Fact]
    public void AHandTriggerForADirectAttackIgnoresAnAttackOnAMonster()
    {
        var e = Engine(Deck(BattleFader), Deck());
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Contains(P(e, PlayerId.One).Hand, c => c.GameCardId == BattleFader);
    }

    [Fact]
    public void AMonsterInTheHandIsSummonedWhenItsPlayerNormalSummonsAMatchingMonsterOnly()
    {
        var e = Engine(Deck(Gargoyle, Rock), Deck());
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Rock), 1));
        Drive(e);
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Gargoyle);
    }

    [Fact]
    public void AQuickEffectDiscardedFromTheHandNegatesADirectAttackAndEndsTheBattlePhase()
    {
        var e = OpponentReadyToAttack(Deck(Scarecrow));
        Ok(e, new DeclareAttack(PlayerId.Two, 0));
        while (e.State.Battle!.Window.Responder != PlayerId.One) Ok(e, new PassBattleResponse(e.State.Battle.Window.Responder, e.State.Battle.Window.DecisionId));
        Ok(e, new ActivateZoneEffectBattleResponse(PlayerId.One, e.State.Battle.Window.DecisionId, Hand(e, PlayerId.One, Scarecrow)));
        Drive(e);
        Assert.Equal(BattleStep.End, e.State.Battle!.Step);
        Assert.Equal(8000, P(e, PlayerId.One).LifePoints);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Scarecrow);
    }

    /// <summary>Player One has Solemn Judgment set; Player Two Set a Warrior and now Flip Summons it on turn 4.</summary>
    private static DuelEngine FlipSummonIntoSolemn()
    {
        var e = Engine(Deck(Solemn), Deck());
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Solemn), 0)); Next(e);
        Ok(e, new SetMonster(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e); Next(e);
        var events = Ok(e, new ChangeMonsterPosition(PlayerId.Two, 0, BattlePosition.FaceUpAttack)).Events;
        Assert.Contains(events, ev => ev is SummonResponseRequested);
        Assert.DoesNotContain(events, ev => ev is MonsterFlipSummoned);
        Assert.Equal(BattlePosition.FaceDownDefense, P(e, PlayerId.Two).MonsterZones[0]!.Position);
        return e;
    }

    [Fact]
    public void ANegatedFlipSummonSendsTheMonsterToTheGraveyardWithoutFlippingIt()
    {
        var e = FlipSummonIntoSolemn();
        Ok(e, new ActivateSummonNegation(PlayerId.One, e.State.PendingSummon!.DecisionId, 0));
        var events = Drive(e);
        Assert.Contains(events, ev => ev is SummonNegated);
        Assert.DoesNotContain(events, ev => ev is MonsterFlipSummoned);
        Assert.Null(e.State.PendingSummon);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == W);
        Assert.Equal(4000, P(e, PlayerId.One).LifePoints);
    }

    [Fact]
    public void AFlipSummonThatIsNotAnsweredHappensAfterBothPlayersPass()
    {
        var e = FlipSummonIntoSolemn();
        Reject(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle), DuelError.SummonWindowPending);
        Ok(e, new PassSummonResponse(PlayerId.One, e.State.PendingSummon!.DecisionId));
        var events = Ok(e, new PassSummonResponse(PlayerId.Two, e.State.PendingSummon!.DecisionId)).Events;
        Assert.Contains(events, ev => ev is MonsterFlipSummoned);
        Assert.Equal(BattlePosition.FaceUpAttack, P(e, PlayerId.Two).MonsterZones[0]!.Position);
        Reject(e, new ChangeMonsterPosition(PlayerId.Two, 0, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
    }

    [Theory]
    [InlineData(SpellOfPain)]
    [InlineData(Barrel)]
    public void AResponseToADamagingEffectMovesThatDamageToTheOpponentForThatEffectOnly(uint response)
    {
        var e = Engine(Deck(response), Deck(Burn, Burn));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, response), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Burn), 0));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0));
        Drive(e);
        Assert.Equal((8000, 7000), (P(e, PlayerId.One).LifePoints, P(e, PlayerId.Two).LifePoints));
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Burn), 0));
        Drive(e);
        Assert.Equal((7000, 7000), (P(e, PlayerId.One).LifePoints, P(e, PlayerId.Two).LifePoints));
    }

    [Fact]
    public void ADamageResponseCannotAnswerAnEffectThatWouldNotDamageItsPlayer()
    {
        var e = Engine(Deck(Barrel, Burn), Deck(Dump, W, W, W, W, W, Fish, Fish));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Barrel), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Dump), 0));
        Reject(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0), DuelError.ActivationConditionNotMet);
        Drive(e); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Burn), 1));
        Ok(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId));
        Reject(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0), DuelError.ActivationConditionNotMet);
    }

    [Fact]
    public void AFaceDownMonsterThatChangesControlDoesNotTrigger()
    {
        var e = Ready(Deck(ChangeOfHeart), Ameba, set: true);
        TakeWithChangeOfHeart(e);
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Ameba);
        Assert.Equal(8000, P(e, PlayerId.One).LifePoints);
    }
}
