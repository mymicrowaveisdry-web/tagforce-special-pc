using System.Text.Json;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

public sealed record FreeDuelStartTexts(FreeDuelTextStyle PlayerName, FreeDuelTextStyle OpponentName, FreeDuelTextStyle Message);
public sealed record FreeDuelStartPrompts(FlowTextSource ChooseCard, FlowTextSource OpponentChoosing, FlowTextSource ChooseOrder);

/// <summary>The measured duel start (free-duel-start-frame.json: numbers, asset keys and source offsets only): rock-paper-scissors hand and
/// reveal, the turn-order buttons and the intro card. Hand cards are 80-pixel cells of jnk_card01 (Rock, Scissors, Paper; the opponent's row
/// below); the WIN / LOSE / DRAW labels are 80-pixel cells (DRAW measured, WIN and LOSE by the atlas). The intro is the VERSUS body moved down
/// <see cref="IntroShift"/> pixels with its own header.</summary>
public sealed record FreeDuelStartProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Particles, FlowDraw[] Frame, FlowDraw PlayerBust,
    FlowDraw OpponentBust, FlowDraw[] Hand, FlowDraw[] HandFocus, float HandRaise, FlowDraw[] OpponentBacks, FlowDraw RevealPlayer, FlowDraw RevealOpponent,
    FlowDraw LabelPlayer, FlowDraw LabelOpponent, float CardCellWidth, float LabelCellWidth, float AtlasWidth, FlowDraw[] FirstButton, FlowDraw[] SecondDark,
    FlowDraw[] SecondLight, FlowDraw[] IntroHeader, float IntroShift, FreeDuelStartTexts Text, FreeDuelStartPrompts Prompts)
{
    public IEnumerable<string> AssetKeys() => Particles.Concat(Frame).Append(PlayerBust).Append(OpponentBust).Concat(Hand).Concat(HandFocus)
        .Concat(OpponentBacks).Append(RevealPlayer).Append(RevealOpponent).Append(LabelPlayer).Append(LabelOpponent).Concat(FirstButton)
        .Concat(SecondDark).Concat(SecondLight).Concat(IntroHeader).Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    public static FreeDuelStartProfile LoadEmbedded()
    {
        using var stream = typeof(FreeDuelStartProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.FreeDuel.free-duel-start-frame.json")
            ?? throw new InvalidDataException("Missing measured Free Duel start profile.");
        var value = JsonSerializer.Deserialize<FreeDuelStartProfile>(stream) ?? throw new InvalidDataException("Invalid Free Duel start profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Free Duel start profile/source mismatch.");
        if (value.Hand.Length != 3 || value.OpponentBacks.Length != 3 || value.FirstButton.Length != 6 || value.SecondDark.Length != 3 || value.SecondLight.Length != 3)
            throw new InvalidDataException("Free Duel start profile is incomplete.");
        return value;
    }
}

/// <summary>Hands in their atlas order.</summary>
public enum FreeDuelHand { Rock, Scissors, Paper }
public enum FreeDuelRpsResult { Win, Lose, Draw }
public enum FreeDuelStartStep { Choose, Reveal, ChooseOrder, OpponentChoosing, Intro, Ready }

/// <summary>Rock-paper-scissors, then the turn order, then the intro card. Measured: the hand opens with Paper focused, Left/Right move the
/// raised card, Confirm plays it; both cards are shown with WIN / LOSE (DRAW on a tie); the winner picks First or Second to Go with First focused
/// and Up/Down; while the CPU picks, "Opponent is choosing..." shows with First focused. Inferred: no wrap on the hand, a tie deals again, the
/// CPU's hand and order are random, and the hold times (reveal 1.5 s, CPU choice 2 s, intro 3 s; animations are not recreated).</summary>
public sealed class FreeDuelStartSession(Random random)
{
    public static readonly TimeSpan RevealHold = TimeSpan.FromSeconds(1.5), ChoosingHold = TimeSpan.FromSeconds(2), IntroHold = TimeSpan.FromSeconds(3);
    private TimeSpan elapsed;
    public FreeDuelStartStep Step { get; private set; }
    public FreeDuelHand Focus { get; private set; } = FreeDuelHand.Paper;
    public FreeDuelHand PlayerHand { get; private set; }
    public FreeDuelHand OpponentHand { get; private set; }
    public FreeDuelRpsResult Result => Outcome(PlayerHand, OpponentHand);
    /// <summary>Focused turn-order button: true for First to Go.</summary>
    public bool FirstFocused { get; private set; } = true;
    /// <summary>Who goes first, once chosen.</summary>
    public bool PlayerGoesFirst { get; private set; }
    public int Revision { get; private set; }

    /// <summary>Rock beats Scissors, Scissors beats Paper, Paper beats Rock.</summary>
    public static FreeDuelRpsResult Outcome(FreeDuelHand player, FreeDuelHand opponent) =>
        player == opponent ? FreeDuelRpsResult.Draw : ((int)player - (int)opponent + 3) % 3 == 2 ? FreeDuelRpsResult.Win : FreeDuelRpsResult.Lose;

    public void Handle(InputFrame input)
    {
        switch (Step)
        {
            case FreeDuelStartStep.Choose:
                if (input.IsPressed(InputAction.Left) && Focus > FreeDuelHand.Rock) { Focus--; Revision++; }
                else if (input.IsPressed(InputAction.Right) && Focus < FreeDuelHand.Paper) { Focus++; Revision++; }
                else if (input.IsPressed(InputAction.Confirm))
                {
                    PlayerHand = Focus; OpponentHand = (FreeDuelHand)random.Next(3);
                    Enter(FreeDuelStartStep.Reveal);
                }
                break;
            case FreeDuelStartStep.ChooseOrder:
                if (input.IsPressed(InputAction.Up) || input.IsPressed(InputAction.Down)) { FirstFocused = !FirstFocused; Revision++; }
                else if (input.IsPressed(InputAction.Confirm)) { PlayerGoesFirst = FirstFocused; Enter(FreeDuelStartStep.Intro); }
                break;
        }
    }

    public void Update(TimeSpan delta)
    {
        elapsed += delta;
        switch (Step)
        {
            case FreeDuelStartStep.Reveal when elapsed >= RevealHold:
                Enter(Result switch
                {
                    FreeDuelRpsResult.Win => FreeDuelStartStep.ChooseOrder,
                    FreeDuelRpsResult.Lose => FreeDuelStartStep.OpponentChoosing,
                    _ => FreeDuelStartStep.Choose
                });
                FirstFocused = true;
                break;
            case FreeDuelStartStep.OpponentChoosing when elapsed >= ChoosingHold:
                // The CPU picks its own order; the player goes first when it picks Second.
                PlayerGoesFirst = random.Next(2) == 1;
                Enter(FreeDuelStartStep.Intro);
                break;
            case FreeDuelStartStep.Intro when elapsed >= IntroHold:
                Enter(FreeDuelStartStep.Ready);
                break;
        }
    }

    private void Enter(FreeDuelStartStep step) { Step = step; elapsed = TimeSpan.Zero; Revision++; }
}
