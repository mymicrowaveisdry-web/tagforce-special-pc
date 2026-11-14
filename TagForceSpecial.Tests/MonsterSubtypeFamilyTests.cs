using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Flip, Spirit and Gemini mechanics through the production trigger definitions.</summary>
public sealed class MonsterSubtypeFamilyTests
{
    private const uint Skelengel = 4546, ManEater = 4507, OldMagician = 5651, HaneHane = 4547, Faith = 4434, Mask = 4108,
        DarkDust = 5414, Aratama = 10766, Piper = 7198, Attacker = 901, Weak = 902, Filler = 907, Burn = 908, Revive = 909, TrapHole = 4836, Solemn = 4861, Recall = 912, Seek = 913;

    private static CardCatalog Catalog() => new(new(null, "synthetic-monster-subtype"),
        MonsterSubtypeFamily.Cards.Select(c => new CardDefinition(c.CardId, $"Probe {c.CardId}", "", new(1500, 1000, c.CardId == DarkDust ? 6u : 4u)))
            .Concat([
                new(Attacker, "Attacker", "", new(2000, 1000, 4)),
                new(Weak, "Weak", "", new(100, 100, 4)),
                new(Filler, "Filler", "", new(1700, 1800, 4)),
                new(Burn, "Burn", "", new(0, 0, 1)),
                new(Revive, "Revive", "", new(0, 0, 1)),
                new(TrapHole, "Trap Hole", "", new(0, 0, 1)),
                new(Solemn, "Solemn", "", new(0, 0, 1)),
                new(Recall, "Recall", "", new(0, 0, 1)),
                new(Seek, "Seek", "", new(0, 0, 1))]));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second, IEnumerable<EffectDefinition>? extra = null)
    {
        var rules = MonsterSubtypeFamily.CardRules.ToDictionary();
        foreach (uint id in new[] { Attacker, Weak, Filler })
            rules[id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Burn] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Revive] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[TrapHole] = SummonTrapFamily.Rules[TrapHole];
        rules[Solemn] = SummonNegationFamily.Rules[Solemn];
        rules[Recall] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Seek] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(Catalog(), rules, first, second, experimentalTriggerEffects: MonsterSubtypeFamily.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [new("burn", Burn, EffectKind.NormalSpell, [new DealDamageStep(100)]),
                new("revive", Revive, EffectKind.NormalSpell, [new SpecialSummonStep(SpecialSummonSources.Hand, [Aratama])]), .. extra ?? []], shuffleSeed: 1);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
    private static uint[] Stacked(uint monster, params uint[] rest) => Deck([monster, Filler, Filler, Filler, Filler, .. rest]);
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static DuelActionResult Play(DuelEngine e, uint id, int zone = 0) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void SetDown(DuelEngine e, uint id, int zone = 0) => Ok(e, new SetMonster(Turn(e), Hand(e, id), zone));
    private static DuelActionResult Flip(DuelEngine e, int zone = 0) => Ok(e, new ChangeMonsterPosition(Turn(e), zone, BattlePosition.FaceUpAttack));
    private static FieldMonster? Monster(DuelEngine e, PlayerId p, int zone = 0) => e.State.GetPlayer(p).MonsterZones[zone];

    private static void Order(DuelEngine e)
    {
        var batch = e.State.EventEffects.PendingTriggers!;
        Ok(e, new ChooseTriggerOrder(batch.Player, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray()));
    }

    private static void ChooseMonster(DuelEngine e, PlayerId owner, int zone)
    {
        var commitment = e.State.EventEffects.PendingCommitment!;
        var target = commitment.TargetOptions.Single(t => t.Player == owner && t.Zone == zone);
        Ok(e, new ChooseTriggerMonsterTarget(commitment.Player, commitment.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
    }

    private static void ToTrigger(DuelEngine e)
    {
        while (e.State.EventEffects.PendingTriggers is null && e.State.Battle?.Attack is not null && e.State.Outcome is null)
        {
            S.Close(e); S.Advance(e);
        }
    }

    /// <summary>Player One sets <paramref name="monster"/>, then Player Two's second turn begins with its flip available to Player One.</summary>
    private static DuelEngine SetThenOwnTurn(uint monster, uint[]? second = null, Action<DuelEngine>? opponent = null, uint[]? first = null)
    {
        var e = Engine(first ?? Deck(monster), second ?? Deck());
        EnterMain(e); SetDown(e, monster); Next(e);
        opponent?.Invoke(e);
        Next(e);
        return e;
    }

    [Fact]
    public void AFlipSummonActivatesItsEffect()
    {
        var e = SetThenOwnTurn(Skelengel);
        Flip(e);
        Assert.True(e.State.EventEffects.PendingTriggers!.Mandatory);
        Assert.Contains(S.DrainEffects(e), ev => ev is CardDrawn);
    }

    [Fact]
    public void ABattleFlipActivatesEvenWhenTheMonsterIsDestroyedByThatBattle()
    {
        var e = Engine(Stacked(ManEater), Deck(Attacker));
        EnterMain(e); SetDown(e, ManEater); Next(e);
        Play(e, Attacker); Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        ToTrigger(e);
        Order(e);
        // The battle-destroyed flip monster cannot target itself, so only the attacker is left.
        Assert.Single(e.State.EventEffects.PendingCommitment!.TargetOptions);
        ChooseMonster(e, PlayerId.Two, 0);
        Resolve(e);
        Assert.Null(Monster(e, PlayerId.Two));
        S.FinishAttack(e);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == ManEater);
    }

    [Fact]
    public void AFlipTargetCanBeFaceDownAndCanBeTheFlipMonster()
    {
        var e = SetThenOwnTurn(ManEater, Deck(Attacker), d => SetDown(d, Attacker));
        Flip(e); Order(e);
        Assert.Equal(2, e.State.EventEffects.PendingCommitment!.TargetOptions.Count);
        ChooseMonster(e, PlayerId.Two, 0);
        Assert.Contains(Resolve(e), ev => ev is MonsterDestroyed { Player: PlayerId.Two });
    }

    [Fact]
    public void AnOpponentOnlyFlipNeedsAnOpponentMonster()
    {
        var none = SetThenOwnTurn(OldMagician);
        Assert.Contains(Flip(none).Events, ev => ev is TriggerUnavailable);
        Assert.Null(none.State.EventEffects.PendingTriggers);

        var e = SetThenOwnTurn(OldMagician, Deck(Attacker), d => Play(d, Attacker));
        Flip(e); Order(e);
        var target = Assert.Single(e.State.EventEffects.PendingCommitment!.TargetOptions);
        Assert.Equal(PlayerId.Two, target.Player);
    }

    [Fact]
    public void AFlipCanReturnAMonsterToTheHand()
    {
        var e = SetThenOwnTurn(HaneHane, Deck(Attacker), d => Play(d, Attacker));
        Flip(e); Order(e); ChooseMonster(e, PlayerId.Two, 0);
        Resolve(e);
        Assert.Null(Monster(e, PlayerId.Two));
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Hand, c => c.GameCardId == Attacker);
    }

    [Fact]
    public void AGraveyardTargetFlipOffersOnlyItsCategoryThroughTheAdapter()
    {
        var e = Engine(Deck(Faith, Burn), Deck());
        EnterMain(e); SetDown(e, Faith);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Burn), 0));
        Resolve(e);
        Next(e); Next(e);
        Flip(e); Order(e);
        var source = new DuelActionSource(e, Catalog());
        var view = source.Observe(PlayerId.One);
        Assert.Equal("TriggerCommitment", view.Decision!.Kind);
        var action = Assert.Single(view.Actions);
        Assert.Equal(ActionKind.ChooseCard, action.Kind);
        Assert.True(source.Execute(PlayerId.One, view.Revision, action.Token).Accepted);
        Resolve(e);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Burn);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Burn);
    }

    [Fact]
    public void AGraveyardTargetFlipNeedsAMatchingCard()
    {
        var e = Engine(Deck(Mask, Burn), Deck());
        EnterMain(e); SetDown(e, Mask);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Burn), 0));
        Resolve(e);
        Next(e); Next(e);
        Assert.Contains(Flip(e).Events, ev => ev is TriggerUnavailable);
    }

    [Fact]
    public void ASpiritReturnsToTheHandInTheEndPhaseOfItsSummon()
    {
        var e = Engine(Deck(Aratama), Deck());
        EnterMain(e); Play(e, Aratama);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.True(e.State.EventEffects.PendingTriggers!.Mandatory);
        S.DrainEffects(e);
        Assert.Null(Monster(e, PlayerId.One));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Aratama);
        Ok(e, new EndTurn(PlayerId.One));
    }

    [Fact]
    public void ASetSpiritStaysOnTheField()
    {
        var e = Engine(Deck(Aratama), Deck());
        EnterMain(e); SetDown(e, Aratama);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.NotNull(Monster(e, PlayerId.One));
    }

    [Fact]
    public void ASpiritFlippedByBattleReturnsAtTheEndOfThatTurn()
    {
        var e = Engine(Deck(Aratama), Deck(Weak));
        EnterMain(e); SetDown(e, Aratama); Next(e);
        Play(e, Weak); Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        S.FinishAttack(e);
        Assert.NotNull(Monster(e, PlayerId.One));
        while (e.State.Battle!.Step != BattleStep.End) { S.Close(e); S.Advance(e); }
        S.Close(e);
        Phase(e, DuelPhase.Main2);
        Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.End));
        S.DrainEffects(e);
        Assert.Null(Monster(e, PlayerId.One));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Aratama);
    }

    [Fact]
    public void ASpiritSearchSkipsItsOwnName()
    {
        var e = Engine(Stacked(Aratama, Aratama, DarkDust), Deck());
        EnterMain(e); Play(e, Aratama); Order(e);
        var chain = e.State.PendingChain!;
        Ok(e, new PassChainResponse(chain.Responder, chain.DecisionId));
        Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, e.State.PendingChain.DecisionId));
        Ok(e, new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId));
        Assert.Equal([DarkDust], e.State.PendingChain!.DefinitionCardChoice!.Options.Select(c => c.GameCardId));
    }

    [Fact]
    public void ASpiritEffectDestroysOtherFaceUpMonstersOnly()
    {
        var e = Engine(Deck(Filler, DarkDust), Deck(Attacker));
        EnterMain(e); Play(e, Filler); Next(e);
        Play(e, Attacker); Next(e);
        var tribute = Monster(e, PlayerId.One)!;
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, DarkDust), 0, [new(PlayerId.One, 0, tribute.Card, tribute.PlacementId)], false, null));
        Assert.Contains(S.DrainEffects(e), ev => ev is MonsterDestroyed { Player: PlayerId.Two });
        Assert.Equal(DarkDust, Monster(e, PlayerId.One)!.Card.GameCardId);
    }

    [Fact]
    public void ACardThatCannotBeSpecialSummonedIsNeverAnOption()
    {
        var e = Engine(Deck(Revive, Aratama), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Revive), 0), DuelError.DecisionUnavailable);
    }

    /// <summary>Player One summons Piper, Player Two a weak monster, and Player One's next turn begins.</summary>
    private static DuelEngine PiperOverWeak(Action<DuelEngine>? onThirdTurn = null)
    {
        var e = Engine(Deck(Piper), Deck(Weak));
        EnterMain(e); Play(e, Piper); Next(e);
        Play(e, Weak); Next(e);
        onThirdTurn?.Invoke(e);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.One, 0, 0));
        return e;
    }

    [Fact]
    public void AGeminiEffectNeedsTheSecondNormalSummon()
    {
        var plain = PiperOverWeak();
        Assert.DoesNotContain(S.FinishAttack(plain), ev => ev is CardDrawn);

        var gemini = PiperOverWeak(d => Ok(d, new GeminiSummon(PlayerId.One, 0)));
        Assert.Contains(S.FinishAttack(gemini), ev => ev is CardDrawn);
        Assert.True(Monster(gemini, PlayerId.One)!.GeminiActive);
    }

    [Fact]
    public void AGeminiSummonUsesTheNormalSummonAndFollowsItsRules()
    {
        var used = Engine(Deck(Piper), Deck());
        EnterMain(used); Play(used, Piper);
        Reject(used, new GeminiSummon(PlayerId.One, 0), DuelError.NormalSummonLimitReached);

        var e = Engine(Deck(Piper, Weak), Deck());
        EnterMain(e); Play(e, Piper); Next(e); Next(e);
        Reject(e, new GeminiSummon(PlayerId.One, 1), DuelError.MonsterZoneEmpty);
        Ok(e, new GeminiSummon(PlayerId.One, 0));
        Reject(e, new GeminiSummon(PlayerId.One, 0), DuelError.GeminiAlreadyActive);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, Weak), 1), DuelError.NormalSummonLimitReached);

        var other = Engine(Deck(Weak), Deck());
        EnterMain(other); Play(other, Weak); Next(other); Next(other);
        Reject(other, new GeminiSummon(PlayerId.One, 0), DuelError.NotGeminiMonster);

        var facedown = SetThenOwnTurn(Piper);
        Reject(facedown, new GeminiSummon(PlayerId.One, 0), DuelError.InvalidBattlePosition);
    }

    [Fact]
    public void AGeminiSummonOpensTheSummonResponseWindowAndAppearsInTheAdapter()
    {
        var e = Engine(Deck(Piper), Deck(TrapHole), SummonTrapFamily.Definitions.Where(d => d.CardId == TrapHole));
        EnterMain(e); Play(e, Piper); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, TrapHole), 0)); Next(e);
        var view = new DuelActionSource(e, Catalog()).Observe(PlayerId.One);
        Assert.Contains(view.Actions, a => a.Kind == ActionKind.Summon && a.Zone == 0 && a.CardInstanceId == Monster(e, PlayerId.One)!.Card.InstanceId);
        Assert.Contains(Ok(e, new GeminiSummon(PlayerId.One, 0)).Events, ev => ev is SummonResponseWindowRequested { Responder: PlayerId.Two });
    }

    private static DuelEngine GeminiFacingSolemn()
    {
        var e = Engine(Deck(Piper), Deck(Solemn), SummonNegationFamily.Definitions.Where(d => d.CardId == Solemn));
        EnterMain(e); Play(e, Piper); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Solemn), 0)); Next(e);
        Ok(e, new GeminiSummon(PlayerId.One, 0));
        return e;
    }

    [Fact]
    public void AGeminiSummonCanBeNegatedAndStaysSpent()
    {
        var e = GeminiFacingSolemn();
        var pending = e.State.PendingSummon!;
        Assert.True(pending.IsGeminiSummon);
        Assert.NotNull(Monster(e, PlayerId.One));
        Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0));
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is SummonNegated);
        Assert.DoesNotContain(events, ev => ev is MonsterDestroyed);
        Assert.Null(Monster(e, PlayerId.One));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Piper);
        Assert.True(e.State.NormalSummonUsed);
        Assert.Null(e.State.PendingSummon);
    }

    [Fact]
    public void AnUnansweredGeminiSummonGivesTheMonsterItsEffects()
    {
        var e = GeminiFacingSolemn();
        Assert.False(Monster(e, PlayerId.One)!.GeminiActive);
        Ok(e, new PassSummonResponse(PlayerId.Two, e.State.PendingSummon!.DecisionId));
        var events = Ok(e, new PassSummonResponse(PlayerId.One, e.State.PendingSummon!.DecisionId)).Events;
        Assert.Contains(events, ev => ev is MonsterNormalSummoned);
        Assert.True(Monster(e, PlayerId.One)!.GeminiActive);
    }

    [Fact]
    public void AGeminiInTheGraveyardCountsAsANormalMonsterButNotInTheDeck()
    {
        var recall = new EffectDefinition("recall", Recall, EffectKind.NormalSpell, [new AddTargetsToHandStep()],
            fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new NormalMonsterFilter([Weak]))]);
        var e = Engine(Deck(Piper, Recall), Deck(Attacker), [recall]);
        EnterMain(e); Play(e, Piper); Next(e);
        Play(e, Attacker); Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        S.FinishAttack(e);
        while (e.State.Battle!.Step != BattleStep.End) { S.Close(e); S.Advance(e); }
        S.Close(e);
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Recall), 0));
        Assert.Equal(Piper, Assert.Single(e.State.PendingDecision!.FieldTargetOptions!).Card.GameCardId);

        var seek = new EffectDefinition("seek", Seek, EffectKind.NormalSpell, [new AddFromDeckToHandStep(new NormalMonsterFilter([Weak]))]);
        var deck = Engine(Stacked(Seek, Piper, Weak), Deck(), [seek]);
        EnterMain(deck);
        Ok(deck, new ActivateSpell(PlayerId.One, Hand(deck, Seek), 0));
        var chain = deck.State.PendingChain!;
        Ok(deck, new PassChainResponse(chain.Responder, chain.DecisionId));
        Ok(deck, new PassChainResponse(deck.State.PendingChain!.Responder, deck.State.PendingChain.DecisionId));
        Ok(deck, new ResolveNextChainLink(PlayerId.One, deck.State.PendingChain!.DecisionId));
        Assert.Equal([Weak], deck.State.PendingChain!.DefinitionCardChoice!.Options.Select(c => c.GameCardId));
    }

    [Fact]
    public void TheNormalMonsterRecoverySpellUsesTheGraveyardRule() =>
        Assert.IsType<NormalMonsterFilter>(Assert.IsType<ZoneTarget>(GraveyardFamily.Cards.Single(c => c.CardId == 6129).Definition.FieldTargets[0]).Filter);

    [Fact]
    public void FlipDefinitionsSwitchTheDuelToTheStagedBattleFlow()
    {
        var rules = MonsterSubtypeFamily.CardRules.ToDictionary();
        rules[Attacker] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        rules[Filler] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        var flips = new DuelEngine(Catalog(), rules, Stacked(Skelengel), Deck(Attacker), experimentalTriggerEffects: MonsterSubtypeFamily.Triggers, shuffleSeed: 1);
        Assert.True(flips.UsesStagedBattle);
        EnterMain(flips); SetDown(flips, Skelengel); Next(flips);
        Play(flips, Attacker); Phase(flips, DuelPhase.Battle); S.Ready(flips);
        Ok(flips, new DeclareAttack(PlayerId.Two, 0, 0));
        ToTrigger(flips);
        Assert.Contains(S.DrainEffects(flips), ev => ev is CardDrawn);

        var plain = new DuelEngine(Catalog(), rules, Deck(), Deck(), experimentalTriggerEffects:
            [new TriggerEffectProfile("old-flip", Skelengel, MonsterEventKind.FlippedFaceUp, EffectRelation.Self, TriggerRequirement.Mandatory, TriggerOperation.GainLifePoints, lifePointGain: 100)]);
        Assert.False(plain.UsesStagedBattle);
    }

    [Fact]
    public void RegistrationRejectsInvalidSubtypeTriggers()
    {
        var draw = new EffectDefinition("draw", Skelengel, EffectKind.MonsterTrigger, [new DrawCardsStep(1)]);
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(draw, MonsterEventKind.EndPhaseAfterSummonOrFlip, EffectRelation.Self,
            TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(draw, MonsterEventKind.DestroyedOpponentByBattle, EffectRelation.Any, TriggerRequirement.Mandatory));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(draw, MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Mandatory,
            TriggerSourceLocation.GraveyardSelf, requiresGeminiActive: true));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("t", Skelengel, EffectKind.NormalTrap, [new ReturnTargetsToHandStep()], fieldTargets: [new TriggerSourceMonster()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("t", Skelengel, EffectKind.MonsterTrigger, [new AddTargetsToHandStep()],
            fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyCardFilter(), 1, 2)]));
        var catalog = new CardCatalog(new(null, "synthetic"), [new(1, "x", "", new(0, 0, 1))]);
        uint[] deck = [.. Enumerable.Repeat(1u, 40)];
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, new Dictionary<uint, DuelCardRules> { [1] = new(CardCategory.Spell, CannotBeSpecialSummoned: true) }, deck, deck));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, new Dictionary<uint, DuelCardRules> { [1] = new(CardCategory.Trap, IsGemini: true) }, deck, deck));
    }
}
