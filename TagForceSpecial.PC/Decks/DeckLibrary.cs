using TagForceSpecial.Core.Decks;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Decks;

public sealed record SavedDeck(string Key, string Name, DeckDefinition? Deck, string? Error = null);

public sealed class DeckLibrary(PrivateStorage storage)
{
    public string Save(DeckDefinition deck, string? previousKey = null)
    {
        if (previousKey is not null && !Preferences.ValidDeckKey(previousKey)) throw new InvalidDataException("Invalid saved-deck key.");
        string group = previousKey?.Split('/')[0] ?? Guid.NewGuid().ToString("N");
        string key = $"{group}/{Guid.NewGuid():N}.json";
        string destination = storage.PathFor("decks/" + key);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".pending";
        try
        {
            DeckFiles.WriteDeck(temporary, deck);
            _ = DeckFiles.ReadDeck(temporary);
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return key;
    }
    public DeckDefinition Load(string key)
    {
        if (!Preferences.ValidDeckKey(key)) throw new InvalidDataException("Invalid saved-deck key.");
        return DeckFiles.ReadDeck(storage.PathFor("decks/" + key));
    }
    public IReadOnlyList<SavedDeck> List()
    {
        string root = storage.PathFor("decks");
        if (!Directory.Exists(root)) return Array.Empty<SavedDeck>();
        var results = new List<SavedDeck>();
        foreach (var group in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal).Take(100))
        {
            PrivateStorage.Check(group);
            if (!Guid.TryParseExact(Path.GetFileName(group), "N", out _)) continue;
            foreach (var path in Directory.EnumerateFiles(group, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
            {
                string key = Path.GetFileName(group) + "/" + Path.GetFileName(path);
                try { var deck = Load(key); results.Add(new(key, deck.DeckId, deck)); }
                catch (Exception e) when (SetupStore.Recoverable(e)) { results.Add(new(key, "Unreadable revision", null, "This revision could not be read. Select an earlier revision.")); }
            }
        }
        return results.AsReadOnly();
    }
}
