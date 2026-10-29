using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

internal static class BattleFlowScenario
{
    internal const uint A = uint.MaxValue - 1, B = uint.MaxValue - 2, Filler = 1;
    internal const uint Negate = 100_001, Destroy = 100_002, Summon = 100_003, Stats = 100_004, Counter = 100_005;

    internal static DuelEngine Engine(int attack = 1800, int defense = 1000, int opposingAttack = 1200, int opposingDefense = 2000,
        bool defender = true, bool faceDown = false, IEnumerable<BattleResponseProfile>? profiles = null,
        IEnumerable<ChainResponseProfile>? chains = null, IEnumerable<TriggerEffectProfile>? triggers = null,
        IEnumerable<ContinuousEffectProfile>? continuous = null, bool staged = true, bool additionalAttacker = false, IEnumerable<ContinuousNegationProfile>? negations = null)
    {
        uint[] ids = [A, B, Filler, Negate, Destroy, Summon, Stats, Counter];
        var catalog = new CardCatalog(new(null, "synthetic-stage8-v1"), ids.Select(id => new CardDefinition(id, $"Probe {id}", "",
            new(id == A ? attack : opposingAttack, id == A ? defense : opposingDefense, 2))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(id is A or B or Filler ? CardCategory.Monster : CardCategory.Trap,
            AllowsNormalSummon: id is A or B or Filler, AllowsBasicBattle: id is A or B or Filler,
            AllowsSpellTrapSet: id is not (A or B or Filler)));
        uint[] first = [A, A, Stats, Counter, Filler, A, .. Enumerable.Repeat(Filler, 34)];
        uint[] second = [B, B, Negate, Destroy, Summon, Stats, Counter, .. Enumerable.Repeat(Filler, 33)];
        var engine = new DuelEngine(catalog, rules, first, second, experimentalChainResponses: chains,
            experimentalTriggerEffects: triggers, experimentalContinuousEffects: continuous, experimentalContinuousNegations: negations,
            experimentalBattleFlow: staged, experimentalBattleResponses: profiles);
        EnterMain(engine); Play(engine, A, 0); Set(engine, Stats, 0); Set(engine, Counter, 1); NextMain(engine);
        if (defender) Play(engine, B, 0, faceDown);
        Set(engine, Negate, 0); Set(engine, Destroy, 1); Set(engine, Summon, 2); Set(engine, Stats, 3);
        NextMain(engine);
        if (additionalAttacker) Play(engine, A, 1);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        return engine;
    }

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
        var before = engine.State; string saved = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded); Assert.Equal(error, result.Error);
        Assert.Same(before, result.State); Assert.Same(before, engine.State);
        Assert.Empty(result.Events); Assert.Equal(saved, JsonSerializer.Serialize(engine.State));
    }
    internal static void EnterMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(player)); Ok(engine, new ChangePhase(player, DuelPhase.Main1));
    }
    private static void NextMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ChangePhase(player, DuelPhase.End)); Ok(engine, new EndTurn(player)); EnterMain(engine);
    }
    private static void Play(DuelEngine engine, uint id, int zone, bool set = false)
    {
        var player = engine.State.ActivePlayer; int card = engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == id).InstanceId;
        Ok(engine, set ? new SetMonster(player, card, zone) : new NormalSummon(player, card, zone));
    }
    private static void Set(DuelEngine engine, uint id, int zone)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new SetSpellTrap(player, engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == id).InstanceId, zone));
    }
    internal static void Close(DuelEngine engine)
    {
        while (!engine.State.Battle!.Window.Closed)
        {
            var w = engine.State.Battle.Window; Ok(engine, new PassBattleResponse(w.Responder, w.DecisionId));
        }
    }
    internal static DuelActionResult Advance(DuelEngine engine) =>
        Ok(engine, new AdvanceBattle(engine.State.ActivePlayer, engine.State.Battle!.Window.DecisionId));
    internal static void Ready(DuelEngine engine)
    {
        Close(engine); Advance(engine); Close(engine);
        Assert.Equal(BattleStep.Battle, engine.State.Battle!.Step);
    }
    internal static DuelActionResult Declare(DuelEngine engine, int? target = 0) =>
        Ok(engine, new DeclareAttack(engine.State.ActivePlayer, 0, target));
    internal static IReadOnlyList<DuelEvent> DrainEffects(DuelEngine engine) => EventEffectScenario.Drain(engine);
    internal static void ToDamage(DuelEngine engine, DamageStep target)
    {
        for (int i = 0; i < 12; i++)
        {
            if (engine.State.Battle!.Step == BattleStep.Damage && engine.State.Battle.DamageStep == target) return;
            DrainEffects(engine); Close(engine); Advance(engine);
        }
        throw new InvalidOperationException("Damage step not reached.");
    }
    internal static IReadOnlyList<DuelEvent> FinishAttack(DuelEngine engine)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 20; i++)
        {
            events.AddRange(DrainEffects(engine));
            if (engine.State.Outcome is not null || engine.State.Battle!.Attack is null) return events;
            Close(engine); events.AddRange(Advance(engine).Events);
        }
        throw new InvalidOperationException("Attack did not finish.");
    }
    internal static DuelActionResult Respond(DuelEngine engine, int zone, int? card = null, int? monsterZone = null)
    {
        var w = engine.State.Battle!.Window;
        return Ok(engine, new ActivateBattleResponse(w.Responder, w.DecisionId, zone, card, monsterZone));
    }
}
