using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tools;

/// <summary>Development-only demonstration with invented cards. Does not import or launch the game.</summary>
public static class SyntheticDuelDemo
{
    public static int Run(TextWriter output)
    {
        var ids = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
        var catalog = new CardCatalog(new(null, "synthetic-duel"), ids.Select(id =>
            new CardDefinition(id, $"Synthetic Monster {id:00}", "Invented demonstration card.",
                new(1000, 1000, 4))));
        var rules = ids.ToDictionary(id => id, _ => new DuelCardRules(CardCategory.Monster, true, AllowsBasicBattle: true));
        var duel = new DuelEngine(catalog, rules, ids, ids);
        output.WriteLine("Synthetic duel demo — invented cards, fixed deck order.");
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
                    BattleDamageDealt e => $"Player {e.Player}: took {e.Damage} battle damage; LP {e.LifePointsBefore} -> {e.LifePointsAfter}.",
                    MonsterDestroyed e => $"Player {e.Player}: {catalog[e.Card.GameCardId].Name} destroyed in zone {e.Zone} and sent to the graveyard.",
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
        Execute(new DeclareAttack(PlayerId.Two, 2, 0));
        if (duel.State.Players.Any(p => p.MonsterZones.Any(m => m is not null) || p.Graveyard.Count != 1))
            throw new InvalidOperationException("Equal-ATK battle did not destroy both monsters.");
        Execute(new ChangePhase(PlayerId.Two, DuelPhase.Main2));
        Execute(new ChangePhase(PlayerId.Two, DuelPhase.End));
        Execute(new EndTurn(PlayerId.Two));
        Execute(new ResolveDrawStep(PlayerId.One));
        Execute(new ChangePhase(PlayerId.One, DuelPhase.Main1));
        Execute(new NormalSummon(PlayerId.One, duel.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 1));
        Execute(new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Execute(new DeclareAttack(PlayerId.One, 1));
        if (duel.State.GetPlayer(PlayerId.Two).LifePoints != 7000)
            throw new InvalidOperationException("Direct attack did not deal 1000 damage.");
        output.WriteLine("Demo complete. This example exercises effect-free battles with authored cards.");
        return 0;
    }
}
