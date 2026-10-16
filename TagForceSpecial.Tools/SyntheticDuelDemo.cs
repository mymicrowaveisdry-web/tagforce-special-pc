using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tools;

/// <summary>Development-only demonstration with invented cards. Does not import or launch the game.</summary>
public static class SyntheticDuelDemo
{
    public static int Run(TextWriter output)
    {
        var ids = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-stage5"), ids.Select(id =>
            new CardDefinition(id, $"Synthetic Monster {id:00}", "Invented demonstration card.",
                new(1000, 1000, 4), new(0, 0, 0, 0, 0, 0, 0),
                new((int)id, id | 100u << 14 | 100u << 23, 4u << 11, 0))));
        var rules = ids.ToDictionary(id => id, _ => new DuelCardRules(CardCategory.Monster, true));
        var duel = new DuelEngine(catalog, rules, ids, ids);
        output.WriteLine("Stage 5 synthetic duel demo — invented cards, fixed deck order.");
        output.WriteLine("Both players: 8000 LP, 5 cards in hand, 35 cards in deck.");

        void Execute(DuelCommand command)
        {
            var result = duel.Execute(command);
            if (!result.Succeeded) throw new InvalidOperationException($"Demo failed: {result.Error}");
            foreach (var item in result.Events)
                output.WriteLine(item switch
                {
                    FirstDrawSkipped e => $"Player {e.Player}: opening draw skipped.",
                    CardDrawn e => $"Player {e.Player}: drew {catalog[e.Card.GameCardId].Name} (instance {e.Card.InstanceId}).",
                    PhaseChanged e => $"Phase: {e.From} -> {e.To}.",
                    MonsterNormalSummoned e => $"Player {e.Player}: normal summoned {catalog[e.Monster.Card.GameCardId].Name} to zone {e.Zone} in {e.Monster.Position}.",
                    TurnStarted e => $"Turn {e.TurnNumber}: Player {e.Player}.",
                    _ => item.GetType().Name
                });
        }

        Execute(new ResolveDrawStep(PlayerId.One));
        Execute(new ChangePhase(PlayerId.One, DuelPhase.Main1));
        Execute(new NormalSummon(PlayerId.One, duel.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        var denied = duel.Execute(new NormalSummon(PlayerId.One, duel.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 1));
        if (denied.Error != DuelError.NormalSummonLimitReached) throw new InvalidOperationException("Summon limit was not enforced.");
        output.WriteLine($"Second normal summon rejected: {denied.Error}.");
        Execute(new ChangePhase(PlayerId.One, DuelPhase.End));
        Execute(new EndTurn(PlayerId.One));
        Execute(new ResolveDrawStep(PlayerId.Two));
        Execute(new ChangePhase(PlayerId.Two, DuelPhase.Main1));
        Execute(new NormalSummon(PlayerId.Two, duel.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 2));
        Execute(new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Execute(new ChangePhase(PlayerId.Two, DuelPhase.Main2));
        Execute(new ChangePhase(PlayerId.Two, DuelPhase.End));
        Execute(new EndTurn(PlayerId.Two));
        Execute(new ResolveDrawStep(PlayerId.One));
        Execute(new ChangePhase(PlayerId.One, DuelPhase.Main1));
        Execute(new NormalSummon(PlayerId.One, duel.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 1));
        output.WriteLine("Demo complete. Battle resolution and executable effects are not implemented.");
        return 0;
    }
}
