using Microsoft.Xna.Framework.Input;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;

namespace TagForceSpecial.PC.Game;

/// <summary>The matched Normal Summon walk: the same public opening as the native research duel (the player goes first with 42 Normal
/// Monsters, 14 names × 3, no Extra Deck, against a hidden 42-card deck) and the same presses (Down to the middle hand card, ○ for its menu,
/// ○ on Summon). Native opening hand, left to right: Baby Dragon, Winged Dragon, Guardian of the Fortress #1, Koumori Dragon, Hitotsu-Me Giant,
/// The Snake Hair; Koumori Dragon is summoned.</summary>
public static class DuelWalkScenario
{
    public const uint Summoned = 4037;
    /// <summary>The native deck: Battle Ox, Hitotsu-Me Giant, Baby Dragon, Ryu-Kishin, Feral Imp, Winged Dragon #1, Blackland Fire Dragon,
    /// Tyhone, Horn Imp, Beaver Warrior, Koumori Dragon, Two-Headed King Rex, The Snake Hair, Rogue Doll.</summary>
    public static IReadOnlyList<uint> Names { get; } = [4032, 4009, 4010, 4011, 4012, 4013, 4016, 4019, 4031, 4033, 4037, 4038, 4042, 4050];
    public static IReadOnlyList<uint> OpeningHand { get; } = [4010, 4013, 4037, 4009, 4042];

    /// <summary>Frame (after the walk starts) and key of each press. The first press waits for Main Phase 1 to settle.</summary>
    public static IReadOnlyList<(int Frame, Keys Key)> Presses { get; } = [(420, Keys.Down), (450, Keys.Enter), (480, Keys.Enter)];
    public const int Frames = 600;

    /// <summary>The player's deck: the native opening hand first (the PC deals the first five cards), then the rest of the 42.</summary>
    public static DeckDefinition PlayerDeck() => Deck("matched-walk-player", "local-player", OpeningHand.Concat(Remaining()));

    /// <summary>The opponent's hidden deck: the same 42 cards (its contents are never shown).</summary>
    public static DeckDefinition OpponentDeck() => Deck("matched-walk-opponent", "local-opponent", Names.SelectMany(id => Enumerable.Repeat(id, 3)));

    private static IEnumerable<uint> Remaining()
    {
        var rest = Names.SelectMany(id => Enumerable.Repeat(id, 3)).ToList();
        foreach (var id in OpeningHand) rest.Remove(id);
        return rest;
    }

    private static DeckDefinition Deck(string id, string owner, IEnumerable<uint> main)
    {
        var source = ConfirmedCardPool.AdoptPreview(InitialOpponentDecks.Create(0, owner));
        return new(id, "1", source.OwnerId, source.CatalogId, source.CatalogRevision, source.SourceBuild, source.PolicyId, source.PolicyRevision,
            main);
    }
}
