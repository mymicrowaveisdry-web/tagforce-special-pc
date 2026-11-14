using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tools;

public sealed record PoolDuelFailure(ulong Seed, string Kind, long Revision, string Detail, IReadOnlyList<string> LastActions);

/// <summary>Activated cards went on a chain; played cards reached the field.</summary>
public sealed record PoolDuelReport(int Duels, int Finished, long Commands, int PoolCards, int ActivatedCards, int PlayedCards,
    IReadOnlyList<uint> NeverPlayed, IReadOnlyList<PoolDuelFailure> Failures)
{
    public IReadOnlyList<uint> Activated { get; init; } = [];
    public IReadOnlyList<uint> Played { get; init; } = [];
    /// <summary>Cards with a registered effect that never reached a chain in these duels.</summary>
    public IReadOnlyList<uint> NeverActivated { get; init; } = [];
}

/// <summary>
/// Plays seeded duels over the whole supported pool, checking engine invariants after every command and replaying each
/// duel to prove it is deterministic. It finds crashes and corrupt states, not wrong card behavior.
/// </summary>
public static class PoolDuels
{
    private const int FillerCount = 16, MainDeckSize = 40, ExtraDeckSize = 10, HistorySize = 12;

    private sealed class Coverage
    {
        public HashSet<uint> Activated { get; } = [];
        public HashSet<uint> Played { get; } = [];
    }

    private sealed record DuelResult(bool Finished, long Commands, string Digest, PoolDuelFailure? Failure);

    public static PoolDuelReport Run(CardCatalog catalog, int duels, ulong seed, int maxSteps = 20000)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duels);
        var known = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        var rules = CardPool.Rules.Where(e => known.Contains(e.Key) && !e.Value.IsToken).ToArray();
        var normals = NormalMonsterFamily.CardIds.ToHashSet();
        uint[] filler = rules.Where(e => normals.Contains(e.Key) && e.Value.AllowsNormalSummon && catalog[e.Key].Statistics.Level is >= 1 and <= 4)
            .Select(e => e.Key).Order().ToArray();
        uint[] effects = Shuffled(rules.Where(e => !normals.Contains(e.Key) && !e.Value.AllowsExtraDeck).Select(e => e.Key).Order().ToArray(), seed);
        uint[] extras = Shuffled(rules.Where(e => e.Value.AllowsExtraDeck).Select(e => e.Key).Order().ToArray(), seed ^ 0x5555);
        if (filler.Length < FillerCount || effects.Length == 0) throw new ArgumentException("The catalog does not hold enough supported cards.", nameof(catalog));

        var coverage = new Coverage();
        var failures = new List<PoolDuelFailure>();
        int finished = 0, effectCursor = 0, extraCursor = 0;
        long commands = 0;
        for (int i = 0; i < duels; i++)
        {
            ulong duelSeed = Mix(seed, (ulong)i);
            uint[] Main(ulong salt)
            {
                var deck = Shuffled(filler, duelSeed ^ salt).Take(FillerCount).ToList();
                for (int n = 0; n < MainDeckSize - FillerCount; n++) deck.Add(effects[effectCursor++ % effects.Length]);
                return Shuffled(deck.ToArray(), duelSeed ^ salt ^ 0xABCD);
            }
            uint[] Extra()
            {
                if (extras.Length == 0) return [];
                return Enumerable.Range(0, Math.Min(ExtraDeckSize, extras.Length)).Select(_ => extras[extraCursor++ % extras.Length]).ToArray();
            }
            uint[] firstMain = Main(1), secondMain = Main(2), firstExtra = Extra(), secondExtra = Extra();
            var result = Play(catalog, firstMain, secondMain, firstExtra, secondExtra, duelSeed, maxSteps, coverage);
            commands += result.Commands;
            if (result.Failure is not null) { failures.Add(result.Failure); continue; }
            var replay = Play(catalog, firstMain, secondMain, firstExtra, secondExtra, duelSeed, maxSteps, new Coverage());
            if (replay.Digest != result.Digest)
            {
                failures.Add(new(duelSeed, "NotDeterministic", 0, "Replaying the same seed produced a different duel.", []));
                continue;
            }
            if (result.Finished) finished++;
        }
        uint[] pool = [.. effects.Order(), .. extras.Order()];
        var activatable = CardFamilies.All.SelectMany(f => f.Definitions.Select(d => d.CardId).Concat(f.Triggers.Select(t => t.CardId)).Concat(f.SummonEffects.Select(s => s.SpellCardId)))
            .Concat(PlayablePreview.Definitions.Select(d => d.CardId)).ToHashSet();
        return new(duels, finished, commands, pool.Length, pool.Count(coverage.Activated.Contains), pool.Count(coverage.Played.Contains),
            Array.AsReadOnly(pool.Where(id => !coverage.Played.Contains(id) && !coverage.Activated.Contains(id)).ToArray()), failures.AsReadOnly())
        {
            Activated = Array.AsReadOnly(pool.Where(coverage.Activated.Contains).ToArray()),
            Played = Array.AsReadOnly(pool.Where(coverage.Played.Contains).ToArray()),
            NeverActivated = Array.AsReadOnly(pool.Where(id => activatable.Contains(id) && !coverage.Activated.Contains(id)).ToArray())
        };
    }

    private static DuelResult Play(CardCatalog catalog, uint[] first, uint[] second, uint[] firstExtra, uint[] secondExtra, ulong seed, int maxSteps, Coverage coverage)
    {
        var history = new Queue<string>();
        var keys = new StringBuilder();
        long commands = 0, revision = 0;
        PoolDuelFailure Fail(string kind, string detail) => new(seed, kind, revision, detail, history.ToArray());
        try
        {
            var engine = CardPool.CreateEngine(catalog, first, second, firstExtra, secondExtra, seed);
            var source = new DuelActionSource(engine, catalog);
            var initial = Zones(engine.State).ToHashSet();
            for (int step = 0; step < maxSteps; step++)
            {
                var state = engine.State;
                revision = state.Revision;
                if (state.Outcome is not null)
                {
                    keys.Append('|').Append(JsonSerializer.Serialize(state));
                    return new(true, commands, Hash(keys), null);
                }
                DuelObservation one = source.Observe(PlayerId.One), two = source.Observe(PlayerId.Two);
                var view = one.Availability == ActionAvailability.Ready ? one : two.Availability == ActionAvailability.Ready ? two : null;
                if (view is null) return new(false, commands, "", Fail("Stalled", one.Diagnostic.Length > 0 ? one.Diagnostic : two.Diagnostic));
                var action = Choose(view, seed);
                history.Enqueue($"P{(int)view.Player + 1}: {action.Label}");
                if (history.Count > HistorySize) history.Dequeue();
                keys.Append(action.Key).Append(';');
                var result = source.Execute(view.Player, view.Revision, action.Token);
                if (!result.Accepted) return new(false, commands, "", Fail("LegalActionRejected", $"{action.Label}: {result.Error}"));
                commands++;
                revision = engine.State.Revision;
                if (Violation(engine.State, initial, catalog) is { } violation) return new(false, commands, "", Fail("Invariant", violation));
                Record(engine.State, coverage);
            }
            return new(false, commands, "", Fail("StepBudget", $"No outcome after {maxSteps} commands."));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string frame = exception.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
            return new(false, commands, "", Fail("Exception", $"{exception.GetType().Name}: {exception.Message} {frame}"));
        }
    }

    private static readonly HashSet<ActionKind> Interesting =
    [
        ActionKind.ActivateSpell, ActionKind.ActivateSetSpell, ActionKind.MonsterEffect, ActionKind.CardEffect, ActionKind.Unequip, ActionKind.ChainResponse, ActionKind.BattleResponse,
        ActionKind.Summon, ActionKind.SetMonster, ActionKind.SetSpellTrap, ActionKind.Attack, ActionKind.ChooseCard, ActionKind.ChooseZone, ActionKind.ChooseTarget,
        ActionKind.ChoosePosition, ActionKind.OrderTriggers, ActionKind.DeclineTriggers, ActionKind.Upkeep, ActionKind.Discard, ActionKind.Draw
    ];

    /// <summary>Mostly plays cards, sometimes takes any legal action so passes and phase changes are also explored.</summary>
    private static LegalAction Choose(DuelObservation view, ulong seed)
    {
        ulong roll = Mix(seed, (ulong)view.Revision ^ (ulong)view.Player << 56);
        var interesting = view.Actions.Where(a => Interesting.Contains(a.Kind)).ToArray();
        IReadOnlyList<LegalAction> pool = roll % 100 < 85 && interesting.Length != 0 ? interesting : view.Actions;
        return pool[(int)((roll >> 16) % (ulong)pool.Count)];
    }

    private static IEnumerable<int> Zones(DuelState state)
    {
        foreach (var player in state.Players)
        {
            foreach (var card in player.Deck.Concat(player.Hand).Concat(player.Graveyard).Concat(player.Banished).Concat(player.ExtraDeck).Concat(player.FaceUpExtraDeck))
                yield return card.InstanceId;
            foreach (var monster in player.MonsterZones.OfType<FieldMonster>())
            {
                yield return monster.Card.InstanceId;
                foreach (var material in monster.XyzMaterials) yield return material.InstanceId;
            }
            foreach (var card in player.SpellTrapFields.Concat(player.PendulumZones).Append(player.FieldSpell).OfType<FieldSpellTrap>())
                yield return card.Card.InstanceId;
        }
    }

    /// <summary>
    /// Cards held by a pending Summon; the monster of a Gemini or Flip Summon stays in its zone, so it is not counted again. A Pendulum
    /// Summon's group already holds its first card, which the summon also names, so only the group is counted.
    /// </summary>
    public static IEnumerable<int> PendingCards(PendingSummon summon) => summon.PendulumGroup.Count != 0
        ? summon.PendulumGroup.SelectMany(PendingCards)
        : summon.XyzMaterials.Select(c => c.InstanceId).Concat(summon.IsGeminiSummon || summon.IsFlipSummon ? [] : [summon.Card.InstanceId]);

    private static string? Violation(DuelState state, HashSet<int> initial, CardCatalog catalog)
    {
        var present = Zones(state).Concat(state.PendingSummon is { } summon ? PendingCards(summon) : []).ToArray();
        if (present.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1) is { } duplicate) return $"Card instance {duplicate.Key} is in two places.";
        var seen = present.ToHashSet();
        if (initial.FirstOrDefault(id => !seen.Contains(id)) is var missing and not 0) return $"Card instance {missing} left every zone.";
        foreach (var player in state.Players)
        {
            if (player.LifePoints <= 0 && state.Outcome is null) return $"{player.Id} has {player.LifePoints} LP with no outcome.";
            if (player.MonsterZones.Count != 5 || player.SpellTrapFields.Count != 5) return $"{player.Id} has the wrong number of zones.";
            if (state.PendingChain is null)
                foreach (var field in player.SpellTrapFields.Concat([player.FieldSpell]).OfType<FieldSpellTrap>())
                    if (field.EquippedTo is { } equipped && !equipped.IsStillPresent(state)) return $"{catalog[field.Card.GameCardId].Name} is equipped to a monster that left.";
        }
        return null;
    }

    private static void Record(DuelState state, Coverage coverage)
    {
        foreach (var link in state.PendingChain?.Links ?? []) coverage.Activated.Add(link.Activation.Spell.GameCardId);
        foreach (var player in state.Players)
        {
            foreach (var monster in player.MonsterZones.OfType<FieldMonster>()) coverage.Played.Add(monster.Card.GameCardId);
            foreach (var card in player.SpellTrapFields.Concat(player.PendulumZones).Append(player.FieldSpell).OfType<FieldSpellTrap>()) coverage.Played.Add(card.Card.GameCardId);
        }
    }

    private static ulong Mix(ulong seed, ulong value)
    {
        ulong z = unchecked(seed + value * 0x9E3779B97F4A7C15UL + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ z >> 30) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ z >> 27) * 0x94D049BB133111EBUL);
        return z ^ z >> 31;
    }

    private static uint[] Shuffled(uint[] input, ulong seed)
    {
        var output = input.ToArray();
        for (int i = output.Length - 1; i > 0; i--)
        {
            int j = (int)(Mix(seed, (ulong)i) % (ulong)(i + 1));
            (output[i], output[j]) = (output[j], output[i]);
        }
        return output;
    }

    private static string Hash(StringBuilder text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

    public static int RunCommand(string[] args, TextWriter output, TextWriter error)
    {
        int duels = 50;
        ulong seed = 1;
        if (args.Length is < 1 or > 4 || args.Length > 1 && !int.TryParse(args[1], out duels) || args.Length > 2 && !ulong.TryParse(args[2], out seed) || duels <= 0)
        {
            error.WriteLine("Usage: pool-duel <package-directory> [duels] [seed] [report.json]");
            return 2;
        }
        try
        {
            var report = Run(CardCatalogPackage.Load(args[0]).Catalog, duels, seed);
            if (args.Length == 4)
                File.WriteAllText(args[3], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            output.WriteLine($"{report.Duels} duels, {report.Finished} finished, {report.Commands} commands; {report.ActivatedCards} activated and {report.PlayedCards} played of {report.PoolCards} pool cards; {report.NeverActivated.Count} registered effects never activated; {report.Failures.Count} failures.");
            foreach (var group in report.Failures.GroupBy(f => (f.Kind, f.Detail)).OrderByDescending(g => g.Count()).Take(20))
                output.WriteLine($"  {group.Count(),4} {group.Key.Kind}: {group.Key.Detail}");
            return report.Failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }
}
