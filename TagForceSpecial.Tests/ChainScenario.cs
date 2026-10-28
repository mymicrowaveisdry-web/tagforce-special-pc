using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

internal static class ChainScenario
{
    internal static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        return result;
    }

    internal static DuelEngine Engine(uint spell, bool chains = true, bool twoMonsters = false, IEnumerable<ChainEffectProfile>? profiles = null)
    {
        uint[] ids = [1, 9979, 7716, 6151, 7747, 9723, 5217, 4909, 4818, 4815, 4664, 4028, 3902, 3903, 3904, 3905];
        var spells = new HashSet<uint> { 7747, 5217, 4909, 4818, 4815 };
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-activation-projection-inputs"),
            ids.Select(id => new CardDefinition(id, $"Synthetic {id}", "", new(1000, 1000, 2),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 2u << 11, 0))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(spells.Contains(id) ? CardCategory.Spell :
            id == 4664 ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: !spells.Contains(id) && id != 4664, AllowsSpellTrapSet: spells.Contains(id) || id == 4664));
        uint[] p1 = [9979, 7716, 9979, 6151, 7747, 7716, 9723, spell, 1, 7716, .. Enumerable.Repeat(1u, 30)];
        uint[] p2 = [4818, 4909, 4815, 4664, 4028, .. Enumerable.Repeat(1u, 35)];
        var engine = new DuelEngine(catalog, rules, p1, p2, experimentalActivationContracts:
            [ActivationResearch.LightningVortex(ids), ActivationResearch.MysticalSpaceTyphoon()],
            experimentalChainEffects: chains ? profiles ?? [ChainResearch.LightningVortex(), ChainResearch.MysticalSpaceTyphoon(), ChainResearch.Scapegoat()] : []);
        EnterMain(engine);
        while (engine.State.TurnNumber < 7)
        {
            var actor = engine.State.ActivePlayer;
            if (actor == PlayerId.One && engine.State.TurnNumber is 1 or 3)
            {
                uint cardId = engine.State.TurnNumber == 1 ? 7716u : 6151u;
                var card = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == cardId);
                Ok(engine, new NormalSummon(actor, card.InstanceId, engine.State.TurnNumber == 1 ? 2 : 3));
            }
            if (actor == PlayerId.Two && engine.State.TurnNumber == 2)
                for (int zone = 1; zone <= 4; zone++)
                {
                    uint id = new uint[] { 4818, 4909, 4815, 4664 }[zone - 1];
                    Ok(engine, new SetSpellTrap(actor, engine.State.GetPlayer(actor).Hand.Single(c => c.GameCardId == id).InstanceId, zone));
                }
            if (actor == PlayerId.Two && (engine.State.TurnNumber == 2 || twoMonsters && engine.State.TurnNumber == 4))
            {
                var monster = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == (engine.State.TurnNumber == 2 ? 4028u : 1u));
                Ok(engine, new NormalSummon(actor, monster.InstanceId, engine.State.TurnNumber == 2 ? 2 : 0));
            }
            Ok(engine, new ChangePhase(actor, DuelPhase.End));
            while (engine.State.GetPlayer(actor).Hand.Count > 6)
                Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
            Ok(engine, new EndTurn(actor));
            EnterMain(engine);
        }
        var source = engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == spell);
        Ok(engine, new ActivateSpell(PlayerId.One, source.InstanceId, 2));
        return engine;
    }

    internal static void EnterMain(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(actor));
        Ok(engine, new ChangePhase(actor, DuelPhase.Main1));
    }

    internal static void Projection(JsonElement expected, DuelState state)
    {
        Assert.Equal(expected.GetProperty("activePlayer").GetInt32(), (int)state.ActivePlayer);
        Assert.Equal(expected.GetProperty("turnNumber").GetInt32(), state.TurnNumber);
        Assert.Equal(expected.GetProperty("phase").GetString(), state.Phase.ToString());
        var e = expected.GetProperty("players")[0];
        var p = state.GetPlayer(PlayerId.One);
        uint[] Ids(string property) => e.GetProperty(property).EnumerateArray().Select(c => c.GetUInt32()).ToArray();
        Assert.Equal(e.GetProperty("handCount").GetInt32(), p.Hand.Count);
        Assert.Equal(e.GetProperty("deckCount").GetInt32(), p.Deck.Count);
        Assert.Equal(Ids("handCardIds"), p.Hand.Select(c => c.GameCardId));
        Assert.Equal(Ids("monsterCardIds"), p.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        Assert.Equal(Ids("spellTrapCardIds"), p.SpellTrapZones.Select(c => c?.GameCardId ?? 0));
        Assert.Equal(Ids("graveyardCardIds"), p.Graveyard.Select(c => c.GameCardId));
    }

}
