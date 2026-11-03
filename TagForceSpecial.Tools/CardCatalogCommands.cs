using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;

namespace TagForceSpecial.Tools;

public static class CardCatalogCommands
{
    public static int Run(string command, string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (command == "catalog-import" && args.Length is 4 or 7 && uint.TryParse(args[1], out var min) && uint.TryParse(args[2], out var max))
            {
                using var source = File.OpenRead(args[0]);
                var catalog = CardCatalogImporter.Read(source, min, max);
                if (args.Length == 4 && catalog.Source.ContainerSha256 != TfspCardMetadata.CardInfoSha256)
                    throw new InvalidDataException("Supply catalog ID, revision and locale for an unreviewed source build.");
                var identity = new CardCatalogPackageInfo(args.Length == 7 ? args[4] : "tfsp-original",
                    args.Length == 7 ? args[5] : "1", args.Length == 7 ? args[6] : "en",
                    $"cardinfo-sha256:{catalog.Source.ContainerSha256}");
                var manifest = CardCatalogPackage.Write(args[3], catalog, identity);
                output.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            }
            if (command == "catalog-check" && args.Length == 1)
            {
                var package = CardCatalogPackage.Load(args[0]);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    package.Manifest.Identity, package.Manifest.CardsSha256, package.Catalog.Count,
                    unknownEffectCards = package.Catalog.Cards.Count(c => c.Effects!.State == CardEffectState.Unknown),
                    effectFreeCards = package.Catalog.Cards.Count(c => c.Effects!.State == CardEffectState.None),
                    referencedEffectCards = package.Catalog.Cards.Count(c => c.Effects!.State == CardEffectState.Referenced)
                }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            }
            error.WriteLine("Usage: catalog-import <cardinfo.ehp> <minimum-id> <maximum-id> <new-package-directory> [catalog-id revision locale]\n" +
                "       catalog-check <package-directory>");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }
}
