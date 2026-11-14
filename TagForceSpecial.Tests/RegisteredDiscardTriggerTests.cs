using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

public sealed class RegisteredDiscardTriggerTests
{
    private const uint Spell = 950, FillerCard = 951, Snake = 4469, Minar = 4540;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ActualRegisteredTriggersRespectControllerAndDestination(bool ownEffect, bool redirect)
    {
        var family = ShortTextCardFamily.Family;
        uint[] ids = [Spell, FillerCard, Snake, Minar];
        var catalog = new CardCatalog(new(null, "synthetic-registered-discard-trigger-scenario"),
            ids.Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4))));
        var rules = family.Rules.Where(p => p.Key is Snake or Minar).ToDictionary();
        rules[Spell] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[FillerCard] = new(CardCategory.Monster, AllowsNormalSummon: true);
        var engine = new DuelEngine(catalog, rules,
            [Snake, Minar, Spell, FillerCard, FillerCard, .. Enumerable.Repeat(FillerCard, 35)],
            [Spell, .. Enumerable.Repeat(FillerCard, 39)],
            effectDefinitions: [new("forced-discard", Spell, EffectKind.NormalSpell,
                [new DiscardCardsStep(2, ownEffect ? EffectPlayers.Self : EffectPlayers.Opponent)])],
            experimentalTriggerEffects: family.Triggers.Where(t => t.CardId is Snake or Minar),
            graveyardRedirects: redirect ? [new("redirect", FillerCard, new GraveyardRedirect())] : [], shuffleSeed: 9);
        EnterMain(engine);
        if (redirect) Summon(engine, FillerCard);
        if (!ownEffect) NextMain(engine);
        var actor = engine.State.ActivePlayer;
        int spell = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == Spell).InstanceId;
        Ok(engine, new ActivateSpell(actor, spell, 0));
        while (engine.State.PendingChain is { DefinitionCardChoice: null })
        {
            var chain = engine.State.PendingChain;
            Ok(engine, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(actor, chain.DecisionId));
        }
        var selected = new List<CardInstance>();
        foreach (uint id in new[] { Snake, Minar })
        {
            var choice = engine.State.PendingChain!.DefinitionCardChoice!;
            var card = choice.Options.Single(c => c.GameCardId == id);
            selected.Add(card);
            Ok(engine, new ChooseDefinitionCard(choice.Player, choice.DecisionId, card.InstanceId));
        }
        if (ownEffect || redirect)
        {
            Assert.Null(engine.State.EventEffects.PendingTriggers);
            Drain(engine);
            Assert.Equal(8000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
        }
        else
        {
            var triggers = engine.State.EventEffects.PendingTriggers!.Options;
            Assert.Equal(2, triggers.Count);
            Assert.Equal(selected.OrderBy(c => c.InstanceId), triggers.Select(t => t.Source.Card).OrderBy(c => c.InstanceId));
            Assert.All(triggers, t => Assert.Equal(PlayerId.Two, t.Occurrence.DiscardEffectController));
            int deck = engine.State.GetPlayer(PlayerId.One).Deck.Count;
            var events = Drain(engine);
            Assert.Equal(deck - 2, engine.State.GetPlayer(PlayerId.One).Deck.Count);
            Assert.Equal(7000, engine.State.GetPlayer(PlayerId.Two).LifePoints);
            Assert.NotEmpty(events);
        }
    }
}
