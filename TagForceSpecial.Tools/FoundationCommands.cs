using System.Text.Json;
using System.Text.Json.Serialization;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.Import.Installed;

namespace TagForceSpecial.Tools;

public static class FoundationCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    public static int Run(string command, string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (command == "source-check" && args.Length == 1)
            {
                var result = GameSourceValidator.Open(args[0]); using var source = result.Source;
                Print(new { result.IsValid, buildId = source?.Profile.BuildId, sourceSha256 = source?.Profile.ImageSha256,
                    profileFingerprint = source?.Profile.Fingerprint, result.Diagnostics });
                return result.IsValid ? 0 : 1;
            }
            if (command == "deck-check" && args.Length == 3)
            {
                // args[0] holds a pinned catalog/ package, such as an importer catalog stage output.
                var game = InstalledGame.FromCatalogDirectory(args[0]);
                var result = DeckValidator.Validate(DeckFiles.ReadDeck(args[1]), game.Catalog.Catalog,
                    InitialDeckPolicy.Create(), DeckFiles.ReadCollection(args[2]));
                Print(result); return result.CanPlay ? 0 : 1;
            }
            if (command == "foundation-examples" && args.Length == 1)
            {
                string[] names = ["legal-deck.json", "illegal-deck.json", "unknown-regulation-deck.json", "collection.json"];
                if (names.Any(n => File.Exists(Path.Combine(args[0], n)))) throw new IOException("Choose a new examples directory.");
                Directory.CreateDirectory(args[0]);
                var legal = InitialDeckPolicy.Example();
                DeckFiles.WriteDeck(Path.Combine(args[0], names[0]), legal);
                DeckFiles.WriteDeck(Path.Combine(args[0], names[1]), new("over-copy-limit", "1", legal.OwnerId, legal.CatalogId,
                    legal.CatalogRevision, legal.SourceBuild, legal.PolicyId, legal.PolicyRevision, legal.Main, side: [legal.Main[0]]));
                DeckFiles.WriteDeck(Path.Combine(args[0], names[2]), new("unknown-regulation", "1", legal.OwnerId, legal.CatalogId,
                    legal.CatalogRevision, legal.SourceBuild, "unreviewed-dlc-policy", "1", legal.Main));
                DeckFiles.WriteCollection(Path.Combine(args[0], names[3]), InitialDeckPolicy.ExampleCollection());
                Print(new { created = names }); return 0;
            }
            error.WriteLine("Usage: source-check <iso> | deck-check <catalog-directory> <deck.json> <collection.json>\n" +
                "       foundation-examples <new-directory>");
            return 2;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { error.WriteLine("FoundationInputInvalid: " + ex.Message); return 1; }
        void Print(object value) => output.WriteLine(JsonSerializer.Serialize(value, Json));
    }
}
