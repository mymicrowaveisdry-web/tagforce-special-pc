using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

internal static class EventEffectScenario
{
    internal const uint A = 100_000, B = uint.MaxValue, Filler = 1, Trap = 2, SummonSpell = 3;

    internal static DuelEngine Engine(IEnumerable<TriggerEffectProfile>? triggers = null,
        IEnumerable<ContinuousEffectProfile>? continuous = null, IEnumerable<ChainResponseProfile>? responses = null,
        PlayerId startingPlayer = PlayerId.One, int? attackA = 1000, int? defenseA = 1000,
        int? attackB = 2000, int? defenseB = 1000,
        IEnumerable<uint>? playerOneDeck = null, IEnumerable<uint>? playerTwoDeck = null)
    {
        uint[] ids = [A, B, Filler, Trap, SummonSpell, 5217, 4909, 4818, 3905, 3903, 3902, 3904];
        var catalog = new CardCatalog(new(null, "synthetic-event-effects-v1"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(id == A ? attackA : id == B ? attackB : 1000,
                id == A ? defenseA : id == B ? defenseB : 1000, 2))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is Trap ? CardCategory.Trap : id is SummonSpell or 5217 or 4909 or 4818 ? CardCategory.Spell : CardCategory.Monster,
            AllowsNormalSummon: id is A or B or Filler,
            AllowsBasicBattle: id is A or B or Filler or 3905 or 3903 or 3902 or 3904,
            AllowsSpellTrapSet: id is Trap or SummonSpell or 5217 or 4909 or 4818));
        uint[] first = [A, A, Trap, 5217, SummonSpell, A, 4909, .. Enumerable.Repeat(Filler, 33)];
        uint[] second = [B, B, Trap, 4818, Filler, .. Enumerable.Repeat(Filler, 35)];
        return new(catalog, rules, playerOneDeck ?? first, playerTwoDeck ?? second, startingPlayer,
            experimentalSpellEffects: [new("special-probe", SummonSpell, [A], banishAtEndPhase: true)],
            experimentalActivationContracts: [ActivationResearch.LightningVortex([Filler, A]), ActivationResearch.MysticalSpaceTyphoon()],
            experimentalChainEffects: [ChainResearch.LightningVortex(), ChainResearch.MysticalSpaceTyphoon(), ChainResearch.Scapegoat()],
            experimentalChainResponses: responses,
            experimentalTriggerEffects: triggers, experimentalContinuousEffects: continuous);
    }

    internal static TriggerEffectProfile Gain(string id, uint card, MonsterEventKind kind = MonsterEventKind.NormalSummoned,
        EffectRelation relation = EffectRelation.Self, TriggerRequirement requirement = TriggerRequirement.Mandatory,
        int gain = 100, TriggerSourceLocation location = TriggerSourceLocation.FaceUpMonster) =>
        new(id, card, kind, relation, requirement, TriggerOperation.GainLifePoints, lifePointGain: gain, sourceLocation: location);

    internal static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        long revision = engine.State.Revision;
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Equal(revision + 1, result.State.Revision);
        return result;
    }

    internal static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        string snapshot = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, result.State);
        Assert.Same(before, engine.State);
        Assert.Empty(result.Events);
        Assert.Equal(snapshot, JsonSerializer.Serialize(engine.State));
    }

    internal static void EnterMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(player));
        Ok(engine, new ChangePhase(player, DuelPhase.Main1));
    }

    internal static void NextMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        if (engine.State.Phase == DuelPhase.Battle) Ok(engine, new ChangePhase(player, DuelPhase.Main2));
        if (engine.State.Phase != DuelPhase.End) Ok(engine, new ChangePhase(player, DuelPhase.End));
        while (engine.State.GetPlayer(player).Hand.Count > 6)
            Ok(engine, new DiscardForHandLimit(player, engine.State.GetPlayer(player).Hand[^1].InstanceId));
        Ok(engine, new EndTurn(player));
        EnterMain(engine);
    }

    internal static DuelActionResult Summon(DuelEngine engine, uint card, int zone = 0, bool set = false)
    {
        var player = engine.State.ActivePlayer;
        var instance = engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == card);
        return Ok(engine, set ? new SetMonster(player, instance.InstanceId, zone) : new NormalSummon(player, instance.InstanceId, zone));
    }

    internal static void Set(DuelEngine engine, uint card, int zone = 0)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new SetSpellTrap(player, engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == card).InstanceId, zone));
    }

    internal static DuelActionResult Order(DuelEngine engine, bool reverse = false, bool decline = false)
    {
        var batch = engine.State.EventEffects.PendingTriggers!;
        var ids = batch.Options.Select(t => t.Id);
        return Ok(engine, new ChooseTriggerOrder(batch.Player, batch.DecisionId,
            decline ? [] : (reverse ? ids.Reverse() : ids).ToArray()));
    }

    internal static void Pass(DuelEngine engine)
    {
        var chain = engine.State.PendingChain!;
        Ok(engine, new PassChainResponse(chain.Responder, chain.DecisionId));
    }

    internal static DuelActionResult Resolve(DuelEngine engine) =>
        Ok(engine, new ResolveNextChainLink(engine.State.ActivePlayer, engine.State.PendingChain!.DecisionId));

    internal static IReadOnlyList<DuelEvent> Drain(DuelEngine engine)
    {
        List<DuelEvent> events = [];
        for (int step = 0; step < 100; step++)
        {
            if (engine.State.EventEffects.PendingTriggers is not null) events.AddRange(Order(engine).Events);
            else if (engine.State.PendingChain is { NextLink: 0 }) Pass(engine);
            else if (engine.State.PendingChain is not null) events.AddRange(Resolve(engine).Events);
            else return events;
        }
        throw new InvalidOperationException("Effect workflow did not terminate.");
    }
}
