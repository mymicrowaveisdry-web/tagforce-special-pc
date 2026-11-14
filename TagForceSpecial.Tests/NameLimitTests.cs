using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Limits shared by every copy a player controls, once-per-Duel limits, and plain once-per-turn copies.</summary>
public sealed class NameLimitTests
{
    private const uint W = 931, M = 932, T = 933, Burn = 934, Ember = 935, Relic = 936, Quick = 937, Neg = 938, Bounce = 939, Double = 940;

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        uint[] monsters = [W, M, T], spells = [Burn, Ember, Relic, Quick, Bounce, Double];
        CardCatalog catalog = new(new(null, "synthetic-name-limits"),
            monsters.Select(id => new CardDefinition(id, $"Monster {id}", "", new(1500, 1000, 4), new(1, 15, 1, 1, 0, 0, 0)))
                .Concat(spells.Append(Neg).Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));
        var rules = new Dictionary<uint, DuelCardRules>();
        foreach (uint id in monsters) rules[id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in spells) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Neg] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second,
            experimentalTriggerEffects: [new TriggerEffectProfile("t-gain", T, MonsterEventKind.NormalSummoned, EffectRelation.Self, TriggerRequirement.Mandatory,
                TriggerOperation.GainLifePoints, lifePointGain: 100, turnLimit: TriggerTurnLimit.SharedPerDuel)],
            effectDefinitions: [
                new EffectDefinition("burn", Burn, EffectKind.NormalSpell, [new DealDamageStep(100)]).WithNameLimit(new("burn-name", LimitCounts.Activations)),
                new EffectDefinition("ember", Ember, EffectKind.NormalSpell, [new DealDamageStep(100)]).WithNameLimit(new("ember-name", LimitCounts.Uses)),
                new EffectDefinition("relic", Relic, EffectKind.NormalSpell, [new DealDamageStep(100)]).WithNameLimit(new("relic-name", perDuel: true)),
                new EffectDefinition("quick", Quick, EffectKind.QuickPlaySpell, [new DealDamageStep(100)]).WithNameLimit(new("quick-name", LimitCounts.Activations)),
                ResponseProbe.Negate("neg", Neg, ResponseNegation.Activation, ChainSpellSpeed.Three),
                new("m-burn", M, EffectKind.MonsterIgnition, [new DealDamageStep(100)], oncePerTurn: true),
                new("bounce", Bounce, EffectKind.NormalSpell, [new ReturnTargetsToHandStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster)]),
                new("double", Double, EffectKind.NormalSpell, [new GrantNormalSummonStep()])],
            shuffleSeed: 4);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static ActivateSpell Spell(DuelEngine e, uint id, int zone = 4) => new(Turn(e), Hand(e, id), zone);
    private static int LifePoints(DuelEngine e, PlayerId p) => e.State.GetPlayer(p).LifePoints;

    /// <summary>Player Two sets the Counter Trap on their turn; it is Player One's turn three afterwards.</summary>
    private static DuelEngine WithNegator(uint card)
    {
        var e = Engine(Deck(card, card), Deck(Neg));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Neg), 0)); Next(e);
        Ok(e, Spell(e, card));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Resolve(e);
        Assert.Equal(8000, LifePoints(e, PlayerId.Two));
        return e;
    }

    [Fact]
    public void AnActivationLimitStopsASecondCopyUntilNextTurn()
    {
        var e = Engine(Deck(Burn, Burn, Burn), Deck());
        EnterMain(e);
        Ok(e, Spell(e, Burn)); Resolve(e);
        Reject(e, Spell(e, Burn, 3), DuelError.EffectAlreadyUsed);
        Next(e); Next(e);
        Ok(e, Spell(e, Burn));
    }

    [Fact]
    public void ANegatedActivationDoesNotSpendAnActivationLimit()
    {
        var e = WithNegator(Burn);
        Ok(e, Spell(e, Burn, 3));
    }

    [Fact]
    public void ANegatedActivationStillSpendsAUseLimit()
    {
        var e = WithNegator(Ember);
        Reject(e, Spell(e, Ember, 3), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void AOncePerDuelLimitNeverResets()
    {
        var e = Engine(Deck(Relic, Relic), Deck());
        EnterMain(e);
        Ok(e, Spell(e, Relic)); Resolve(e);
        Next(e); Next(e);
        Reject(e, Spell(e, Relic), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void EachPlayerHasTheirOwnCount()
    {
        var e = Engine(Deck(Quick, Quick), Deck(Quick));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Quick), 0)); Next(e);
        Ok(e, Spell(e, Quick));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Resolve(e);
        Reject(e, Spell(e, Quick, 3), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void APlainOncePerTurnLimitResetsWhenTheMonsterLeavesAndReturns()
    {
        var e = Engine(Deck(M, Bounce, Double), Deck());
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, M), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Resolve(e);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
        Ok(e, Spell(e, Double)); Resolve(e);
        Ok(e, Spell(e, Bounce));
        var pending = e.State.PendingDecision!;
        var own = pending.FieldTargetOptions!.Single(o => o.Card.GameCardId == M);
        Ok(e, new ChooseDecisionFieldTarget(pending.Player, pending.DecisionId, own.Player, own.Location, own.Zone, own.Card.InstanceId));
        Resolve(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, M), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
    }

    [Fact]
    public void AOncePerDuelTriggerActivatesOnlyOnce()
    {
        var e = Engine(Deck(T, T), Deck());
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, T), 0));
        EventEffectScenario.Drain(e);
        Assert.Equal(8100, LifePoints(e, PlayerId.One));
        Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, T), 1));
        EventEffectScenario.Drain(e);
        Assert.Equal(8100, LifePoints(e, PlayerId.One));
    }
}
