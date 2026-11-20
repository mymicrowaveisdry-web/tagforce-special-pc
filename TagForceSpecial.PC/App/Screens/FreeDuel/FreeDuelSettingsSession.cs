using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>The steps of the original Free Duel "Change duel settings" screen, in the order the original asks them.</summary>
public enum FreeDuelStep { Format, Ending, Rules, LifePoints, TimeLimit, Music, Confirm }

public enum FreeDuelSettingsOutcome { None, Close, Notice, Next }

/// <summary>Chosen settings once the confirmation step is reached. Rows are the original's list indices.</summary>
public sealed record FreeDuelSettings(int Format, int Ending, int Rules, int LifePoints, int TimeLimit, int Music)
{
    /// <summary>Measured: "No (Standard Setting)" starts out with 8000 Life Points and no time limit.</summary>
    public int StartingLifePoints => Rules == 0 ? 8000 : LifePoints == 0 ? 4000 : 8000;
}

/// <summary>Observed Free Duel settings input: Up/Down move one row and wrap; Confirm takes the focused row and opens the next step on
/// its first row; Back returns one step with the earlier choice focused and forgets the later ones, and leaves from the first step.
/// Rows the PC cannot play yet show a PC notice instead of being taken (owner decision); either button closes it and keeps focus.</summary>
public sealed class FreeDuelSettingsSession(int musicRows, int visibleRows = 6)
{
    private readonly int?[] chosen = new int?[Enum.GetValues<FreeDuelStep>().Length];
    public FreeDuelStep Step { get; private set; }
    public int Focus { get; private set; }
    public bool NoticeOpen { get; private set; }
    /// <summary>First list row shown. Measured on the music list: moving past the last shown row scrolls by one; wrapping shows the first
    /// or the last page; moves inside the shown rows do not scroll.</summary>
    public int Top { get; private set; }
    public int VisibleRows => visibleRows;
    private TimeSpan clock;
    /// <summary>Game frames since the screen opened (drives the highlight pulse).</summary>
    public int Frame => Title.MainMenuEntry.FramesIn(clock);
    /// <summary>Bumped whenever anything drawn changes, so the host can skip re-rendering.</summary>
    public int Revision { get; private set; }

    public void Update(TimeSpan delta) => clock += delta;

    /// <summary>Shows the PC notice without input: Next opens the versus screen, which the PC has not recreated yet.</summary>
    public void ShowNotice() { NoticeOpen = true; Revision++; }

    /// <summary>Rows of each step: three formats, two ending types, two rule answers, LP 4000/8000, five time limits, the music list, and
    /// the confirmation's Next button.</summary>
    public int Rows(FreeDuelStep step) => step switch
    {
        FreeDuelStep.Format => 3, FreeDuelStep.Ending or FreeDuelStep.Rules or FreeDuelStep.LifePoints => 2, FreeDuelStep.TimeLimit => 5,
        FreeDuelStep.Music => musicRows, _ => 1
    };

    /// <summary>The row taken at a step, if that step has been answered.</summary>
    public int? Chosen(FreeDuelStep step) => chosen[(int)step];

    /// <summary>Owner decision, 18 Nov 2026: Tag Duel, CPU Spectator, Match and turn time limits are drawn but not playable yet.</summary>
    public static bool Playable(FreeDuelStep step, int row) => step switch
    {
        FreeDuelStep.Format or FreeDuelStep.Ending or FreeDuelStep.TimeLimit => row == 0,
        _ => true
    };

    public FreeDuelSettings? Settings => Step == FreeDuelStep.Confirm
        ? new(chosen[0]!.Value, chosen[1]!.Value, chosen[2]!.Value, chosen[3] ?? 1, chosen[4] ?? 0, chosen[5]!.Value) : null;

    /// <summary>Measured: Back from the versus screen returns to the first step with Normal Duel focused and nothing chosen.</summary>
    public void Reset() { Array.Clear(chosen); Step = FreeDuelStep.Format; Focus = 0; Top = 0; NoticeOpen = false; }

    private void Reveal() => Top = Math.Clamp(Top, Math.Max(0, Focus - visibleRows + 1), Focus);

    public FreeDuelSettingsOutcome Handle(InputFrame input)
    {
        var (step, focus, top, notice) = (Step, Focus, Top, NoticeOpen);
        var outcome = HandleInput(input);
        if ((step, focus, top, notice) != (Step, Focus, Top, NoticeOpen)) Revision++;
        return outcome;
    }

    private FreeDuelSettingsOutcome HandleInput(InputFrame input)
    {
        if (NoticeOpen)
        {
            if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Back)) NoticeOpen = false;
            return FreeDuelSettingsOutcome.None;
        }
        int rows = Rows(Step);
        if (input.IsPressed(InputAction.Up)) { Focus = (Focus + rows - 1) % rows; Reveal(); }
        else if (input.IsPressed(InputAction.Down)) { Focus = (Focus + 1) % rows; Reveal(); }
        else if (input.IsPressed(InputAction.Back)) return Back();
        else if (input.IsPressed(InputAction.Confirm)) return Confirm();
        return FreeDuelSettingsOutcome.None;
    }

    private FreeDuelSettingsOutcome Confirm()
    {
        if (Step == FreeDuelStep.Confirm) return FreeDuelSettingsOutcome.Next;
        if (!Playable(Step, Focus)) { NoticeOpen = true; return FreeDuelSettingsOutcome.Notice; }
        chosen[(int)Step] = Focus;
        Step = Step switch
        {
            FreeDuelStep.Format => FreeDuelStep.Ending,
            FreeDuelStep.Ending => FreeDuelStep.Rules,
            FreeDuelStep.Rules => Focus == 0 ? FreeDuelStep.Music : FreeDuelStep.LifePoints,
            FreeDuelStep.LifePoints => FreeDuelStep.TimeLimit,
            FreeDuelStep.TimeLimit => FreeDuelStep.Music,
            _ => FreeDuelStep.Confirm
        };
        Focus = 0; Top = 0;
        return FreeDuelSettingsOutcome.None;
    }

    private FreeDuelSettingsOutcome Back()
    {
        if (Step == FreeDuelStep.Format) return FreeDuelSettingsOutcome.Close;
        var previous = Step switch
        {
            FreeDuelStep.Ending => FreeDuelStep.Format,
            FreeDuelStep.Rules => FreeDuelStep.Ending,
            FreeDuelStep.LifePoints => FreeDuelStep.Rules,
            FreeDuelStep.TimeLimit => FreeDuelStep.LifePoints,
            FreeDuelStep.Music => chosen[(int)FreeDuelStep.Rules] == 0 ? FreeDuelStep.Rules : FreeDuelStep.TimeLimit,
            _ => FreeDuelStep.Music
        };
        for (int s = (int)previous; s < chosen.Length; s++)
            if (s != (int)previous) chosen[s] = null;
        Focus = chosen[(int)previous] ?? 0;
        chosen[(int)previous] = null;
        Step = previous;
        Top = 0; Reveal(); // inferred: returning to a scrolled list shows the earlier choice on the last shown row

        return FreeDuelSettingsOutcome.None;
    }
}
