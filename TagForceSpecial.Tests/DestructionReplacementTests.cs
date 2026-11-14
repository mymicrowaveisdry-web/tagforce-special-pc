using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>"If ... would be destroyed, (you can) ... instead", by card effect and by battle.</summary>
public sealed class DestructionReplacementTests
{
    private const uint W = 981, Rock = 982, Psy = 983, Leaf = 984, Big = 985, Snipe = 986, Hole = 987, PsyHole = 988, Aegis = 989,
        Catapult = 10194, Shocker = 7720, Nettles = 7667;

    private static DuelEngine Engine(uint[] first, uint[] second, DestructionReplacementProfile[]? extra = null)
    {
        (uint Id, uint Type, int Attack)[] monsters = [(W, MonsterTypes.Warrior, 1500), (Rock, MonsterTypes.Rock, 1000), (Psy, MonsterTypes.Psychic, 1500),
            (Leaf, MonsterTypes.Plant, 1000), (Big, MonsterTypes.Warrior, 2600)];
        uint[] spells = [Snipe, Hole, PsyHole, Aegis];
        CardCatalog catalog = new(new(null, "synthetic-destruction-replacement"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(500, 2000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Concat(monsters.Select(m => new CardDefinition(m.Id, $"Monster {m.Id}", "", new(m.Attack, 1000, 4), new(1, m.Type, 1, 1, 0, 0, 0))))
                .Concat(spells.Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        foreach (var m in monsters) rules[m.Id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in spells) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions,
                new("snipe", Snipe, EffectKind.NormalSpell, [new DestroyTargetsStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster)]),
                new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("psy-hole", PsyHole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster, Cards: new MonsterTypeFilter([MonsterTypes.Psychic]))]),
                new("aegis", Aegis, EffectKind.ContinuousSpell, [], isMarker: true)],
            shuffleSeed: 5, ruleLocks: TemplateCardFamily.Family.RuleLocks, battleAbilities: TemplateCardFamily.Family.BattleAbilities,
            destructionReplacements: [.. TemplateCardFamily.Family.DestructionReplacements, .. extra ?? []]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), zone));

    private static void Choose(DuelEngine e, uint id)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Card.GameCardId == id);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, t.Player, t.Location, t.Zone, t.Card.InstanceId));
    }

    /// <summary>Resolves the chain, answering each replacement offer with <paramref name="answer"/> (the offer and its card options).</summary>
    private static List<PendingReplacement> ResolveAll(DuelEngine e, Func<ReplacementOffer, (bool Use, int? Card)> answer)
    {
        List<PendingReplacement> asked = [];
        while (e.State.PendingChain is { } chain && e.State.Outcome is null)
        {
            if (chain.ReplacementChoice is { } choice)
            {
                asked.Add(choice.Replacement);
                var (use, card) = answer(choice.Replacement.Current);
                Ok(e, new ChooseDestructionReplacement(choice.Player, choice.DecisionId, use, card));
                continue;
            }
            Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId));
        }
        return asked;
    }

    /// <summary>Plays the current attack to its end, answering any replacement offer with <paramref name="answer"/>.</summary>
    private static List<PendingReplacement> FinishAttack(DuelEngine e, Func<ReplacementOffer, (bool Use, int? Card)> answer)
    {
        List<PendingReplacement> asked = [];
        for (int i = 0; i < 30 && e.State.Outcome is null && e.State.Battle!.Attack is not null; i++)
        {
            if (e.State.Battle.Replacement is { } pending)
            {
                asked.Add(pending);
                var (use, card) = answer(pending.Current);
                Ok(e, new ChooseDestructionReplacement(pending.Current.Player, pending.DecisionId, use, card));
                continue;
            }
            EventEffectScenario.Drain(e);
            if (e.State.Battle!.Attack is null) break;
            S.Close(e); S.Advance(e);
        }
        return asked;
    }

    /// <summary>Turn four: Player One controls <paramref name="first"/> in zone 0 and <paramref name="second"/> in zone 1.</summary>
    private static DuelEngine TwoOwnMonsters(uint first, uint second, uint[] opponent, Action<DuelEngine>? before = null)
    {
        var e = Engine(Deck(first, second), opponent);
        EnterMain(e); Summon(e, first, 0); Next(e); Next(e);
        Summon(e, second, 1); before?.Invoke(e); Next(e);
        return e;
    }

    [Fact]
    public void AnOptionalReplacementIsOfferedAndUsed()
    {
        var e = TwoOwnMonsters(Nettles, Leaf, Deck(Snipe));
        Activate(e, Snipe); Choose(e, Nettles);
        var asked = ResolveAll(e, offer => (true, offer.CardOptions[0].InstanceId));
        Assert.Single(asked);
        Assert.Equal(PlayerId.One, asked[0].Current.Player);
        Assert.Equal(Nettles, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Null(P(e, PlayerId.One).MonsterZones[1]);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Leaf);
    }

    [Fact]
    public void DecliningLetsTheDestructionHappen()
    {
        var e = TwoOwnMonsters(Nettles, Leaf, Deck(Snipe));
        Activate(e, Snipe); Choose(e, Nettles);
        ResolveAll(e, _ => (false, null));
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[1]);
    }

    [Fact]
    public void ACardDestroyedAtTheSameTimeCannotReplaceAnother()
    {
        var e = TwoOwnMonsters(Shocker, Psy, Deck(Hole));
        Activate(e, Hole);
        Assert.Empty(ResolveAll(e, _ => (true, null)));
        Assert.All(P(e, PlayerId.One).MonsterZones, m => Assert.Null(m));
    }

    [Fact]
    public void OneReplacementCanProtectEveryMatchingMonsterForOneCost()
    {
        var e = Engine(Deck(Shocker, Psy), Deck(Psy, PsyHole));
        EnterMain(e); Summon(e, Shocker, 0); Next(e);
        Summon(e, Psy, 0); Next(e);
        Summon(e, Psy, 1); Next(e);
        Activate(e, PsyHole);
        var asked = ResolveAll(e, _ => (true, null));
        Assert.Single(asked);
        Assert.Null(asked[0].Current.Subject);
        Assert.Equal(7500, P(e, PlayerId.One).LifePoints);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[1]);
        Assert.NotNull(P(e, PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void ABattleReplacementIsAskedAtDamageCalculationOncePerTurn()
    {
        uint[] first = [Catapult, W, W, W, W, .. Enumerable.Repeat(Rock, 35)];
        var e = Engine(first, Deck(Big, Big));
        EnterMain(e); Activate(e, Catapult, DuelPlayerState.FieldZoneIndex); ResolveAll(e, _ => (false, null));
        Summon(e, W, 0); Next(e);
        Summon(e, Big, 0); Next(e);
        Summon(e, W, 1); Next(e);
        Summon(e, Big, 1);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        var asked = FinishAttack(e, offer => (true, offer.CardOptions[0].InstanceId));
        Assert.Single(asked);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Rock);
        Assert.Equal(6900, P(e, PlayerId.One).LifePoints);
        S.Close(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 1, 1));
        Assert.Empty(FinishAttack(e, offer => (true, offer.CardOptions[0].InstanceId)));
        Assert.Null(P(e, PlayerId.One).MonsterZones[1]);
    }

    [Fact]
    public void AMandatoryReplacementAppliesWithoutAsking()
    {
        DestructionReplacementProfile aegis = new("aegis-replacement", Aegis, ReplacementSubject.Monsters,
            new(ReplacementAction.SendSourceToGraveyard), DestructionCause.Effect, Optional: false);
        var e = Engine(Deck(Aegis, W), Deck(Snipe), [aegis]);
        EnterMain(e); Activate(e, Aegis, 3); ResolveAll(e, _ => (false, null)); Summon(e, W, 0); Next(e);
        Activate(e, Snipe); Choose(e, W);
        Assert.Empty(ResolveAll(e, _ => (false, null)));
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Aegis);
    }

    [Fact]
    public void AnEngineRejectsReplacementsThatCannotWork()
    {
        var deck = Enumerable.Repeat(W, 40).ToArray();
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", Aegis, ReplacementSubject.Self, new(ReplacementAction.DetachMaterial))]));
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", W, ReplacementSubject.Self, new(ReplacementAction.SendSourceToGraveyard))]));
        Assert.Throws<ArgumentException>(() => Engine(deck, deck, [new("bad", W, ReplacementSubject.Equipped, new(ReplacementAction.DestroySource))]));
    }
}
