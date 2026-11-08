using System.Runtime.CompilerServices;
using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

internal static class EventEffectScenario
{
    internal const uint A = 100_000, B = uint.MaxValue, Filler = 1, Trap = 2, SummonSpell = 3;

    internal static DuelEngine Engine(IEnumerable<TriggerEffectProfile>? triggers = null,
        IEnumerable<ContinuousEffectProfile>? continuous = null, IEnumerable<EffectDefinition>? responses = null,
        PlayerId startingPlayer = PlayerId.One, int? attackA = 1000, int? defenseA = 1000,
        int? attackB = 2000, int? defenseB = 1000,
        IEnumerable<uint>? playerOneDeck = null, IEnumerable<uint>? playerTwoDeck = null,
        IEnumerable<ContinuousNegationProfile>? negations = null)
    {
        uint[] ids = [A, B, Filler, Trap, SummonSpell, 5217, 4909, 4818, 3905, 3903, 3902, 3904];
        var catalog = new CardCatalog(new(null, "synthetic-event-effects-v1"), ids.Select(id =>
            new CardDefinition(id, $"Probe {id}", "", new(id == A ? attackA : id == B ? attackB : 1000,
                id == A ? defenseA : id == B ? defenseB : 1000, 2))));
        var rules = ids.ToDictionary(id => id, id => ChainCardFamily.Rules.TryGetValue(id, out var shared) ? shared :
            new DuelCardRules(id is Trap ? CardCategory.Trap : id is SummonSpell ? CardCategory.Spell : CardCategory.Monster,
                AllowsNormalSummon: id is A or B or Filler, AllowsBasicBattle: id is A or B or Filler,
                AllowsSpellTrapSet: id is Trap or SummonSpell));
        uint[] first = [A, A, Trap, 5217, SummonSpell, A, 4909, .. Enumerable.Repeat(Filler, 33)];
        uint[] second = [B, B, Trap, 4818, Filler, .. Enumerable.Repeat(Filler, 35)];
        return new(catalog, rules, playerOneDeck ?? first, playerTwoDeck ?? second, startingPlayer,
            experimentalTriggerEffects: triggers, experimentalContinuousEffects: continuous, experimentalContinuousNegations: negations,
            effectDefinitions: [SpecialProbe, .. ChainCards(5217, 4909, 4818), .. responses ?? []], shuffleSeed: 1);
    }

    /// <summary>Normal Spell that Special Summons A from the Deck, banished in the End Phase.</summary>
    internal static EffectDefinition SpecialProbe { get; } = new("special-probe", SummonSpell, EffectKind.NormalSpell,
        [new SpecialSummonStep(SpecialSummonSources.Deck, [A], banishAtEndPhase: true)]);

    internal static EffectDefinition[] ChainCards(params uint[] ids) =>
        ChainCardFamily.Definitions.Where(d => ids.Contains(d.CardId)).ToArray();

    /// <summary>Activates the special probe from hand, closes the window and summons the first option.</summary>
    internal static DuelActionResult SummonWithProbe(DuelEngine engine, int spellZone = 0, int monsterZone = 1)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ActivateSpell(actor, engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == SummonSpell).InstanceId, spellZone));
        Pass(engine); Pass(engine);
        var paused = Resolve(engine);
        var choice = Assert.IsType<DefinitionSummonRequested>(paused.Events[^1]).Decision;
        return Ok(engine, new ChooseDefinitionSummon(actor, choice.DecisionId, choice.Options[0].Card.InstanceId, monsterZone,
            BattlePosition.FaceUpAttack));
    }

    /// <summary>A phase change that passes any open window offered to the non-turn player.</summary>
    internal static void Phase(DuelEngine engine, DuelPhase target)
    {
        Ok(engine, new ChangePhase(engine.State.ActivePlayer, target));
        if (engine.State.OpenWindow is { } window) Ok(engine, new PassChainResponse(window.Responder, window.DecisionId));
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
        Phase(engine, DuelPhase.Main1);
    }

    internal static void NextMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        if (engine.State.Phase == DuelPhase.Battle) Phase(engine, DuelPhase.Main2);
        if (engine.State.Phase != DuelPhase.End) Phase(engine, DuelPhase.End);
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

    private static readonly ConditionalWeakTable<DuelEngine, Tuple<int, int>> plannedSummons = new();

    /// <summary>Sets the card and zone the next resolution-time definition summon picks; otherwise the first of each.</summary>
    internal static void PlanSummon(DuelEngine engine, int cardInstanceId, int monsterZone) =>
        plannedSummons.AddOrUpdate(engine, Tuple.Create(cardInstanceId, monsterZone));

    /// <summary>Answers a pending definition summon in face-up Defense Position, using any planned card and zone.</summary>
    internal static DuelActionResult ChooseSummon(DuelEngine engine)
    {
        var summon = engine.State.PendingChain!.DefinitionSummon!;
        var (card, zone) = plannedSummons.TryGetValue(engine, out var plan)
            ? (plan.Item1, plan.Item2) : (summon.Options[0].Card.InstanceId, summon.MonsterZoneOptions[0]);
        plannedSummons.Remove(engine);
        return Ok(engine, new ChooseDefinitionSummon(summon.Player, summon.DecisionId, card, zone, BattlePosition.FaceUpDefense));
    }

    internal static IReadOnlyList<DuelEvent> Drain(DuelEngine engine)
    {
        List<DuelEvent> events = [];
        for (int step = 0; step < 100; step++)
        {
            if (engine.State.EventEffects.PendingCommitment is { } commitment)
            {
                var command = commitment.Kind == TriggerCommitmentKind.DetachCost
                    ? (DuelCommand)new ChooseTriggerXyzMaterials(commitment.Player, commitment.DecisionId,
                        commitment.CardOptions.Take(commitment.Trigger.Profile.DetachMaterialCount).Select(c => c.InstanceId))
                    : commitment.Kind == TriggerCommitmentKind.DiscardCost
                    ? (DuelCommand)new ChooseTriggerCostCard(commitment.Player, commitment.DecisionId, commitment.CardOptions[0].InstanceId)
                    : new ChooseTriggerMonsterTarget(commitment.Player, commitment.DecisionId, commitment.TargetOptions[0].Player,
                        commitment.TargetOptions[0].Zone, commitment.TargetOptions[0].Card.InstanceId);
                events.AddRange(Ok(engine, command).Events);
            }
            else if (engine.State.EventEffects.PendingTriggers is not null) events.AddRange(Order(engine).Events);
            else if (engine.State.PendingChain?.DefinitionSummon is not null) events.AddRange(ChooseSummon(engine).Events);
            else if (engine.State.PendingChain is { NextLink: 0 }) Pass(engine);
            else if (engine.State.PendingChain is not null) events.AddRange(Resolve(engine).Events);
            else return events;
        }
        throw new InvalidOperationException("Effect workflow did not terminate.");
    }
}
