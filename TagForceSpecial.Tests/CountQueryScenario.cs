using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using Xunit.Abstractions;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic count scenarios reached through public commands, with optional semantic trace output.</summary>
internal sealed class CountQueryScenario
{
    internal const uint Probe = 101, Preparation = 102, FirstMonster = 1, SecondMonster = 2;
    internal DuelEngine Engine { get; }
    internal List<DuelEvent> Events { get; } = [];
    private readonly ITestOutputHelper? output;

    internal CountQueryScenario(EffectDefinition probe, PlayerId controller = PlayerId.One,
        EffectStep[]? preparation = null, ITestOutputHelper? output = null, bool random = false, bool mixedSpells = false)
    {
        this.output = output;
        CardCatalog catalog = new(new(null, "synthetic-count-queries"),
        [
            new(FirstMonster, "First monster", "", new(1000, 1000, 4), new(1, 1, 1, 1, 0, 0, 0)),
            new(SecondMonster, "Second monster", "", new(1000, 1000, 4), new(1, 2, 1, 1, 0, 0, 0)),
            new(Probe, "Count probe", "", new(0, 0, 0)),
            new(Preparation, "Preparation", "", new(0, 0, 0))
        ]);
        catalogForTrace = catalog;
        Dictionary<uint, DuelCardRules> rules = new()
        {
            [FirstMonster] = new(CardCategory.Monster, AllowsNormalSummon: true),
            [SecondMonster] = new(CardCategory.Monster, AllowsNormalSummon: true, IsGemini: true),
            [Probe] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
            [Preparation] = new(CardCategory.Spell, AllowsSpellTrapSet: true)
        };
        uint[] Deck(uint monster) => random ? mixedSpells ? [.. Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? Probe : Preparation)] : Enumerable.Repeat(Probe, 40).ToArray()
            : [Probe, Preparation, .. Enumerable.Repeat(monster, 38)];
        Engine = new(catalog, rules, Deck(FirstMonster), Deck(SecondMonster), controller,
            effectDefinitions: [probe, new("prepare", Preparation, EffectKind.NormalSpell,
                preparation ?? [new GainLifePointsStep(1)])], shuffleSeed: random ? 37UL : null);
    }

    internal void Execute(DuelCommand command, DuelError expected = DuelError.None)
    {
        var before = Engine.State;
        var result = Engine.Execute(command);
        Assert.Equal(expected, result.Error);
        Assert.Equal(expected == DuelError.None, result.Succeeded);
        if (expected != DuelError.None)
        {
            Assert.Same(before, Engine.State);
            Assert.Empty(result.Events);
        }
        Events.AddRange(result.Events);
        if (output is null) return;
        var actions = new DuelActionSource(Engine, catalogForTrace);
        output.WriteLine("COUNT_TRACE " + JsonSerializer.Serialize(new
        {
            CommandType = command.GetType().Name,
            Command = JsonSerializer.SerializeToElement(command, command.GetType()),
            result.Error,
            State = Engine.State,
            MonsterDetails = Engine.State.Players.Select(p => new
            {
                p.Id,
                Zones = p.MonsterZones.Select(m => m is null ? null : new
                {
                    m.Card, m.FlippedOnTurn, m.GeminiActive, m.SummonTributes, m.Level,
                    m.Counters, m.ProtectionCounters, m.AttackLockedTurn,
                    Identity = Engine.GetCardIdentityState(m.Card)
                })
            }),
            Events = result.Events.Select(e => new { Type = e.GetType().Name, Value = JsonSerializer.SerializeToElement(e, e.GetType()) }),
            Views = new[] { actions.Observe(PlayerId.One), actions.Observe(PlayerId.Two) }
        }));
    }

    private readonly CardCatalog catalogForTrace;

    internal void EnterMain()
    {
        Execute(new ResolveDrawStep(Engine.State.ActivePlayer));
        Execute(new ChangePhase(Engine.State.ActivePlayer, DuelPhase.Main1));
    }

    internal void Populate()
    {
        EnterMain();
        for (int i = 0; i < 4; i++)
        {
            var player = Engine.State.ActivePlayer;
            uint id = player == PlayerId.One ? FirstMonster : SecondMonster;
            int instance = Engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == id).InstanceId;
            Execute(i < 2 ? new NormalSummon(player, instance, 0) : new SetMonster(player, instance, 1));
            if (i == 1)
                Execute(new SetSpellTrap(player, Engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == Preparation).InstanceId, 1));
            Execute(new ChangePhase(player, DuelPhase.End));
            while (Engine.State.GetPlayer(player).Hand.Count > 6)
                Execute(new DiscardForHandLimit(player, Engine.State.GetPlayer(player).Hand[^1].InstanceId));
            Execute(new EndTurn(player));
            EnterMain();
        }
    }

    internal void Cast(uint card = Probe)
    {
        var player = Engine.State.ActivePlayer;
        Execute(new ActivateSpell(player, Engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == card).InstanceId, 0));
    }

    internal void Resolve(int declaration = 1)
    {
        for (int i = 0; i < 30; i++)
        {
            if (Engine.State.Outcome is not null) return;
            DuelCommand? command = Engine.State.PendingDecision is { FieldTargetOptions: { Count: > 0 } options } decision
                ? new ChooseDecisionFieldTarget(decision.Player, decision.DecisionId, options[0].Player,
                    options[0].Location, options[0].Zone, options[0].Card.InstanceId)
                : Engine.State.PendingDecision is { CardOptions.Count: > 0 } cost
                    ? new ChooseDecisionCard(cost.Player, cost.DecisionId, cost.CardOptions[0].InstanceId)
                : Engine.State.PendingChain is not { } chain ? null
                : chain.Declaration is { } declared ? new DeclareValue(declared.Player, declared.DecisionId, declaration)
                : chain.DefinitionCardChoice is { } pick ? new ChooseDefinitionCard(pick.Player, pick.DecisionId, pick.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(Engine.State.ActivePlayer, chain.DecisionId);
            if (command is null) return;
            Execute(command);
        }
        Assert.Fail("Count scenario did not finish its decisions/chain.");
    }
}
