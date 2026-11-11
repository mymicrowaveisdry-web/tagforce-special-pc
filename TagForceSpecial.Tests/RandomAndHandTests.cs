using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Die rolls, coin tosses, and looking at or taking from the opponent's hand.</summary>
public sealed class RandomAndHandTests
{
    private const uint W = 1011, Coins = 1012, GracefulDice = 4815, SkullDice = 4816, SnipeHunter = 6879, MindHaxorz = 6221, TrapDustshoot = 5446,
        Gallis = 7981, EyeOfTruth = 4987, MindOnAir = 6119, PotOfDuality = 8993, PathsOfDestiny = 6809, DiceJar = 5424, Ordeal = 5439, TwinBarrel = 7675;

    private static DuelEngine Engine(uint[] first, uint[] second, ulong seed = 3)
    {
        CardCatalog catalog = new(new(null, "synthetic-random-and-hand"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Coins, "Coins", "", new(0, 0, 1))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Coins] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions,
                new("coins", Coins, EffectKind.NormalSpell, [new TossCoinsStep(3), new StopUnlessHeadsStep(1), new GainLifePointsStep(100, PerCount: EffectCount.Heads)])],
            shuffleSeed: seed, ruleLocks: TemplateCardFamily.Family.RuleLocks, battleAbilities: TemplateCardFamily.Family.BattleAbilities,
            destructionReplacements: TemplateCardFamily.Family.DestructionReplacements, graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects,
            damageRules: TemplateCardFamily.Family.DamageRules);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static int Attack(DuelEngine e, PlayerId owner, int zone) =>
        e.State.EventEffects.Statistics.Single(s => s.Monster.Player == owner && s.Monster.Zone == zone).Attack!.Value;

    /// <summary>Resolves the chain, answering any card choice with its first option.</summary>
    private static List<DuelEvent> ResolveAll(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.PendingChain is { } chain && e.State.Outcome is null)
        {
            DuelCommand command = chain.DefinitionCardChoice is { } pick ? new ChooseDefinitionCard(pick.Player, pick.DecisionId, pick.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId);
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Answers triggers, targets, calls and choices with their first option and resolves every chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e, CardCategory call = CardCategory.Spell)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingCommitment is { } c
                ? new ChooseTriggerZoneTarget(c.Player, c.DecisionId, c.ZoneOptions[0].Player, c.ZoneOptions[0].Card.InstanceId)
                : e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingChain is not { } chain ? null
                : chain.TypeCall is { } typeCall ? new CallCardType(typeCall.Player, typeCall.DecisionId, call)
                : chain.DefinitionSummon is { } summon ? new ChooseDefinitionSummon(summon.Player, summon.DecisionId, summon.Options[0].Card.InstanceId,
                    summon.MonsterZoneOptions[0], summon.PositionOptions[0])
                : chain.DefinitionCardChoice is { } pick ? new ChooseDefinitionCard(pick.Player, pick.DecisionId, pick.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    [Fact]
    public void ARevealedHandMonsterChecksTheCardItSendsFromTheDeck()
    {
        var hit = Engine(Deck(Gallis, W, W, W, W, W), Deck());
        EnterMain(hit);
        var paid = Ok(hit, new ActivateZoneEffect(PlayerId.One, Hand(hit, Gallis), 0)).Events;
        Assert.Contains(paid, ev => ev is CardsRevealed r && r.Cards.Single().GameCardId == Gallis);
        Drive(hit);
        Assert.Equal(8000 - 800, P(hit, PlayerId.Two).LifePoints);
        Assert.Contains(P(hit, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Gallis);

        var miss = Engine(Deck(Gallis, W, W, W, W, Coins), Deck());
        EnterMain(miss);
        Ok(miss, new ActivateZoneEffect(PlayerId.One, Hand(miss, Gallis), 0));
        Assert.Single(Drive(miss).OfType<CardDestroyedInHand>());
        Assert.Contains(P(miss, PlayerId.One).Graveyard, c => c.GameCardId == Gallis);
        Assert.Equal(8000, P(miss, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ExcavatingAddsOneCardShufflesTheRestAndLocksSpecialSummons()
    {
        var e = Engine(Deck(PotOfDuality, PotOfDuality, W, W, W, Coins, W, W), Deck());
        EnterMain(e);
        int hand = P(e, PlayerId.One).Hand.Count, deck = P(e, PlayerId.One).Deck.Count;
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PotOfDuality), 0));
        var events = Drive(e);
        Assert.Contains(events, ev => ev is RuleLockApplied);
        Assert.Equal(3, events.OfType<CardsRevealed>().Single().Cards.Count);
        Assert.Single(events.OfType<CardSearched>());
        Assert.Single(events.OfType<DeckShuffled>());
        Assert.Equal(hand - 1 + 1, P(e, PlayerId.One).Hand.Count);
        Assert.Equal(deck - 1, P(e, PlayerId.One).Deck.Count);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PotOfDuality), 1), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void EachPlayerTossesForTheirOwnGainOrDamage()
    {
        var seen = new HashSet<(int, int)>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var e = Engine(Deck(PathsOfDestiny), Deck(), seed);
            EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PathsOfDestiny), 0)); Next(e); Next(e);
            Ok(e, new ActivateSetSpell(PlayerId.One, 0));
            var tosses = Drive(e).OfType<CoinsTossed>().ToArray();
            Assert.Equal(2, tosses.Length);
            foreach (var toss in tosses)
                Assert.Equal(toss.Heads[0] ? 10000 : 6000, P(e, toss.Player).LifePoints);
            seen.Add((P(e, PlayerId.One).LifePoints, P(e, PlayerId.Two).LifePoints));
        }
        Assert.True(seen.Count >= 3, "Different results should occur across the seeds tried.");
    }

    [Fact]
    public void BothPlayersRollAndTheLowerTakesTheDamage()
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var e = Engine(Deck(DiceJar), Deck(), seed);
            EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, DiceJar), 0)); Next(e); Next(e);
            Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
            var rolls = Drive(e).OfType<DieRolled>().ToArray();
            var (own, theirs) = (rolls[^2].Result, rolls[^1].Result);
            Assert.NotEqual(own, theirs);
            int high = Math.Max(own, theirs);
            var loser = own < theirs ? PlayerId.One : PlayerId.Two;
            Assert.Equal(8000 - (high == 6 ? 6000 : high * 500), P(e, loser).LifePoints);
            Assert.Equal(8000, P(e, loser == PlayerId.One ? PlayerId.Two : PlayerId.One).LifePoints);
        }
    }

    [Fact]
    public void TheAttackerReturnsWhenTheOpponentCallsARandomHandCardWrong()
    {
        bool returned = false, stayed = false;
        uint[] mixed = [Ordeal, .. Enumerable.Range(0, 39).Select(i => i % 2 == 0 ? Coins : W)];
        for (ulong seed = 1; seed <= 40 && !(returned && stayed); seed++)
        {
            var e = Engine(mixed, Deck(), seed);
            EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Ordeal), 0)); Next(e);
            Ok(e, new NormalSummon(PlayerId.Two, Hand(e, W), 0)); Next(e);
            Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e); Next(e);
            Phase(e, DuelPhase.Battle); BattleFlowScenario.Ready(e);
            Ok(e, new DeclareAttack(PlayerId.Two, 0));
            var called = Drive(e, CardCategory.Spell).OfType<CardTypeCalled>().Single();
            Assert.Equal(PlayerId.Two, called.Caller);
            bool gone = P(e, PlayerId.Two).MonsterZones[0] is null;
            Assert.Equal(!called.Right, gone);
            returned |= gone; stayed |= !gone;
        }
        Assert.True(returned && stayed, "Both outcomes should occur across the seeds tried.");
    }

    [Fact]
    public void ASummonTriggerTargetsACardAndNeedsTwoHeads()
    {
        bool destroyed = false, spared = false;
        for (ulong seed = 1; seed <= 40 && !(destroyed && spared); seed++)
        {
            var e = Engine(Deck(TwinBarrel), Deck(W), seed);
            EnterMain(e); Next(e);
            Ok(e, new NormalSummon(PlayerId.Two, Hand(e, W), 0)); Next(e);
            Ok(e, new NormalSummon(PlayerId.One, Hand(e, TwinBarrel), 0));
            if (e.State.EventEffects.PendingCommitment is null)
                Ok(e, new ChooseTriggerOrder(PlayerId.One, e.State.EventEffects.PendingTriggers!.DecisionId,
                    e.State.EventEffects.PendingTriggers.Options.Select(o => o.Id).ToArray()));
            Assert.Equal(PlayerId.Two, e.State.EventEffects.PendingCommitment!.ZoneOptions.Single().Player);
            int heads = Drive(e).OfType<CoinsTossed>().Single().Heads.Count(h => h);
            bool gone = P(e, PlayerId.Two).MonsterZones[0] is null;
            Assert.Equal(heads == 2, gone);
            destroyed |= gone; spared |= !gone;
        }
        Assert.True(destroyed && spared, "Both outcomes should occur across the seeds tried.");
    }

    [Fact]
    public void ARevealEffectShowsTheOpponentsHandAndTheEyeGainsOnTheirStandby()
    {
        var mind = Engine(Deck(MindOnAir), Deck());
        EnterMain(mind);
        Assert.False(mind.HandRevealed(PlayerId.Two));
        Ok(mind, new NormalSummon(PlayerId.One, Hand(mind, MindOnAir), 0));
        Assert.True(mind.HandRevealed(PlayerId.Two));
        Assert.False(mind.HandRevealed(PlayerId.One));

        var eye = Engine(Deck(EyeOfTruth), [.. Enumerable.Repeat(Coins, 40)]);
        EnterMain(eye); Ok(eye, new SetSpellTrap(PlayerId.One, Hand(eye, EyeOfTruth), 0)); Next(eye); Next(eye);
        Ok(eye, new ActivateSetSpell(PlayerId.One, 0)); Drive(eye);
        Assert.True(eye.HandRevealed(PlayerId.Two));
        Phase(eye, DuelPhase.End); Ok(eye, new EndTurn(PlayerId.One));
        Ok(eye, new ResolveDrawStep(PlayerId.Two));
        Drive(eye);
        Assert.Equal(9000, P(eye, PlayerId.Two).LifePoints);
        Assert.Equal(8000, P(eye, PlayerId.One).LifePoints);
    }

    [Fact]
    public void ADieRollScalesAStatChangeForEveryMonsterOnOneSide()
    {
        var e = Engine(Deck(GracefulDice, W), Deck(W));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, W), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, W), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, GracefulDice), 4));
        int roll = ResolveAll(e).OfType<DieRolled>().Single().Result;
        Assert.InRange(roll, 1, 6);
        Assert.Equal(1500 + roll * 100, Attack(e, PlayerId.One, 0));
        Assert.Equal(1500, Attack(e, PlayerId.Two, 0));
    }

    [Fact]
    public void ARollDecidesWhetherTheRestOfTheEffectHappens()
    {
        bool destroyed = false, spared = false;
        for (ulong seed = 1; seed <= 40 && !(destroyed && spared); seed++)
        {
            var e = Engine(Deck(SnipeHunter, W, W), Deck(W), seed);
            EnterMain(e); Next(e);
            Ok(e, new NormalSummon(PlayerId.Two, Hand(e, W), 0)); Next(e);
            Ok(e, new NormalSummon(PlayerId.One, Hand(e, SnipeHunter), 0));
            Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
            var cost = e.State.PendingDecision!;
            Ok(e, new ChooseDecisionCard(cost.Player, cost.DecisionId, cost.CardOptions[0].InstanceId));
            var target = e.State.PendingDecision!;
            var opposing = target.FieldTargetOptions!.First(t => t.Player == PlayerId.Two);
            Ok(e, new ChooseDecisionFieldTarget(target.Player, target.DecisionId, opposing.Player, opposing.Location, opposing.Zone, opposing.Card.InstanceId));
            int roll = ResolveAll(e).OfType<DieRolled>().Single().Result;
            bool gone = P(e, PlayerId.Two).MonsterZones[0] is null;
            Assert.Equal(roll is >= 2 and <= 5, gone);
            destroyed |= gone; spared |= !gone;
        }
        Assert.True(destroyed && spared, "Both outcomes should occur across the seeds tried.");
    }

    [Fact]
    public void CoinTossHeadsCanScaleAnAmountOrStopTheEffect()
    {
        bool stopped = false, gained = false;
        for (ulong seed = 1; seed <= 40 && !(stopped && gained); seed++)
        {
            var e = Engine(Deck(Coins), Deck(), seed);
            EnterMain(e);
            Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Coins), 4));
            int heads = ResolveAll(e).OfType<CoinsTossed>().Single().Heads.Count(h => h);
            Assert.Equal(8000 + heads * 100, P(e, PlayerId.One).LifePoints);
            stopped |= heads == 0; gained |= heads > 0;
        }
        Assert.True(stopped && gained, "Both outcomes should occur across the seeds tried.");
    }

    [Fact]
    public void LookingAtTheOpponentsHandShowsOnlyTheViewer()
    {
        var e = Engine(Deck(MindHaxorz), Deck(W));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, MindHaxorz), 0)); Next(e);
        Ok(e, new SetMonster(PlayerId.Two, Hand(e, W), 0)); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        var look = ResolveAll(e).OfType<CardsLookedAt>().Single();
        Assert.Equal(PlayerId.One, look.Viewer);
        Assert.Equal(P(e, PlayerId.Two).Hand.Count + 1, look.Cards.Count);
        Assert.Equal(7500, P(e, PlayerId.One).LifePoints);
    }

    [Fact]
    public void ACardTakenFromTheOpponentsHandGoesBackIntoTheirDeck()
    {
        var e = Engine(Deck(TrapDustshoot), Deck(W));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, TrapDustshoot), 0)); Next(e); Next(e);
        int hand = P(e, PlayerId.Two).Hand.Count, deck = P(e, PlayerId.Two).Deck.Count;
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        ResolveAll(e);
        Assert.Equal(hand - 1, P(e, PlayerId.Two).Hand.Count);
        Assert.Equal(deck + 1, P(e, PlayerId.Two).Deck.Count);
    }

    [Fact]
    public void AHandSizeConditionMustHoldToActivate()
    {
        var e = Engine(Deck(TrapDustshoot), Deck(Coins, Coins, Coins, Coins, Coins));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, TrapDustshoot), 0)); Next(e);
        while (P(e, PlayerId.Two).Hand.Count > 3)
            Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Coins), P(e, PlayerId.Two).SpellTrapFields.ToList().FindIndex(f => f is null)));
        Next(e);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.ActivationConditionNotMet);
    }

    [Fact]
    public void AResultIsReadOnlyAfterTheEffectRollsOrTosses()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new StopUnlessRollStep(1), new RollDieStep()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new GainLifePointsStep(100, PerCount: EffectCount.Heads)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new TossCoinsStep(0)]));
    }
}
