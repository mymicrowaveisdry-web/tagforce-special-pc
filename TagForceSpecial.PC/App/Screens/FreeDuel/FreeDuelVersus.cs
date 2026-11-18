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
public sealed record FreeDuelVersusProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Streak, FlowDraw[] Header, FlowDraw[] StartIdle,
    FlowDraw[] StartActive, FlowDraw[] PlayerPortrait, FlowDraw PlayerBust, FlowDraw PlayerLpBadge, FlowDraw[] PlayerLpDigits, FlowDraw[] OpponentPortrait,
    FlowDraw OpponentBust, FlowDraw OpponentLpBadge, FlowDraw[] OpponentLpDigits, FlowDraw[] PlayerNamePanel, FlowDraw[] OpponentNamePanel,
    FlowDraw[] PlayerDeckPanel, FlowDraw[] OpponentDeckPanel, FreeDuelVersusFocusFrames Focus, float DigitU0, float DigitStep, FreeDuelVersusTexts Text,
    FlowTextSource DeckHint, FreeDuelDefaultOpponent DefaultOpponent)
{
    public IEnumerable<string> AssetKeys() => Streak.Concat(Header).Concat(StartIdle).Concat(StartActive).Concat(PlayerPortrait).Append(PlayerBust)
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
/// first step (measured). Choosing a part, Start Duel and □ show a PC notice until their screens exist.</summary>
public sealed class FreeDuelVersusSession
{
    public FreeDuelVersusFocus Focus { get; private set; }
    public bool NoticeOpen { get; private set; }
    public int Revision { get; private set; }

    /// <summary>True when Back leaves the screen.</summary>
    public bool Handle(InputFrame input)
    {
        var (focus, notice) = (Focus, NoticeOpen);
        bool back = HandleInput(input);
        if ((focus, notice) != (Focus, NoticeOpen)) Revision++;
        return back;
    }

    private bool HandleInput(InputFrame input)
    {
        if (NoticeOpen)
        {
            if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Back)) NoticeOpen = false;
            return false;
        }
        const int parts = 4;
        if (input.IsPressed(InputAction.Down)) Focus = (FreeDuelVersusFocus)(((int)Focus + 1) % parts);
        else if (input.IsPressed(InputAction.Up)) Focus = (FreeDuelVersusFocus)(((int)Focus + parts - 1) % parts);
        else if (input.IsPressed(InputAction.Right)) Focus = FreeDuelVersusFocus.Start;
        else if (input.IsPressed(InputAction.Left) && Focus == FreeDuelVersusFocus.Start) Focus = FreeDuelVersusFocus.OpponentName;
        else if (input.IsPressed(InputAction.Back)) return true;
        else if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Switch) && Focus is FreeDuelVersusFocus.OpponentName or FreeDuelVersusFocus.PlayerDeck)
            NoticeOpen = true;
        return false;
    }
}

/// <summary>A Free Duel visit: the settings screen, then VERSUS once Next is chosen. Back from VERSUS returns to the settings' first step
/// with nothing chosen (measured).</summary>
public sealed class FreeDuelSession(FreeDuelSettingsSession settings, string playerName, string playerDeck)
{
    public FreeDuelSettingsSession Settings => settings;
    public FreeDuelVersusSession? Versus { get; private set; }
    public string PlayerName => playerName;
    public string PlayerDeck => playerDeck;
    public bool NoticeOpen => Versus?.NoticeOpen ?? settings.NoticeOpen;
    public int Revision => settings.Revision * 1000 + (Versus?.Revision ?? -1) + (Versus is null ? 0 : 500);

    public void Update(TimeSpan delta) => settings.Update(delta);

    /// <summary>True when Back from the settings' first step leaves Free Duel.</summary>
    public bool Handle(InputFrame input)
    {
        if (Versus is { } versus)
        {
            if (versus.Handle(input)) { Versus = null; settings.Reset(); }
            return false;
        }
        var outcome = settings.Handle(input);
        if (outcome == FreeDuelSettingsOutcome.Next) Versus = new();
        return outcome == FreeDuelSettingsOutcome.Close;
    }
}
