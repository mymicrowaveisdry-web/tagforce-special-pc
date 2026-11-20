using System.Text.Json;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

public sealed record FreeDuelDateStyle(float[] X, float DY, float ColonX, float ColonDY, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
/// <summary>One measured deck picker window (opponent: Character tab; player: User tab).</summary>
public sealed record FreeDuelDeckSide(FlowDraw[] Window, FlowText TabLabel, FlowDraw[] Panel, FlowDraw[] OtherPanel, FlowDraw CurrentIcon, FlowDraw OtherIcon, FlowDraw[] Cursor,
    FlowDraw[] Scroll, float PenX, float NameY, int NameColor, int NameShadow, FlowDraw[]? SlotDigits = null, FreeDuelDateStyle? Date = null);
public sealed record FreeDuelCharacterDecks(bool Partner, FlowTextSource[] Decks);

/// <summary>The measured deck pickers (free-duel-decks-frame.json: numbers, asset keys and source offsets only), with each roster character's
/// original deck names (EBOOT recipe table; partners list "Partner's Deck" first).</summary>
public sealed record FreeDuelDecksProfile(int SchemaVersion, string BuildId, string Scope, FreeDuelDeckSide Opponent, FreeDuelDeckSide Player,
    float RowPitch, int VisibleRows, float FitWidth, float ScaleStart, float ScaleStep, float TrackTop, float TrackLength, FlowTextSource PartnerDeck,
    FreeDuelCharacterDecks[] CharacterDecks)
{
    public IEnumerable<string> AssetKeys() => new[] { Opponent, Player }
        .SelectMany(s => s.Window.Concat(s.Panel).Concat(s.OtherPanel).Append(s.CurrentIcon).Append(s.OtherIcon).Concat(s.Cursor).Concat(s.Scroll).Concat(s.SlotDigits ?? []))
        .Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    public static FreeDuelDecksProfile LoadEmbedded()
    {
        using var stream = typeof(FreeDuelDecksProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.FreeDuel.free-duel-decks-frame.json")
            ?? throw new InvalidDataException("Missing measured Free Duel deck picker profile.");
        var value = JsonSerializer.Deserialize<FreeDuelDecksProfile>(stream) ?? throw new InvalidDataException("Invalid Free Duel deck picker profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Free Duel deck picker profile/source mismatch.");
        if (value.VisibleRows <= 0 || value.Player.Date is null || value.Player.SlotDigits is not { Length: 3 } || value.CharacterDecks.Length == 0)
            throw new InvalidDataException("Free Duel deck picker profile is incomplete.");
        return value;
    }
}

/// <summary>One list row: original text by source offset or a PC literal, whether the PC can use it (owner decision: only the PC deck), and for
/// saved player recipes the slot and save date.</summary>
public sealed record FreeDuelDeckEntry(FlowTextSource? Source, string? Literal, bool Enabled, int? Slot = null, DateTime? Updated = null);

/// <summary>Deck picker input (inferred from the captured list, same as the Recipe Viewer list): Up/Down move one row and wrap, scrolling only
/// past the shown rows; Confirm takes an enabled row or shows the PC notice on an original one; Back cancels. Tabs are not recreated.</summary>
public sealed class FreeDuelDeckPickerSession(bool player, IReadOnlyList<FreeDuelDeckEntry> entries, int visibleRows)
{
    public bool Player => player;
    public IReadOnlyList<FreeDuelDeckEntry> Entries => entries;
    public int Focus { get; private set; }
    public int Top { get; private set; }
    public bool NoticeOpen { get; private set; }
    public int Revision { get; private set; }
    public enum Outcome { None, Picked, Cancelled }

    public Outcome Handle(InputFrame input)
    {
        var before = (Focus, Top, NoticeOpen);
        var outcome = HandleInput(input);
        if ((Focus, Top, NoticeOpen) != before) Revision++;
        return outcome;
    }

    private Outcome HandleInput(InputFrame input)
    {
        if (NoticeOpen)
        {
            if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Back)) NoticeOpen = false;
            return Outcome.None;
        }
        int n = entries.Count;
        if (input.IsPressed(InputAction.Up)) { Focus = (Focus + n - 1) % n; Reveal(); }
        else if (input.IsPressed(InputAction.Down)) { Focus = (Focus + 1) % n; Reveal(); }
        else if (input.IsPressed(InputAction.Back)) return Outcome.Cancelled;
        else if (input.IsPressed(InputAction.Confirm))
        {
            if (entries[Focus].Enabled) return Outcome.Picked;
            NoticeOpen = true;
        }
        return Outcome.None;
    }

    private void Reveal() => Top = Math.Clamp(Top, Math.Max(0, Focus - visibleRows + 1), Focus);

    /// <summary>The picker's measured thumb rule applied to this list's track (inferred for this window).</summary>
    public (float Top, float Length) Thumb(float trackTop, float trackLength)
    {
        float length = MathF.Min(trackLength, MathF.Floor(trackLength * visibleRows / entries.Count));
        int span = entries.Count - visibleRows;
        return (trackTop + (span <= 0 ? 0 : MathF.Floor(Top * (trackLength - length) / span)), length);
    }

    /// <summary>Opponent list: the PC deck (selectable, first so it is focused), then "Partner's Deck" for partners, then the character's own
    /// four decks, all original rows shown but not selectable.</summary>
    public static IReadOnlyList<FreeDuelDeckEntry> OpponentEntries(FreeDuelDecksProfile profile, int character)
    {
        var decks = profile.CharacterDecks[character];
        var list = new List<FreeDuelDeckEntry> { new(null, FreeDuelVersusRenderer.PcDeckLabel, true) };
        if (decks.Partner) list.Add(new(profile.PartnerDeck, null, false));
        list.AddRange(decks.Decks.Select(d => new FreeDuelDeckEntry(d, null, false)));
        return list;
    }

    /// <summary>Player list: the PC's current deck (selectable), then the imported original save recipes by slot, not selectable.</summary>
    public static IReadOnlyList<FreeDuelDeckEntry> PlayerEntries(string currentDeck, IEnumerable<(int Slot, string Name, DateTime Updated)> saved) =>
        [new(null, currentDeck, true), .. saved.OrderBy(r => r.Slot).Select(r => new FreeDuelDeckEntry(null, r.Name, false, r.Slot, r.Updated))];
}
