using System.Text.Json;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Focusable parts of the VERSUS screen in their measured Down order (the player's name is not focusable).</summary>
public enum FreeDuelVersusFocus { OpponentName, OpponentDeck, PlayerDeck, Start }

public sealed record FreeDuelVersusTexts(FlowText Header, FreeDuelTextStyle OpponentName, FreeDuelTextStyle OpponentDeck, FreeDuelTextStyle PlayerName,
    FreeDuelTextStyle PlayerDeck, FlowText Hint, FlowText Start);
public sealed record FreeDuelVersusFocusFrames(FlowDraw[] OpponentName, FlowDraw[] OpponentDeck, FlowDraw[] PlayerDeck);
public sealed record FreeDuelDefaultOpponent(FlowTextSource Name, float[][] BustUv);

/// <summary>The measured Normal Duel VERSUS screen (free-duel-versus-frame.json: numbers, asset keys and source offsets only). Portraits are
/// cells of the bust atlas; LP digits are cells of d_menu01 (digit d at <see cref="DigitU0"/> + d × <see cref="DigitStep"/>; 0 and 8 measured).</summary>
public sealed record FreeDuelVersusProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Header, FlowDraw[] StartIdle,
    FlowDraw[] StartActive, FlowDraw[] PlayerPortrait, FlowDraw PlayerBust, FlowDraw PlayerLpBadge, FlowDraw[] PlayerLpDigits, FlowDraw[] OpponentPortrait,
    FlowDraw OpponentBust, FlowDraw OpponentLpBadge, FlowDraw[] OpponentLpDigits, FlowDraw[] PlayerNamePanel, FlowDraw[] OpponentNamePanel,
    FlowDraw[] PlayerDeckPanel, FlowDraw[] OpponentDeckPanel, FreeDuelVersusFocusFrames Focus, float DigitU0, float DigitStep, FreeDuelVersusTexts Text,
    FlowTextSource DeckHint, FreeDuelDefaultOpponent DefaultOpponent)
{
    /// <summary>PC-authored label of the deck the deterministic PC opponent plays (the INITIAL PREVIEW deck).</summary>
    public static string PcDeckLabel => "PC: " + Effects.InitialOpponentDecks.Names[0];

    public IEnumerable<string> AssetKeys() => Header.Concat(StartIdle).Concat(StartActive).Concat(PlayerPortrait).Append(PlayerBust)
        .Append(PlayerLpBadge).Concat(PlayerLpDigits).Concat(OpponentPortrait).Append(OpponentBust).Append(OpponentLpBadge).Concat(OpponentLpDigits)
        .Concat(PlayerNamePanel).Concat(OpponentNamePanel).Concat(PlayerDeckPanel).Concat(OpponentDeckPanel)
        .Concat(Focus.OpponentName).Concat(Focus.OpponentDeck).Concat(Focus.PlayerDeck).Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    public static FreeDuelVersusProfile LoadEmbedded()
    {
        using var stream = typeof(FreeDuelVersusProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.FreeDuel.free-duel-versus-frame.json")
            ?? throw new InvalidDataException("Missing measured Free Duel versus profile.");
        var value = JsonSerializer.Deserialize<FreeDuelVersusProfile>(stream) ?? throw new InvalidDataException("Invalid Free Duel versus profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Free Duel versus profile/source mismatch.");
        if (value.PlayerLpDigits.Length != 5 || value.OpponentLpDigits.Length != 5 || value.Focus.OpponentName.Length != 9 || value.StartActive.Length != 3)
            throw new InvalidDataException("Free Duel versus profile is incomplete.");
        return value;
    }
}

/// <summary>Observed VERSUS input: Down goes opponent name → opponent deck → player deck (measured), then Start Duel (inferred) and back to the
/// name (measured); Up reverses; Right jumps to Start Duel (measured), Left from it returns to the name (inferred). Back returns to the settings'
/// first step (measured). Confirm on the name opens the character picker and □ there picks a random character, on a deck its deck picker,
/// on Start Duel rock-paper-scissors; □ on the player's deck shows a PC notice.</summary>
public sealed class FreeDuelVersusSession
{
    public enum Outcome { None, Back, OpenPicker, RandomCharacter, OpenOpponentDecks, OpenPlayerDecks, StartDuel }
    public FreeDuelVersusFocus Focus { get; private set; }
    public bool NoticeOpen { get; private set; }
    public string? NoticeMessage { get; private set; }
    public int Revision { get; private set; }

    public Outcome Handle(InputFrame input)
    {
        var (focus, notice) = (Focus, NoticeOpen);
        var outcome = HandleInput(input);
        if ((focus, notice) != (Focus, NoticeOpen) || outcome == Outcome.RandomCharacter) Revision++;
        return outcome;
    }

    /// <summary>Redraws after the opponent changed (a pick returned).</summary>
    public void Touch() => Revision++;

    /// <summary>Opens the PC notice (used while a duel cannot start).</summary>
    public void ShowNotice(string message) { NoticeOpen = true; NoticeMessage = message; Revision++; }

    private Outcome HandleInput(InputFrame input)
    {
        if (NoticeOpen)
        {
            if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Back))
            {
                NoticeOpen = false;
                NoticeMessage = null;
            }
            return Outcome.None;
        }
        const int parts = 4;
        if (input.IsPressed(InputAction.Down)) Focus = (FreeDuelVersusFocus)(((int)Focus + 1) % parts);
        else if (input.IsPressed(InputAction.Up)) Focus = (FreeDuelVersusFocus)(((int)Focus + parts - 1) % parts);
        else if (input.IsPressed(InputAction.Right)) Focus = FreeDuelVersusFocus.Start;
        else if (input.IsPressed(InputAction.Left) && Focus == FreeDuelVersusFocus.Start) Focus = FreeDuelVersusFocus.OpponentName;
        else if (input.IsPressed(InputAction.Back)) return Outcome.Back;
        else if (input.IsPressed(InputAction.Confirm) && Focus == FreeDuelVersusFocus.OpponentName) return Outcome.OpenPicker;
        else if (input.IsPressed(InputAction.Confirm) && Focus == FreeDuelVersusFocus.OpponentDeck) return Outcome.OpenOpponentDecks;
        else if (input.IsPressed(InputAction.Confirm) && Focus == FreeDuelVersusFocus.PlayerDeck) return Outcome.OpenPlayerDecks;
        else if (input.IsPressed(InputAction.Confirm) && Focus == FreeDuelVersusFocus.Start) return Outcome.StartDuel;
        else if (input.IsPressed(InputAction.Switch) && Focus == FreeDuelVersusFocus.OpponentName) return Outcome.RandomCharacter;
        else if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Switch) && Focus == FreeDuelVersusFocus.PlayerDeck) NoticeOpen = true;
        return Outcome.None;
    }
}

/// <summary>A Free Duel visit: the settings screen, then VERSUS once Next is chosen. Back from VERSUS returns to the settings' first step
/// with nothing chosen (measured).</summary>
public sealed class FreeDuelSession(FreeDuelSettingsSession settings, string playerName, string playerDeck, FreeDuelPickerProfile? roster = null,
    int defaultOpponent = 0, Random? random = null, FreeDuelDecksProfile? decks = null, IReadOnlyList<(int Slot, string Name, DateTime Updated)>? saved = null)
{
    /// <summary>A deck picker while open over VERSUS.</summary>
    public FreeDuelDeckPickerSession? DeckPicker { get; private set; }
    private readonly Random random = random ?? new Random();
    public FreeDuelSettingsSession Settings => settings;
    public FreeDuelVersusSession? Versus { get; private set; }
    /// <summary>The character picker while open over VERSUS.</summary>
    public FreeDuelPickerSession? Picker { get; private set; }
    /// <summary>Rock-paper-scissors, turn order and intro after Start Duel.</summary>
    public FreeDuelStartSession? Start { get; private set; }
    /// <summary>Roster index of the opponent character (the measured default until a pick).</summary>
    public int Opponent { get; private set; } = defaultOpponent;
    public string PlayerName => playerName;
    public string PlayerDeck => playerDeck;
    public bool NoticeOpen => DeckPicker?.NoticeOpen ?? Versus?.NoticeOpen ?? settings.NoticeOpen;
    public int Revision => settings.Revision * 100000 + (Versus?.Revision ?? -1) * 100 + (Versus is null ? 0 : 50) + (Picker?.Revision ?? -1) * 7 + Opponent * 13 + (Picker is null ? 0 : 3) + (DeckPicker?.Revision ?? -1) * 17 + (DeckPicker is null ? 0 : 5)
        + (Start?.Revision ?? -1) * 31 + (Start is null ? 0 : 11);

    public void Update(TimeSpan delta) { settings.Update(delta); Start?.Update(delta); }
    internal void BeginStart() => Start = new(random);

    /// <summary>The turn order once the intro has played (the duel can start), then back on VERSUS; null before.</summary>
    public bool? TakeReadyDuel()
    {
        if (Start is not { Step: FreeDuelStartStep.Ready } start) return null;
        Start = null;
        return start.PlayerGoesFirst;
    }

    /// <summary>True when Back from the settings' first step leaves Free Duel.</summary>
    public bool Handle(InputFrame input)
    {
        if (Start is { } start)
        {
            if (input.IsPressed(InputAction.Back))
            {
                Start = null;
                Versus!.Touch();
            }
            else start.Handle(input);
            return false;
        }
        if (DeckPicker is { } deckPicker)
        {
            // Only the PC deck can be picked, so a pick keeps the current choice; either way the list closes.
            if (deckPicker.Handle(input) != FreeDuelDeckPickerSession.Outcome.None) { DeckPicker = null; Versus!.Touch(); }
            return false;
        }
        if (Picker is { } picker)
        {
            var picked = picker.Handle(input);
            if (picked == FreeDuelPickerSession.Outcome.Picked) Opponent = picker.FocusedCharacter;
            if (picked != FreeDuelPickerSession.Outcome.None) { Picker = null; Versus!.Touch(); }
            return false;
        }
        if (Versus is { } versus)
        {
            switch (versus.Handle(input))
            {
                case FreeDuelVersusSession.Outcome.Back: Versus = null; settings.Reset(); break;
                case FreeDuelVersusSession.Outcome.OpenPicker when roster is not null: Picker = new(roster, Opponent); break;
                case FreeDuelVersusSession.Outcome.RandomCharacter when roster is not null: Opponent = random.Next(roster.Characters.Length); break;
                case FreeDuelVersusSession.Outcome.OpenOpponentDecks when decks is not null:
                    DeckPicker = new(false, FreeDuelDeckPickerSession.OpponentEntries(decks, Opponent), decks.VisibleRows); break;
                case FreeDuelVersusSession.Outcome.OpenPlayerDecks when decks is not null:
                    DeckPicker = new(true, FreeDuelDeckPickerSession.PlayerEntries(playerDeck, saved ?? []), decks.VisibleRows); break;
                case FreeDuelVersusSession.Outcome.StartDuel: BeginStart(); break;
            }
            return false;
        }
        var outcome = settings.Handle(input);
        if (outcome == FreeDuelSettingsOutcome.Next) Versus = new();
        return outcome == FreeDuelSettingsOutcome.Close;
    }
}
