using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Cards sent elsewhere instead of the Graveyard, changed damage and LP gain, and replacements offered when a negation destroys.</summary>
public sealed class RedirectAndDamageRuleTests
{
    private const uint W = 991, Big = 992, Leaf = 993, Echo = 994, Hole = 995, Both = 996, Heal = 997, Toss = 998, Negator = 999, Recover = 1000,
        Fissure = 6707, Kettle = 5188, Banisher = 4914, Protector = 6039, Ground = 9154, Wall = 6448, Rainbow = 7463, Nurse = 7352;

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        (uint Id, uint Type, int Attack)[] monsters = [(W, MonsterTypes.Warrior, 1500), (Big, MonsterTypes.Warrior, 2600), (Leaf, MonsterTypes.Plant, 1000), (Echo, MonsterTypes.Warrior, 1200)];
        uint[] spells = [Hole, Both, Heal, Toss, Recover];
        CardCatalog catalog = new(new(null, "synthetic-redirects-and-damage"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(500, 2000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Concat(monsters.Select(m => new CardDefinition(m.Id, $"Monster {m.Id}", "", new(m.Attack, 1000, 4), new(1, m.Type, 1, 1, 0, 0, 0))))
                .Concat(spells.Append(Negator).Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        foreach (var m in monsters) rules[m.Id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in spells) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Negator] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions,
                new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("both", Both, EffectKind.NormalSpell, [new DamageBothPlayersStep(500, 500)]),
                new("heal", Heal, EffectKind.NormalSpell, [new GainLifePointsStep(1000)]),
                new("toss", Toss, EffectKind.NormalSpell, [new DealDamageStep(100)], cost: new DiscardCost()),
                new("echo", Echo, EffectKind.MonsterIgnition, [new DealDamageStep(100)]),
                ResponseProbe.Negate("negator", Negator, ResponseNegation.Activation, ChainSpellSpeed.Three, destroy: true)],
            shuffleSeed: 8, ruleLocks: TemplateCardFamily.Family.RuleLocks, battleAbilities: TemplateCardFamily.Family.BattleAbilities,
            destructionReplacements: [.. TemplateCardFamily.Family.DestructionReplacements,
                new("echo-stays", Echo, ReplacementSubject.Self, new(ReplacementAction.DestroyOwnMonster))],
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id), zone));

    /// <summary>Resolves the chain, using every destruction replacement offered with its first card option.</summary>
    private static List<DuelEvent> ResolveAll(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.PendingChain is { } chain && e.State.Outcome is null)
        {
            DuelCommand command = chain.ReplacementChoice is { } choice
                ? new ChooseDestructionReplacement(choice.Player, choice.DecisionId, true, choice.Replacement.Current.CardOptions.FirstOrDefault()?.InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId);
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Turn three: Player One has <paramref name="card"/> face-up and a monster in zone 0; Player Two then destroys every monster.</summary>
    private static (DuelEngine Engine, List<DuelEvent> Events) HoleAgainst(uint card, bool continuousSpell)
    {
        var e = Engine(Deck(card, W), Deck(W, Hole));
        EnterMain(e);
        if (continuousSpell) { Activate(e, card, 3); ResolveAll(e); } else Summon(e, card, 1);
        if (continuousSpell) Summon(e, W, 0); else { Next(e); Next(e); Summon(e, W, 0); }
        Next(e);
        Summon(e, W, 0);
        Activate(e, Hole);
        return (e, ResolveAll(e));
    }

    [Fact]
    public void AFissureBanishesMonstersInsteadAndMarksTheirEvents()
    {
        var (e, events) = HoleAgainst(Fissure, continuousSpell: true);
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == W);
        Assert.Contains(P(e, PlayerId.Two).Banished, c => c.GameCardId == W);
        Assert.DoesNotContain(P(e, PlayerId.One).Graveyard, c => c.GameCardId == W);
        Assert.Contains(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == Hole);
        Assert.All(events.OfType<MonsterDestroyed>(), d => Assert.Equal(FieldCardDestination.Banished, d.FieldDestination));
    }

    [Fact]
    public void AKettleOnlyChangesItsControllersGraveyard()
    {
        var (e, _) = HoleAgainst(Kettle, continuousSpell: true);
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == W);
        Assert.Contains(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == W);
    }

    [Fact]
    public void ABanisherDestroyedAtTheSameTimeNoLongerApplies()
    {
        var (e, _) = HoleAgainst(Banisher, continuousSpell: false);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Banisher);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == W);
        Assert.Empty(P(e, PlayerId.One).Banished);
    }

    [Fact]
    public void AMonsterDestroyedByBattleCanBeShuffledIntoTheDeckInstead()
    {
        var e = Engine(Deck(Protector, W), Deck(Big));
        EnterMain(e); Summon(e, Protector, 1); Next(e); Next(e);
        Summon(e, W, 0); Next(e);
        int deck = P(e, PlayerId.One).Deck.Count;
        Summon(e, Big, 0);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        S.FinishAttack(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.DoesNotContain(P(e, PlayerId.One).Graveyard, c => c.GameCardId == W);
        Assert.Equal(deck + 1, P(e, PlayerId.One).Deck.Count);
    }

    [Fact]
    public void ADiscardCostNeverUsesACardThatWouldBeBanished()
    {
        var e = Engine(Deck(Fissure, Recover, W, Hole), Deck());
        EnterMain(e); Activate(e, Fissure, 3); ResolveAll(e);
        // The only card that could become the Graveyard target is a monster, which would be banished instead of discarded.
        Assert.False(e.Execute(new ActivateSpell(PlayerId.One, Hand(e, Recover), 4)).Succeeded);
    }
    [Fact]
    public void ACostThatSendsCardsToTheGraveyardCannotBePaidWhileTheyWouldBeBanished()
    {
        var e = Engine(Deck(Banisher, Toss, W), Deck());
        EnterMain(e); Summon(e, Banisher, 0);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Toss), 4), DuelError.LockedByEffect);
    }

    [Fact]
    public void ARedirectAppliedByAnEffectLastsOnlyThisTurn()
    {
        var e = Engine(Deck(Ground, W, Hole, Hole), Deck(W, Hole));
        EnterMain(e); SetCard(e, Ground, 0); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); ResolveAll(e);
        Summon(e, W, 0); Activate(e, Hole); ResolveAll(e);
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == W);
        Next(e);
        Summon(e, W, 0); Activate(e, Hole); ResolveAll(e);
        Assert.Contains(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == W);
    }

    [Fact]
    public void BattleDamageCanBeRedirectedForOneAttack()
    {
        var e = Engine(Deck(Wall), Deck(Big));
        EnterMain(e); SetCard(e, Wall, 0); Next(e);
        Summon(e, Big, 0);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0));
        var window = e.State.Battle!.Window;
        if (window.Responder != PlayerId.One) { Ok(e, new PassBattleResponse(window.Responder, window.DecisionId)); window = e.State.Battle!.Window; }
        Ok(e, new ActivateBattleResponse(PlayerId.One, window.DecisionId, 0));
        ResolveAll(e);
        S.FinishAttack(e);
        Assert.Equal(8000, P(e, PlayerId.One).LifePoints);
        Assert.Equal(5400, P(e, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void DamageCanBecomeAGainForTheTurn()
    {
        var e = Engine(Deck(Rainbow, Both, W), Deck());
        EnterMain(e); SetCard(e, Rainbow, 0); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        var discard = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(discard.Player, discard.DecisionId, discard.CardOptions.First(c => c.GameCardId == W).InstanceId));
        ResolveAll(e);
        Activate(e, Both); ResolveAll(e);
        Assert.Equal(8500, P(e, PlayerId.One).LifePoints);
        Assert.Equal(7500, P(e, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AnOpponentsGainCanBecomeDamage()
    {
        var e = Engine(Deck(Nurse), Deck(Heal));
        EnterMain(e); Summon(e, Nurse, 0); Next(e);
        Activate(e, Heal); ResolveAll(e);
        Assert.Equal(7000, P(e, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AReplacementIsOfferedWhenANegationWouldDestroyTheSource()
    {
        var e = Engine(Deck(Echo, Leaf), Deck(Negator));
        EnterMain(e); Summon(e, Echo, 0); Next(e);
        SetCard(e, Negator, 0); Next(e);
        Summon(e, Leaf, 1);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        ResolveAll(e);
        Assert.Equal(Echo, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Null(P(e, PlayerId.One).MonsterZones[1]);
    }
}
