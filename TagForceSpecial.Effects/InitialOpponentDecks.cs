using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.Effects;

/// <summary>Versioned, authored deck orders for the first opponent. No inferred original character deck/AI.</summary>
public static class InitialOpponentDecks
{
    public const string ManifestId = "tfsp-initial-opponent-decks-v1";
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
        { "INITIAL PREVIEW", "INITIAL PREVIEW / REVERSE ORDER", "INITIAL PREVIEW / ATTACK FIRST" });
    private static readonly uint[] AttackOrder = [6960, 5644, 4032, 4084, 4012, 4033, 4071, 4030, 4352, 4350, 4349, 4348, 4345, 4840];

    public static DeckDefinition Create(int variant, string ownerId = "local-player")
    {
        if (variant < 0 || variant >= Names.Count) throw new ArgumentOutOfRangeException(nameof(variant));
        var source = InitialDeckPolicy.Example(ownerId);
        var ids = variant switch
        {
            1 => source.Main.Reverse(),
            2 => source.Main.OrderBy(id => Array.IndexOf(AttackOrder, id)),
            _ => source.Main.AsEnumerable()
        };
        return new($"initial-opponent-deck-{variant}", "1", source.OwnerId, source.CatalogId, source.CatalogRevision,
            source.SourceBuild, source.PolicyId, source.PolicyRevision, ids);
    }
}
