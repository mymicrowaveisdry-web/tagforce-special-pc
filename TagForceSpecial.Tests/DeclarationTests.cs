using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Effects that declare a card name, monster Type or card type as they are activated.</summary>
public sealed class DeclarationTests
{
    private const uint W = 1021, Rock = 1022, Spell = 1023, VampireLady = 5958, Oath = 5789, Virus = 5660, MindCrush = 6147, Designator = 5480;

    private static DuelEngine Engine(uint[] first, uint[] second, ulong seed = 5, EffectDefinition[]? extra = null)
    {
        CardCatalog catalog = new(new(null, "synthetic-declarations"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Rock, "Rock", "", new(1200, 1000, 4), new(5, MonsterTypes.Rock, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Spell, "Spell", "", new(0, 0, 1))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = rules[Rock] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Spell] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, .. extra ?? []], shuffleSeed: seed, ruleLocks: TemplateCardFamily.Family.RuleLocks,
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;

    /// <summary>Declares <paramref name="value"/> when asked, answers other choices with their first option and resolves every chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e, int value)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingCommitment is { } c
                ? c.Kind == TriggerCommitmentKind.Declaration ? new DeclareValue(c.Player, c.DecisionId, value)
                    : new ChooseTriggerZoneTarget(c.Player, c.DecisionId, c.ZoneOptions[0].Player, c.ZoneOptions[0].Card.InstanceId)
                : e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingDecision is { } d ? new ChooseDecisionCard(d.Player, d.DecisionId, d.CardOptions[0].InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.Declaration is { } declaration ? new DeclareValue(declaration.Player, declaration.DecisionId, value)
                : chain.DefinitionCardChoice is { } pick ? new ChooseDefinitionCard(pick.Player, pick.DecisionId, pick.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    [Fact]
    public void AnActivatedEffectDeclaresBeforeEitherPlayerCanRespond()
    {
        var e = Engine(Deck(Oath, W, W, W, W, W), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Oath), 0));
        Drive(e, 0);
        var activated = Ok(e, new ActivateCardEffect(PlayerId.One, 0, 1)).Events;
        var asked = Assert.Single(activated.OfType<DeclarationRequested>()).Decision;
        Assert.Equal((PlayerId.One, DeclarationKind.CardName), (asked.Player, asked.Kind));
        Assert.DoesNotContain(activated, ev => ev is ChainResponseRequested);
        Reject(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId), DuelError.DecisionPending);
        Reject(e, new DeclareValue(PlayerId.One, asked.DecisionId, 999_999), DuelError.InvalidDecisionChoice);
        int hand = P(e, PlayerId.One).Hand.Count;
        var events = Drive(e, (int)W);
        Assert.Equal((int)W, events.OfType<DeclarationMade>().Single().Value);
        Assert.Single(events.OfType<CardSearched>());
        Assert.Equal(hand + 1, P(e, PlayerId.One).Hand.Count);
        Assert.Equal(7500, P(e, PlayerId.One).LifePoints);
    }

    [Fact]
    public void AWrongNameSendsTheExcavatedCardToTheGraveyard()
    {
        var e = Engine(Deck(Oath, W, W, W, W, W), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Oath), 0));
        Drive(e, 0);
        Ok(e, new ActivateCardEffect(PlayerId.One, 0, 1));
        int graveyard = P(e, PlayerId.One).Graveyard.Count;
        var events = Drive(e, (int)Spell);
        Assert.Empty(events.OfType<CardSearched>());
        Assert.Equal(graveyard + 1, P(e, PlayerId.One).Graveyard.Count);
    }

    [Fact]
    public void ADeclaredTypeChoosesWhichMonstersAreDestroyed()
    {
        var e = Engine(Deck(Virus, Spell), Deck(Rock, W));
        EnterMain(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, Rock), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Virus), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Drive(e, (int)MonsterTypes.Rock);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void ANamedCardInTheOpponentsHandIsDiscardedOtherwiseTheActivatorDiscardsAtRandom()
    {
        var hit = Engine(Deck(MindCrush), Deck());
        EnterMain(hit); Ok(hit, new SetSpellTrap(PlayerId.One, Hand(hit, PlayerId.One, MindCrush), 0)); Next(hit); Next(hit);
        Ok(hit, new ActivateSetSpell(PlayerId.One, 0));
        Drive(hit, (int)W);
        Assert.DoesNotContain(P(hit, PlayerId.Two).Hand, c => c.GameCardId == W);

        var miss = Engine(Deck(MindCrush), Deck());
        EnterMain(miss); Ok(miss, new SetSpellTrap(PlayerId.One, Hand(miss, PlayerId.One, MindCrush), 0)); Next(miss); Next(miss);
        int own = P(miss, PlayerId.One).Hand.Count, theirs = P(miss, PlayerId.Two).Hand.Count;
        Ok(miss, new ActivateSetSpell(PlayerId.One, 0));
        var checkedHand = Assert.Single(Drive(miss, (int)Spell).OfType<CardsLookedAt>());
        Assert.Equal((PlayerId.One, PlayerId.Two), (checkedHand.Viewer, checkedHand.Owner));
        Assert.Equal(own - 1, P(miss, PlayerId.One).Hand.Count);
        Assert.Equal(theirs, P(miss, PlayerId.Two).Hand.Count);
    }

    [Fact]
    public void AMonsterNameDeclarationRejectsOtherCardsAndAddsFromTheOpponentsDeck()
    {
        var e = Engine(Deck(Designator), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Designator), 0));
        var asked = e.State.PendingChain!.Declaration!;
        Reject(e, new DeclareValue(PlayerId.One, asked.DecisionId, (int)Spell), DuelError.InvalidDecisionChoice);
        Reject(e, new DeclareValue(PlayerId.Two, asked.DecisionId, (int)W), DuelError.NotActivePlayer);
        int hand = P(e, PlayerId.Two).Hand.Count, deck = P(e, PlayerId.Two).Deck.Count;
        Drive(e, (int)W);
        Assert.Equal(hand + 1, P(e, PlayerId.Two).Hand.Count);
        Assert.Equal(deck - 1, P(e, PlayerId.Two).Deck.Count);
    }

    [Fact]
    public void ABattleDamageTriggerDeclaresACardTypeTheOpponentSends()
    {
        var e = Engine(Deck(VampireLady), Deck(Spell, Spell, Spell, Spell, Spell, Spell, Spell));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, VampireLady), 0)); Next(e); Next(e);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
        List<DuelEvent> events = [];
        for (int i = 0; i < 30 && e.State.Battle?.Attack is not null; i++)
        {
            events.AddRange(Drive(e, (int)CardCategory.Spell));
            if (e.State.Battle?.Attack is null) break;
            S.Close(e); events.AddRange(S.Advance(e).Events);
        }
        events.AddRange(Drive(e, (int)CardCategory.Spell));
        var made = Assert.Single(events.OfType<DeclarationMade>());
        Assert.Equal((PlayerId.One, DeclarationKind.CardType), (made.Player, made.Kind));
        var sent = Assert.Single(events.OfType<CardSentFromDeckToGraveyard>());
        Assert.Equal((PlayerId.Two, Spell), (sent.Player, sent.Card.GameCardId));
    }

    [Fact]
    public void ADeclaredFilterNeedsAnEffectThatDeclares() =>
        Assert.Throws<ArgumentException>(() => Engine(Deck(), Deck(), extra: [new EffectDefinition("undeclared", Spell, EffectKind.NormalSpell,
            [new OpponentAddsFromDeckStep(new DeclaredFilter())])]));
}
