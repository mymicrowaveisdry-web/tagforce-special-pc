using TagForceSpecial.Cards;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>The original's 11 sort keys, in the Sort popup's row order.</summary>
public enum DeckEditSortKey { Name, CardFrame, LevelRank, Scale, Attack, Defense, Type, Attribute, Icon, ForbiddenLimited, Copies }

/// <summary>Label Display Settings rows: Show All Cards in Trunk, Hide Labeled Cards, Only Show Labeled Cards.</summary>
public enum DeckEditLabelDisplay { All, HideLabeled, OnlyLabeled }

/// <summary>Narrow Search panels that filter, in the Narrow Search popup's row order (Key Search is separate: no genre data on the PC).</summary>
public enum DeckEditNarrowPanel { CardFrame, Type, Attribute, LevelRank, Scale, SpellTrapIcon, ForbiddenLimited }

/// <summary>Which Trunk entries the list shows and in what order: sort key and order, Narrow Search selections, the related-card filter and
/// the label display. Observed: picking a sort key re-sorts the list, Run narrows it, Search for Related Cards filters at once, labels hide or
/// isolate labeled cards. Within a panel any selected cell matches; selected panels must all match (inferred). The PC has no banlist, so every
/// card is "No Limit" (PC rule). Cards without numeric codes match no panel that needs them.</summary>
public sealed class DeckEditQuery
{
    /// <summary>Level/Rank grid: 3 ranges (1~4, 5~6, 7~), Levels 1-12, then Ranks 1-11.</summary>
    public const int LevelRanges = 3, Levels = 12, Ranks = 11;
    private static readonly (uint Kind, uint Icon)[] SpellTrapCells = [(13, 2), (13, 3), (13, 4), (13, 5), (13, 6), (13, 0), (14, 4), (14, 1), (14, 0)];
    private readonly Func<CardDefinition, int?> frameCell;
    private readonly Dictionary<DeckEditNarrowPanel, HashSet<int>> narrow = Enum.GetValues<DeckEditNarrowPanel>().ToDictionary(p => p, _ => new HashSet<int>());

    /// <summary>Card Frame grid cells in order (Normal, Effect, Ritual, Pendulum, Pendulum/Effect, Fusion, Synchro, Dark Synchro, Xyz, Spell,
    /// Trap), by the preview frame each one's capture drew (phase62_frame_previews).</summary>
    private static readonly string[] FrameKeys = ["deck.all.frame.p00", "deck.all.frame.p01", "deck.all.frame.p02", "deck.all.frame-p.p00",
        "deck.all.frame-p.p01", "deck.all.frame.p03", "deck.all.frame.p10", "deck.all.frame.p11", "deck.all.frame-xyz", "deck.all.frame.p04", "deck.all.frame.p05"];

    public static int? FrameCell(string frameKey) => Array.IndexOf(FrameKeys, frameKey) is int i and >= 0 ? i : null;

    /// <param name="frameCell">Card Frame grid cell of a card (0 Normal ... 10 Trap) from its preview frame, or null when unknown.</param>
    public DeckEditQuery(Func<CardDefinition, int?> frameCell) => this.frameCell = frameCell;

    /// <summary>Null keeps the PC's catalog order (no sort picked yet).</summary>
    public DeckEditSortKey? SortKey { get; set; }
    public bool Inverse { get; set; }
    public DeckEditLabelDisplay LabelDisplay { get; set; }
    public HashSet<uint> Labeled { get; } = [];
    /// <summary>Card shown by Search for Related Cards, or null.</summary>
    public CardDefinition? RelatedTo { get; set; }
    /// <summary>Applied Narrow Search conditions (set by Run).</summary>
    public IReadOnlyDictionary<DeckEditNarrowPanel, HashSet<int>> Narrow => narrow;
    public bool Narrowed => narrow.Values.Any(s => s.Count > 0) || RelatedTo is not null;

    /// <summary>Everything the list order and filter depend on, for the frame cache.</summary>
    public string Signature => $"{SortKey}/{Inverse}/{LabelDisplay}/{RelatedTo?.GameCardId}/" + string.Join(',', Labeled.Order()) + "/" +
        string.Join(',', narrow.Select(p => string.Join('.', p.Value.Order())));

    public void ApplyNarrow(IReadOnlyDictionary<DeckEditNarrowPanel, HashSet<int>> selections)
    {
        foreach (var (panel, cells) in selections) { narrow[panel].Clear(); narrow[panel].UnionWith(cells); }
    }

    /// <summary>Reset Narrow Search Results: clears Narrow Search and the related-card filter; sort and label display stay.</summary>
    public void ResetNarrow()
    {
        foreach (var cells in narrow.Values) cells.Clear();
        RelatedTo = null;
    }

    public bool Matches(CardDefinition card, IReadOnlyDictionary<DeckEditNarrowPanel, HashSet<int>>? conditions = null)
    {
        foreach (var (panel, cells) in conditions ?? narrow)
            if (cells.Count > 0 && !cells.Any(cell => Matches(card, panel, cell))) return false;
        if (RelatedTo is { } related && conditions is null && !IsRelated(card, related)) return false;
        return true;
    }

    public bool Shows(CardDefinition card) => Matches(card) && LabelDisplay switch
    {
        DeckEditLabelDisplay.HideLabeled => !Labeled.Contains(card.GameCardId),
        DeckEditLabelDisplay.OnlyLabeled => Labeled.Contains(card.GameCardId),
        _ => true
    };

    public IReadOnlyList<DeckEditEntry> Apply(IEnumerable<DeckEditEntry> trunk) => Sort(trunk.Where(e => Shows(e.Card))).ToArray();

    public IEnumerable<DeckEditEntry> Sort(IEnumerable<DeckEditEntry> entries)
    {
        if (SortKey is not { } key) return entries;
        var ordered = Inverse ? entries.OrderByDescending(e => Value(e, key)) : entries.OrderBy(e => Value(e, key));
        return key == DeckEditSortKey.Name ? ordered.ThenBy(e => e.Card.GameCardId)
            : ordered.ThenBy(e => e.Card.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Card.GameCardId);
    }

    private IComparable Value(DeckEditEntry entry, DeckEditSortKey key)
    {
        var card = entry.Card;
        var codes = card.Codes;
        bool monster = codes?.RawKind is not (13 or 14);
        return key switch
        {
            DeckEditSortKey.Name => card.Name.ToUpperInvariant(),
            DeckEditSortKey.CardFrame => frameCell(card) ?? int.MaxValue,
            DeckEditSortKey.LevelRank => monster ? (int)card.Statistics.Level : -1,
            DeckEditSortKey.Scale => IsPendulum(card) ? (int)(codes?.Field23To26 ?? 0) : -1,
            DeckEditSortKey.Attack => monster ? card.Statistics.Attack ?? -1 : -2,
            DeckEditSortKey.Defense => monster ? card.Statistics.Defense ?? -1 : -2,
            DeckEditSortKey.Type => monster ? (int)(codes?.Type ?? 0) : int.MaxValue,
            DeckEditSortKey.Attribute => monster ? (int)(codes?.Attribute ?? 0) : int.MaxValue,
            DeckEditSortKey.Icon => monster ? -1 : (int)(codes?.Icon ?? 0),
            DeckEditSortKey.ForbiddenLimited => 3,
            _ => entry.Owned
        };
    }

    private bool IsPendulum(CardDefinition card) => frameCell(card) is 3 or 4;
    private static bool IsXyz(CardDefinition card) => card.Codes?.RawKind is 22 or 23;
    private static bool IsMonster(CardDefinition card) => card.Codes is { RawKind: not (13 or 14) };

    public bool Matches(CardDefinition card, DeckEditNarrowPanel panel, int cell)
    {
        var codes = card.Codes;
        int level = (int)card.Statistics.Level;
        return panel switch
        {
            DeckEditNarrowPanel.CardFrame => frameCell(card) == cell,
            DeckEditNarrowPanel.Type => IsMonster(card) && codes!.Type == cell + 1,
            DeckEditNarrowPanel.Attribute => IsMonster(card) && codes!.Attribute == cell + 1,
            DeckEditNarrowPanel.LevelRank => IsMonster(card) && (cell switch
            {
                0 => !IsXyz(card) && level is >= 1 and <= 4,
                1 => !IsXyz(card) && level is 5 or 6,
                2 => !IsXyz(card) && level >= 7,
                < LevelRanges + Levels => !IsXyz(card) && level == cell - LevelRanges + 1,
                _ => IsXyz(card) && level == cell - LevelRanges - Levels + 1
            }),
            DeckEditNarrowPanel.Scale => IsPendulum(card) && (codes!.Field23To26 == cell + 1 || codes.Field27To30 == cell + 1),
            DeckEditNarrowPanel.SpellTrapIcon => cell < SpellTrapCells.Length && codes is { } c && c.RawKind == SpellTrapCells[cell].Kind && c.Icon == SpellTrapCells[cell].Icon,
            DeckEditNarrowPanel.ForbiddenLimited => cell == 3,
            _ => false
        };
    }

    /// <summary>PC rule (the original's related-card data is unmeasured): the same card, cards whose name contains its name before any
    /// " - " subtitle, and cards whose text quotes its full name.</summary>
    public static bool IsRelated(CardDefinition card, CardDefinition to)
    {
        if (card.GameCardId == to.GameCardId) return true;
        string stem = to.Name.Split(" - ", 2)[0];
        return card.Name.Contains(stem, StringComparison.OrdinalIgnoreCase) ||
            card.Description.Contains('"' + to.Name + '"', StringComparison.OrdinalIgnoreCase) ||
            to.Description.Contains('"' + card.Name + '"', StringComparison.OrdinalIgnoreCase);
    }
}
