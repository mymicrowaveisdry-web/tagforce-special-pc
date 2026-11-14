using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Resolution-time Graveyard choices reached through ordinary discard costs and chain responses.</summary>
public sealed class GraveyardSummonStepTests
{
    private const uint Target = 1, Restricted = 2, Filler = 3, Revive = 10, Seed = 11, Response = 12;

    private static DuelEngine Engine(SpecialSummonSources sources = SpecialSummonSources.Graveyard,
        bool filter = false, bool negate = false, bool restricted = false)
    {
        uint[] ids = [Target, Restricted, Filler, Revive, Seed, Response];
        var catalog = new CardCatalog(new(null, "synthetic-graveyard-choice"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4))));
        var rules = ids.ToDictionary(id => id, id => id < Revive
            ? new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true)
            : new DuelCardRules(id == Response ? CardCategory.Trap : CardCategory.Spell, AllowsSpellTrapSet: true));
        var summon = filter ? new SpecialSummonStep(sources, new CardIdFilter([Target, Restricted]))
            : new SpecialSummonStep(sources, [Target, Restricted]);
        return new(catalog, rules,
            [Revive, Seed, restricted ? Restricted : Target, Filler, Filler, Filler, Target, .. Enumerable.Repeat(Filler, 33)],
            [Response, Filler, Filler, Filler, Filler, .. Enumerable.Repeat(Filler, 35)],
            experimentalSpecialSummonCards: [new(Restricted, SpecialSummonSources.Graveyard, "proper")],
            effectDefinitions:
            [
                new("revive", Revive, EffectKind.NormalSpell, [summon]),
                new("seed", Seed, EffectKind.NormalSpell, [new DealDamageStep(1)], cost: new DiscardCost()),
                negate ? ResponseProbe.Negate("response", Response, ResponseNegation.Effect, ChainSpellSpeed.Two)
                    : new("response", Response, EffectKind.NormalTrap, [new BanishTargetsStep()],
                        fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Opponent, new AnyMonsterFilter())])
            ], shuffleSeed: 7);
    }

    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void SeedGraveyard(DuelEngine e, uint id = Target)
    {
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Seed), 4));
        var p = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(p.Player, p.DecisionId, Hand(e, id)));
        Resolve(e);
    }

    private static List<DuelEvent> ResolveToChoice(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.PendingChain is { DefinitionSummon: null } chain)
            events.AddRange(Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId)).Events);
        return events;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraveyardChoiceIsMadeAtResolutionAndKeepsTheInstanceWithoutShuffling(bool filter)
    {
        var e = Engine(filter: filter); SeedGraveyard(e);
        var grave = e.State.GetPlayer(PlayerId.One).Graveyard.Single(c => c.GameCardId == Target);
        var deck = e.State.GetPlayer(PlayerId.One).Deck.ToArray();
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Revive), 4));
        Assert.Null(e.State.PendingDecision);
        Assert.Null(e.State.PendingChain!.DefinitionSummon);
        ResolveToChoice(e);
        var p = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal(grave, Assert.Single(p.Options).Card);
        Assert.Equal(DecisionCardSource.Graveyard, p.Options[0].Source);
        Reject(e, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId - 1, grave.InstanceId, 0, BattlePosition.FaceUpAttack), DuelError.StaleDecision);
        Reject(e, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId, Hand(e, Filler), 0, BattlePosition.FaceUpAttack), DuelError.InvalidDecisionChoice);
        var before = e.State;
        var result = Ok(e, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId, grave.InstanceId, 0, BattlePosition.FaceUpDefense));
        Assert.Contains(grave, before.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(grave, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card);
        Assert.DoesNotContain(grave, e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(deck, e.State.GetPlayer(PlayerId.One).Deck);
        Assert.Empty(result.Events.OfType<DeckShuffled>());
        Assert.Equal(DecisionCardSource.Graveyard, Assert.Single(result.Events.OfType<MonsterSpecialSummoned>()).Source);
        Assert.False(e.State.NormalSummonUsed);
    }

    [Fact]
    public void CombinedSourcesOfferDeckThenGraveyardAndShuffleOnlyForTheDeckPick()
    {
        foreach (var from in new[] { DecisionCardSource.Deck, DecisionCardSource.Graveyard })
        {
            var e = Engine(SpecialSummonSources.Deck | SpecialSummonSources.Graveyard); SeedGraveyard(e);
            Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Revive), 4)); ResolveToChoice(e);
            var p = e.State.PendingChain!.DefinitionSummon!;
            Assert.Equal(new[] { DecisionCardSource.Deck, DecisionCardSource.Graveyard }, p.Options.Select(o => o.Source));
            var pick = p.Options.Single(o => o.Source == from);
            var events = Ok(e, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId, pick.Card.InstanceId, 0, BattlePosition.FaceUpAttack)).Events;
            Assert.Equal(from == DecisionCardSource.Deck ? 1 : 0, events.OfType<DeckShuffled>().Count());
            Assert.Equal(pick.Card, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card);
        }
    }

    [Fact]
    public void MissingOrImproperlySummonedGraveyardMonsterRejectsBeforeCommit()
    {
        var empty = Engine(); EnterMain(empty);
        Reject(empty, new ActivateSpell(PlayerId.One, Hand(empty, Revive), 4), DuelError.DecisionUnavailable);
        var improper = Engine(restricted: true); SeedGraveyard(improper, Restricted);
        Reject(improper, new ActivateSpell(PlayerId.One, Hand(improper, Revive), 4), DuelError.DecisionUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainedRemovalOrNegationDoesNotRequestAStaleGraveyardChoice(bool negate)
    {
        var e = Engine(negate: negate); SeedGraveyard(e);
        Next(e);
        var trap = e.State.GetPlayer(PlayerId.Two).Hand.Single(c => c.GameCardId == Response);
        Ok(e, new SetSpellTrap(PlayerId.Two, trap.InstanceId, 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Revive), 4));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        if (!negate)
        {
            var p = e.State.PendingDecision!;
            var target = Assert.Single(p.FieldTargetOptions!);
            Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId));
        }
        var events = ResolveToChoice(e);
        Assert.Null(e.State.PendingChain);
        Assert.Empty(events.OfType<DefinitionSummonRequested>());
        Assert.Empty(events.OfType<MonsterSpecialSummoned>());
        Assert.All(e.State.GetPlayer(PlayerId.One).MonsterZones, Assert.Null);
        Assert.Equal(negate ? 0 : 1, e.State.GetPlayer(PlayerId.One).Banished.Count);
    }

    [Fact]
    public void AuthoredMixedSourceSpellRequiresItsFieldConditionAndOffersBothSources()
    {
        const uint dog = 3516, spell = 3518;
        var family = ShortTextCardFamily.Family;
        uint[] ids = [dog, spell, Seed, Filler];
        var catalog = new CardCatalog(new(null, "synthetic-mixed-source-proof"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(1200, 1100, 4),
                new CardNumericCodes(MonsterAttributes.Wind, MonsterTypes.Beast, 1, 1, 0, 0, 0))));
        var rules = family.Rules.Where(p => p.Key is dog or spell).ToDictionary();
        rules[Seed] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Filler] = new(CardCategory.Monster, AllowsNormalSummon: true);
        var e = new DuelEngine(catalog, rules,
            [spell, dog, dog, Seed, Filler, Filler, dog, .. Enumerable.Repeat(Filler, 33)],
            Enumerable.Repeat(Filler, 40), effectDefinitions:
            [.. family.Definitions.Where(d => d.CardId is dog or spell),
                new("seed", Seed, EffectKind.NormalSpell, [new DealDamageStep(1)], cost: new DiscardCost())], shuffleSeed: 7);
        SeedGraveyard(e, dog);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, spell), 4), DuelError.ActivationConditionNotMet);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, dog), 0));
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, spell), 4)); ResolveToChoice(e);
        var p = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal(new[] { DecisionCardSource.Deck, DecisionCardSource.Graveyard }, p.Options.Select(o => o.Source));
        var pick = p.Options.Single(o => o.Source == DecisionCardSource.Graveyard);
        Ok(e, new ChooseDefinitionSummon(PlayerId.One, p.DecisionId, pick.Card.InstanceId, 1, BattlePosition.FaceUpAttack));
        Assert.Equal(pick.Card, e.State.GetPlayer(PlayerId.One).MonsterZones[1]!.Card);
    }
}
