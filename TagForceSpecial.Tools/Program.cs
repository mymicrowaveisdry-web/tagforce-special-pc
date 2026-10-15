using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Assets;
using TagForceSpecial.Tools;

uint minimum = 0, maximum = 0;
bool valid = args.Length == 2 && args[0] is "ehp-list" or "card-images-list" ||
    args.Length == 4 && (args[0] == "cardinfo-list" || args[0] == "card-details" || args[0] == "card-catalog") &&
    uint.TryParse(args[2], out minimum) && uint.TryParse(args[3], out maximum);
if (!valid)
{
    Console.Error.WriteLine("Usage: TagForceSpecial.Tools ehp-list|card-images-list <file>\n" +
        "       TagForceSpecial.Tools cardinfo-list|card-details|card-catalog <cardinfo.ehp> <minimum-card-id> <maximum-card-id>\n" +
        "Read-only JSON output; cardinfo ID range must come from an inspected image index.");
    return 2;
}
try
{
    using var source = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read);
    string before = Convert.ToHexStringLower(SHA256.HashData(source));
    source.Position = 0;
    object index = args[0] switch
    {
        "ehp-list" => new EhpReader().ReadIndex(source),
        "card-images-list" => new CardImageIndexReader().ReadIndex(source),
        "cardinfo-list" => new CardInfoReader().Read(source, minimum, maximum),
        "card-details" => new CardDetailReader().Read(source, minimum, maximum),
        "card-catalog" => CardCatalogImporter.Read(source, minimum, maximum),
        _ => throw new ArgumentException("Unknown inspection command.")
    };
    source.Position = 0;
    string after = Convert.ToHexStringLower(SHA256.HashData(source));
    if (before != after) throw new InvalidDataException("Source changed during inspection.");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1, sourceName = Path.GetFileName(args[1]), sourceSha256 = before,
        sourceSha256After = after, actualLength = source.Length, command = args[0], index
    }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    return 0;
}
catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine($"ERROR: {error.Message}");
    return 1;
}
