using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic Continuous Spells and Traps: timing, lifetime and the stat layer.</summary>
public sealed class LastingSpellTrapTests
{
    private const uint A = 901, B = 902, Aura = 910, Weaken = 911, Counter = 912, EffectCounter = 913, Storm = 914, Snipe = 915,
        Burn = 916, Filtered = 917;

    private static readonly uint[] Spells = [Aura, Storm, Burn, Filtered];
    private static readonly uint[] Traps = [Weaken, Counter, EffectCounter, Snipe];

    private static EffectDefinition[] Definitions() =>
    [
        new("aura", Aura, EffectKind.ContinuousSpell, [], lastingModifiers: [new LastingStatModifier(TargetSide.Own, 500, 100)]),
        new("weaken", Weaken, EffectKind.ContinuousTrap, [new GainLifePointsStep(300)],
            lastingModifiers: [new LastingStatModifier(TargetSide.Opponent, -200, 0)]),
        new("filtered", Filtered, EffectKind.ContinuousSpell, [],
            lastingModifiers: [new LastingStatModifier(TargetSide.Own, 200, 0, new MonsterTraitFilter(MonsterStat.Attack, 1000))]),
        ResponseProbe.Negate("counter", Counter, ResponseNegation.Activation, ChainSpellSpeed.Three),
        ResponseProbe.Negate("effect-counter", EffectCounter, ResponseNegation.Effect, ChainSpellSpeed.Three),
        new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.SpellTrap)]),
        new("burn", Burn, EffectKind.NormalSpell, [new DealDamageStep(100)]),
        new("snipe", Snipe, EffectKind.NormalTrap, [new DestroyTargetsStep()], [new RespondsToActivationCondition()],
            fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap)])
    ];

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var catalog = new CardCatalog(new(null, "synthetic-lasting"), Spells.Concat(Traps).Concat([A, B]).Select(id =>
            new CardDefinition(id, $"Probe {id}", "", id switch { A => new(1000, 1000, 4), B => new(1500, 2000, 4), _ => new(0, 0, 1) })));
        var rules = new Dictionary<uint, DuelCardRules>
        {
            [A] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true),
            [B] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true)
        };
        foreach (uint id in Spells) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        foreach (uint id in Traps) rules[id] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, effectDefinitions: Definitions());
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(A, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static CardInstance Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id);
    private static void Summon(DuelEngine e, uint id, int zone, bool set = false) =>
        Ok(e, set ? new SetMonster(Turn(e), Hand(e, id).InstanceId, zone) : new NormalSummon(Turn(e), Hand(e, id).InstanceId, zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id).InstanceId, zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id).InstanceId, zone));
    private static void Respond(DuelEngine e, PlayerId player, int zone) =>
        Ok(e, new ActivateChainResponse(player, e.State.PendingChain!.DecisionId, zone));
    private static int? Atk(DuelEngine e, PlayerId player, int zone) => e.GetMonsterStatistics(player, zone)!.Attack;
    private static int? Def(DuelEngine e, PlayerId player, int zone) => e.GetMonsterStatistics(player, zone)!.Defense;
    private static FieldSpellTrap? Zone(DuelEngine e, PlayerId player, int zone) => e.State.GetPlayer(player).SpellTrapFields[zone];

    /// <summary>Player One has A in zone 0 and activates Aura into zone 4, with Two holding a set card in zone 0.</summary>
    private static DuelEngine AuraOnChainAgainstSetCard(uint setCard)
    {
        var e = Engine(Deck(Aura), Deck(setCard));
        EnterMain(e); Next(e);
        SetCard(e, setCard, 0); Next(e);
        Summon(e, A, 0);
        Activate(e, Aura);
        return e;
    }

    [Fact]
    public void ContinuousSpellStaysFaceUpAndAppliesOnlyOnceResolved()
    {
        var e = Engine(Deck(Aura), Deck());
        EnterMain(e); Summon(e, A, 0);
        Activate(e, Aura);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));

        var events = Resolve(e);

        Assert.Equal(1500, Atk(e, PlayerId.One, 0));
        Assert.Equal(1100, Def(e, PlayerId.One, 0));
        Assert.Single(events.OfType<StatModifierApplied>());
        Assert.DoesNotContain(events, ev => ev is SpellSentToGraveyard);
        Assert.Empty(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.True(Zone(e, PlayerId.One, 4) is { IsFaceUp: true, EffectActive: true });
    }

    [Fact]
    public void LastingModifierFollowsSideAndCoversLaterMonsters()
    {
        var e = Engine(Deck(Aura), Deck());
        EnterMain(e); Activate(e, Aura); Resolve(e);
        Summon(e, A, 0);
        Assert.Equal(1500, Atk(e, PlayerId.One, 0));
        Next(e);
        Summon(e, A, 0);
        Assert.Equal(1000, Atk(e, PlayerId.Two, 0));
        Assert.Equal(1500, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void LastingModifierSkipsFaceDownMonsters()
    {
        var e = Engine(Deck(Aura), Deck());
        EnterMain(e); Summon(e, A, 0, set: true);
        Activate(e, Aura); Resolve(e);
        Assert.Empty(e.State.EventEffects.AppliedModifiers);
    }

    [Fact]
    public void LastingModifierHonoursItsFilter()
    {
        var e = Engine(Deck(Filtered, B), Deck());
        EnterMain(e); Summon(e, A, 0);
        Activate(e, Filtered); Resolve(e);
        Next(e); Next(e);
        Summon(e, B, 1);
        Assert.Equal(1200, Atk(e, PlayerId.One, 0));
        Assert.Equal(1500, Atk(e, PlayerId.One, 1));
    }

    [Fact]
    public void LastingModifierChangesBattleDamage()
    {
        var e = Engine(Deck(Aura), Deck());
        EnterMain(e); Summon(e, A, 0);
        Activate(e, Aura); Resolve(e);
        Next(e); Next(e);
        Phase(e, DuelPhase.Battle);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
        Assert.Equal(6500, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void DestroyingTheCardEndsItsEffect()
    {
        var e = Engine(Deck(Aura), Deck(Storm));
        EnterMain(e); Summon(e, A, 0);
        Activate(e, Aura); Resolve(e);
        Next(e);
        Activate(e, Storm);
        var events = Resolve(e);

        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
        Assert.Equal(ModifierRemovalReason.ConditionNoLongerMet, Assert.Single(events.OfType<StatModifierRemoved>()).Reason);
        Assert.Null(Zone(e, PlayerId.One, 4));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Aura);
    }

    [Fact]
    public void ContinuousTrapWaitsATurnThenResolvesAndStays()
    {
        var e = Engine(Deck(Weaken, Weaken), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Weaken).InstanceId, 1), DuelError.ActivationTimingNotAllowed);
        SetCard(e, Weaken, 0);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.SetThisTurn);
        Next(e);
        Summon(e, A, 0); Next(e);

        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);

        Assert.Equal(8300, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(800, Atk(e, PlayerId.Two, 0));
        Assert.True(Zone(e, PlayerId.One, 0) is { IsFaceUp: true, EffectActive: true });
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.UnsupportedSpellEffect);
    }

    [Fact]
    public void SetContinuousSpellActivatesOnOwnTurnButCannotRespond()
    {
        var e = Engine(Deck(Aura), Deck(Burn));
        EnterMain(e); SetCard(e, Aura, 0); Next(e);
        Activate(e, Burn);
        Reject(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0), DuelError.ActivationTimingNotAllowed);
        Resolve(e); Next(e);

        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);
        Assert.True(Zone(e, PlayerId.One, 0) is { IsFaceUp: true, EffectActive: true });
    }

    [Fact]
    public void NegatedActivationGoesToTheGraveyardWithoutEffect()
    {
        var e = AuraOnChainAgainstSetCard(Counter);
        Respond(e, PlayerId.Two, 0);
        var events = Resolve(e);

        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
        Assert.Null(Zone(e, PlayerId.One, 4));
        Assert.Contains(events, ev => ev is SpellSentToGraveyard s && s.Card.GameCardId == Aura);
    }

    [Fact]
    public void NegatedEffectLeavesTheCardFaceUpAndInactive()
    {
        var e = AuraOnChainAgainstSetCard(EffectCounter);
        Respond(e, PlayerId.Two, 0);
        Resolve(e);

        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
        Assert.True(Zone(e, PlayerId.One, 4) is { IsFaceUp: true, EffectActive: false });
    }

    [Fact]
    public void SourceDestroyedInResponseSkipsTheWholeEffect()
    {
        var e = Engine(Deck(Weaken), Deck(Snipe));
        EnterMain(e); SetCard(e, Weaken, 0); Next(e);
        SetCard(e, Snipe, 0); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Respond(e, PlayerId.Two, 0);
        var pending = e.State.PendingDecision!;
        var target = pending.FieldTargetOptions!.Single(o => o.Player == PlayerId.One && o.Zone == 0);
        Ok(e, new ChooseDecisionFieldTarget(pending.Player, pending.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId));

        var events = Resolve(e);

        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.SourceLeftField });
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Weaken);
    }

    [Fact]
    public void DefinitionsValidateLastingRules()
    {
        var modifier = new LastingStatModifier(TargetSide.Own, 100, 0);
        Assert.Throws<ArgumentException>(() => new LastingStatModifier(TargetSide.Own, 0, 0));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Aura, EffectKind.ContinuousSpell, []));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Burn, EffectKind.NormalSpell, [new DealDamageStep(1)], lastingModifiers: [modifier]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Aura, EffectKind.ContinuousSpell, [new DealDamageStep(1)],
            [new RespondsToActivationCondition()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Aura, EffectKind.ContinuousSpell, [], lastingModifiers: [modifier, modifier]));
    }
}
