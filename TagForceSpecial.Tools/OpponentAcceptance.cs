using System.Text.Json;
using System.Text.Json.Serialization;
using TagForceSpecial.AI;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Import;

namespace TagForceSpecial.Tools;

public static class OpponentAcceptance
{
    private static readonly JsonSerializerOptions Json = new()
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2) { error.WriteLine("Usage: opponent-acceptance <validated-cache> <new-private-output-directory>"); return 2; }
        try
        {
            string destination = Path.GetFullPath(args[1]);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new acceptance directory.");
            var validated = GameCache.Validate(args[0]);
            if (validated.Cache is not { } cache) { error.WriteLine(JsonSerializer.Serialize(validated.Diagnostics, Json)); return 1; }
            Directory.CreateDirectory(destination);
            var rows = new List<object>();
            foreach (uint seed in new uint[] { 0, 1, 7, 42, uint.MaxValue })
                foreach (int variant in new[] { 0, 2 })
                {
                    var session = SupportedDuelFactory.Create(cache, variant, seed);
                    for (int step = 0; step < 6000 && session.View.Outcome is null; step++)
                    {
                        bool moved = session.View.Choices.Count > 0 ? session.Choose(session.View.Revision, 0) : session.StepOpponent();
                        if (!moved) throw new InvalidDataException($"Scenario {seed}/{variant} stalled at revision {session.View.Revision}");
                    }
                    if (session.View.Outcome is null) throw new InvalidDataException("Scenario command budget exhausted without a terminal outcome.");
                    var replay = session.ExportReplay(InitialOpponentDecks.ManifestId);
                    var engine = PlayablePreview.CreateEngine(cache.Catalog.Catalog, replay.FirstDeck, replay.SecondDeck, replay.StagedBattle);
                    var source = new DuelActionSource(engine, cache.Catalog.Catalog);
                    var policy = new DeterministicPolicy(seed);
                    foreach (var entry in replay.Steps)
                    {
                        if (entry.Player == PlayerId.Two && policy.Choose(source.Observe(entry.Player))?.Key != entry.ActionKey)
                            throw new InvalidDataException("Replay differs from the recorded policy/seed.");
                        if (!ReplayCommands.Apply(source, entry).Accepted) throw new InvalidDataException("Replay command failed validation.");
                    }
                    if (source.Observe(PlayerId.One).Outcome != replay.Outcome) throw new InvalidDataException("Replay terminal outcome differs.");
                    string file = $"seed-{seed}-deck-{variant}.json";
                    using (var stream = new FileStream(Path.Combine(destination, file), FileMode.CreateNew, FileAccess.Write))
                        JsonSerializer.Serialize(stream, replay, Json);
                    rows.Add(new { seed, variant, file, commands = replay.Steps.Count, digest = replay.CommandDigest(), replay.Outcome, replayVerified = true });
                }
            var summary = new { schemaVersion = 1, sourceSha256 = cache.SourceSha256, policy = DeterministicPolicy.Version,
                manifest = InitialOpponentDecks.ManifestId, stepBudget = 6000, scenarios = rows };
            using (var stream = new FileStream(Path.Combine(destination, "summary.json"), FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, summary, Json);
            output.WriteLine($"Opponent acceptance passed: {rows.Count} terminal scenarios and policy-verified replays.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            error.WriteLine(exception.Message); return 1;
        }
    }
}
