using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tools;

public sealed record PreviewDuelReport(string Scope, DuelOutcome Outcome, int Turns, int Commands,
    int SpellResolutions, int Battles, int DestroyedMonsters, int[] FinalLifePoints, string[] CommandTrace);

/// <summary>Bounded scripted engine acceptance, not PC playability or reconstructed opponent AI.</summary>
public static class PreviewDuelScenario
{
    public static PreviewDuelReport Run(CardCatalog catalog)
    {
        uint[] Ordered(params uint[] opening)
        {
            var cards = PlayablePreview.ExampleDeck.ToList();
            foreach (uint id in opening)
                if (!cards.Remove(id)) throw new InvalidOperationException("Opening exceeds the example deck.");
            return [.. opening, .. cards];
        }
        var engine = PlayablePreview.CreateEngine(catalog,
            Ordered(6960, 4352, 4349, 4348, 4084), Ordered(4030, 4033, 4071, 4345, 4350));
        List<string> trace = [];
        int spells = 0, battles = 0, destroyed = 0;
        void Do(DuelCommand command)
        {
            var result = engine.Execute(command);
            if (!result.Succeeded) throw new InvalidOperationException($"Scenario rejected {command}: {result.Error}");
            trace.Add(JsonSerializer.Serialize(command, command.GetType()));
            spells += result.Events.OfType<ChainLinkResolved>().Count();
            battles += result.Events.OfType<AttackDeclared>().Count();
            destroyed += result.Events.OfType<MonsterDestroyed>().Count();
        }
        while (engine.State.Outcome is null && engine.State.TurnNumber <= 80)
        {
            var actor = engine.State.ActivePlayer;
            Do(new ResolveDrawStep(actor));
            if (engine.State.Outcome is not null) break;
            Do(new ChangePhase(actor, DuelPhase.Main1));
            foreach (var card in engine.State.GetPlayer(actor).Hand.Where(c => PlayablePreview.Rules[c.GameCardId].Category == CardCategory.Spell).ToArray())
            {
                Do(new ActivateSpell(actor, card.InstanceId, 0));
                while (engine.State.PendingChain is { } chain && engine.State.Outcome is null)
                    Do(chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                        : new ResolveNextChainLink(actor, chain.DecisionId));
                if (engine.State.Outcome is not null) break;
            }
            if (engine.State.Outcome is not null) break;
            var player = engine.State.GetPlayer(actor);
            int zone = Enumerable.Range(0, 5).FirstOrDefault(z => player.MonsterZones[z] is null, -1);
            var monsters = player.Hand.Where(c => PlayablePreview.Rules[c.GameCardId].Category == CardCategory.Monster);
            var monster = actor == PlayerId.One ? monsters.OrderByDescending(c => catalog[c.GameCardId].Statistics.Attack).FirstOrDefault()
                : monsters.OrderBy(c => catalog[c.GameCardId].Statistics.Attack).FirstOrDefault();
            if (zone >= 0 && monster is not null)
                Do(actor == PlayerId.One ? new NormalSummon(actor, monster.InstanceId, zone) : new SetMonster(actor, monster.InstanceId, zone));
            if (actor == PlayerId.One && engine.State.TurnNumber > 1)
            {
                Do(new ChangePhase(actor, DuelPhase.Battle));
                for (int z = 0; z < 5 && engine.State.Outcome is null; z++)
                {
                    if (engine.State.GetPlayer(actor).MonsterZones[z] is not { Position: BattlePosition.FaceUpAttack }) continue;
                    int target = Enumerable.Range(0, 5).FirstOrDefault(i => engine.State.GetPlayer(PlayerId.Two).MonsterZones[i] is not null, -1);
                    Do(new DeclareAttack(actor, z, target < 0 ? null : target));
                }
                if (engine.State.Outcome is not null) break;
                Do(new ChangePhase(actor, DuelPhase.Main2));
            }
            Do(new ChangePhase(actor, DuelPhase.End));
            while (engine.State.GetPlayer(actor).Hand.Count > 6)
                Do(new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
            Do(new EndTurn(actor));
        }
        if (engine.State.Outcome is not { } outcome) throw new InvalidOperationException("Scenario exceeded its turn bound without a result.");
        return new(PlayablePreview.ManifestId, outcome, engine.State.TurnNumber, trace.Count, spells, battles,
            destroyed, engine.State.Players.Select(p => p.LifePoints).ToArray(), trace.ToArray());
    }

    public static int RunCommand(string[] args, TextWriter output, TextWriter error)
    {
        uint min = 0, max = 0;
        if (args.Length != 1 && (args.Length != 3 || !uint.TryParse(args[1], out min) || !uint.TryParse(args[2], out max)))
        {
            error.WriteLine("Usage: preview-duel <package-directory> | <cardinfo.ehp> <minimum-card-id> <maximum-card-id>");
            return 2;
        }
        try
        {
            CardCatalog catalog;
            if (args.Length == 1) catalog = CardCatalogPackage.Load(args[0]).Catalog;
            else
            {
                using var stream = File.OpenRead(args[0]);
                catalog = CardCatalogImporter.Read(stream, min, max);
            }
            output.WriteLine(JsonSerializer.Serialize(Run(catalog), new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
}
