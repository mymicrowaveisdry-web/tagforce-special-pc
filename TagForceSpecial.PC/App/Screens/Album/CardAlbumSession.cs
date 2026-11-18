using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.Album;

public enum CardAlbumFocus { Pocket, Edge, Bar }
public enum CardAlbumOutcome { None, Close, DetailRequested }

/// <summary>One page turn in progress: direction, the view it started from and game frames elapsed.</summary>
public sealed record CardAlbumTurn(int Direction, int FromView, int Frames);

/// <summary>Card Album state and input as observed on the same build. Measured: D-pad moves within the 3x3 page and across to the other
/// page; Right past the last column reaches the page edge, where Right or Confirm turns forward; Down past the last row reaches the bar;
/// L/R and the bar's single arrows turn one view (no wrap, held L/R repeat); the bar's double arrows move one card; its book buttons flip to
/// the first or last view; Switch moves to the bar's first button; Back leaves. Confirm on an owned card asks for the detail screen.</summary>
public sealed class CardAlbumSession
{
    public const int BarButtons = 6;
    /// <summary>Captured page-turn keyframes: 14 poses, two game frames each.</summary>
    public const int TurnFrames = 28;
    /// <summary>Inferred from the captured fast flip (about ten views per second): game frames per view while flipping.</summary>
    public const int FlipFramesPerView = 3;
    private readonly MenuHoldRepeat repeat = new(TitleFlow.MenuRepeatDelay, TitleFlow.MenuRepeatInterval);
    private TimeSpan turnClock, glintClock, jumpClock;
    private int jumpTarget, jumpLength = 7, jumpTurnFrame = 5;
    private uint? jumpPreview;
    private bool jumpPreviewBack;
    private int heldTurn, flipTarget = -1, lastRow;

    public IReadOnlyList<uint> Order { get; }
    public CardAlbumCollection Collection { get; }
    public int Views { get; }
    public int View { get; private set; }
    public CardAlbumFocus Focus { get; private set; } = CardAlbumFocus.Pocket;
    public bool RightPage { get; private set; } = true;
    public int Pocket { get; private set; }
    public int BarButton { get; private set; }
    public CardAlbumTurn? Turn { get; private set; }
    public bool Flipping => flipTarget >= 0;
    /// <summary>Game frames since the album opened, for the info strip's glint loop (inferred: the loop starts with the album).</summary>
    public int GlintFrame { get; private set; }
    /// <summary>Bumped whenever anything drawn changes, so the host can skip re-rendering.</summary>
    public int Revision { get; private set; }

    /// <summary>Measured Page Jump grid: 8 columns by 5 rows; '_' cells take focus but do nothing.</summary>
    public static readonly string[] JumpLayout = ["*ABCD_YZ", "EFGHI___", "JKLMN___", "OPQRS___", "TUVWX___"];
    private readonly IReadOnlyList<char> initials;
    private readonly HashSet<char> ownedInitials;
    public bool JumpOpen { get; private set; }
    public int JumpRow { get; private set; }
    public int JumpColumn { get; private set; }
    /// <summary>Game frames since a Page Jump letter was accepted, while its close and arrival play; input waits until it ends.</summary>
    public int? JumpFrame { get; private set; }
    /// <summary>View the jump started from (drawn until the arrival) and the direction of its single turn pose.</summary>
    public int JumpFromView { get; private set; }
    public int JumpDirection { get; private set; }
    public int JumpTargetView { get; private set; }
    /// <summary>Measured: the hand is not drawn while Page Jump is open or closing.</summary>
    public bool HandHidden => JumpOpen || JumpFrame is not null;

    /// <summary>Sets the measured close/arrival timing (frames of the turn pose and in all).</summary>
    public void UseJumpTiming(int turnFrame, int length) { jumpTurnFrame = turnFrame; jumpLength = length; }

    /// <summary>The card detail over the album, while open (Confirm on an owned card).</summary>
    public CardAlbumDetail? Detail { get; private set; }
    private Func<uint, CardAlbumDetail>? openDetail;
    private int detailCloseUpdates;

    /// <summary>Builds the detail for a card (counts and tab heights come from the screen); without it Confirm only reports the request.</summary>
    public void UseDetail(Func<uint, CardAlbumDetail> factory) => openDetail = factory;

    /// <param name="initials">Page Jump letter of each album position ('*' for names not starting with a letter); defaults to '*'.</param>
    public CardAlbumSession(IReadOnlyList<uint> order, CardAlbumCollection collection, IReadOnlyList<char>? initials = null)
    {
        Order = order; Collection = collection;
        Views = CardAlbumLayout.Views(order.Count);
        this.initials = initials ?? Enumerable.Repeat('*', order.Count).ToArray();
        ownedInitials = [.. Enumerable.Range(0, order.Count).Where(i => collection.Owns(order[i])).Select(i => this.initials[i])];
    }

    /// <summary>Measured on the fresh save: a letter is drawn dimmed, and does nothing, when no owned card starts with it.</summary>
    public bool JumpEnabled(char letter) => ownedInitials.Contains(letter);

    /// <summary>Measured: Up/Down/Left/Right wrap over the grid; Back closes; Confirm on an enabled letter jumps to the first owned card
    /// with that initial after the measured close and arrival.</summary>
    private void HandleJump(InputFrame input)
    {
        int rows = JumpLayout.Length, columns = JumpLayout[0].Length;
        if (input.IsPressed(InputAction.Back)) JumpOpen = false;
        else if (input.IsPressed(InputAction.Up)) JumpRow = (JumpRow + rows - 1) % rows;
        else if (input.IsPressed(InputAction.Down)) JumpRow = (JumpRow + 1) % rows;
        else if (input.IsPressed(InputAction.Left)) JumpColumn = (JumpColumn + columns - 1) % columns;
        else if (input.IsPressed(InputAction.Right)) JumpColumn = (JumpColumn + 1) % columns;
        else if (input.IsPressed(InputAction.Confirm))
        {
            char letter = JumpLayout[JumpRow][JumpColumn];
            if (letter == '_' || !JumpEnabled(letter)) return;
            int target = Enumerable.Range(0, Order.Count).First(i => initials[i] == letter && Collection.Owns(Order[i]));
            JumpOpen = false;
            // Measured: the overlay closes, one turn pose plays, then the target view shows the old preview for a frame.
            jumpPreview = PreviewCard; jumpPreviewBack = PreviewsCardBack;
            jumpTarget = target; jumpClock = TimeSpan.Zero; JumpFrame = 0; JumpFromView = View;
            JumpTargetView = CardAlbumLayout.Locate(target).View;
            JumpDirection = JumpTargetView >= View ? 1 : -1;
        }
        else return;
        Revision++;
    }

    public int? FocusedPosition => Focus == CardAlbumFocus.Pocket ? CardAlbumLayout.Position(View, RightPage, Pocket, Order.Count) : null;

    /// <summary>The card shown in the preview: the focused pocket's card when owned. Measured: an unowned pocket shows a card back and no text.</summary>
    public uint? PreviewCard
    {
        get
        {
            if (JumpFrame is not null) return jumpPreview;
            // Measured: on the page edge and the bar the preview keeps the last pocket's card.
            if (Focus != CardAlbumFocus.Pocket) return lastPreview;
            return FocusedPosition is int p && Collection.Owns(Order[p]) ? Order[p] : null;
        }
    }
    private uint? lastPreview;

    /// <summary>Measured on the fresh save: a focused unowned pocket previews the card back with no text.</summary>
    public bool PreviewsCardBack => JumpFrame is not null ? jumpPreviewBack : Focus == CardAlbumFocus.Pocket && FocusedPosition is int p && !Collection.Owns(Order[p]);

    /// <summary>Measured page counter: the sheet under the cursor (the edge counts as the right page).</summary>
    public int CounterSheet => CardAlbumLayout.CounterSheet(View, Focus != CardAlbumFocus.Pocket || RightPage);
    public int Sheets => CardAlbumLayout.Sheets(Order.Count);

    public void Update(TimeSpan delta)
    {
        glintClock += delta;
        if (MainMenuEntry.FramesIn(glintClock) is var glint && glint != GlintFrame) { GlintFrame = glint; Revision++; }
        if (Detail is { Closing: true } && ++detailCloseUpdates >= 2) { Detail = null; Revision++; }
        if (JumpFrame is int jump)
        {
            jumpClock += delta;
            int frames = MainMenuEntry.FramesIn(jumpClock);
            if (frames == jump) return;
            // The target view replaces the old one on the frame after the turn pose.
            if (jump <= jumpTurnFrame && frames > jumpTurnFrame) JumpTo(jumpTarget);
            JumpFrame = frames >= jumpLength ? null : frames;
            Revision++;
            return;
        }
        if (Turn is { } turn)
        {
            turnClock += delta;
            int frames = MainMenuEntry.FramesIn(turnClock);
            int length = Flipping ? FlipFramesPerView : TurnFrames;
            if (frames >= length) { Turn = null; turnClock = TimeSpan.Zero; ContinueFlip(); }
            else if (frames != turn.Frames) Turn = turn with { Frames = frames };
            Revision++;
            return;
        }
        if (heldTurn != 0)
            for (int due = repeat.Advance(delta); due > 0 && Turn is null; due--) StartTurn(heldTurn);
    }

    public CardAlbumOutcome Handle(InputFrame input)
    {
        int held = (input.Held & (InputAction.PreviousPage | InputAction.NextPage)) switch { InputAction.PreviousPage => -1, InputAction.NextPage => 1, _ => 0 };
        if (held != heldTurn) { heldTurn = 0; repeat.Restart(); }
        if (Detail is { } detail) { if (detail.Handle(input)) Revision++; return CardAlbumOutcome.None; }
        if (Turn is not null || Flipping || JumpFrame is not null) return CardAlbumOutcome.None;
        if (JumpOpen) { HandleJump(input); return CardAlbumOutcome.None; }
        if (input.IsPressed(InputAction.Back)) return CardAlbumOutcome.Close;
        int before = Revision;
        // Measured: Triangle opens Page Jump with the first cell ("*") focused.
        if (input.IsPressed(InputAction.Menu)) { JumpOpen = true; JumpRow = 0; JumpColumn = 0; Revision++; return CardAlbumOutcome.None; }
        if (input.IsPressed(InputAction.PreviousPage) || input.IsPressed(InputAction.NextPage))
        {
            int direction = input.IsPressed(InputAction.NextPage) ? 1 : -1;
            heldTurn = direction; repeat.Restart();
            StartTurn(direction);
        }
        else if (input.IsPressed(InputAction.Switch) && Focus != CardAlbumFocus.Bar) { Remember(); Focus = CardAlbumFocus.Bar; BarButton = 0; Revision++; }
        else if (Focus == CardAlbumFocus.Bar) HandleBar(input);
        else if (Focus == CardAlbumFocus.Edge) HandleEdge(input);
        else if (HandlePocket(input) is var outcome && outcome != CardAlbumOutcome.None) return outcome;
        if (Revision != before) lastPreview ??= PreviewCard;
        return CardAlbumOutcome.None;
    }

    private CardAlbumOutcome HandlePocket(InputFrame input)
    {
        int row = Pocket / 3, col = Pocket % 3;
        if (input.IsPressed(InputAction.Up) && row > 0) Move(RightPage, Pocket - 3);
        else if (input.IsPressed(InputAction.Down))
        {
            if (row < 2) Move(RightPage, Pocket + 3);
            // Measured from the right page: Down past the last row reaches the bar's forward arrow; the left page's target is inferred.
            else { Remember(); Focus = CardAlbumFocus.Bar; BarButton = RightPage ? 3 : 2; Revision++; }
        }
        else if (input.IsPressed(InputAction.Right))
        {
            if (col < 2) Move(RightPage, Pocket + 1);
            else if (!RightPage && PageExists(true)) Move(true, row * 3);
            else if (RightPage && View + 1 < Views) { Remember(); lastRow = row; Focus = CardAlbumFocus.Edge; Revision++; }
        }
        else if (input.IsPressed(InputAction.Left))
        {
            if (col > 0) Move(RightPage, Pocket - 1);
            else if (RightPage && PageExists(false)) Move(false, row * 3 + 2);
            // Inferred: the left page's outer column has no edge target (only the right edge was observed).
        }
        else if (input.IsPressed(InputAction.Confirm) && FocusedPosition is int p && Collection.Owns(Order[p]))
        {
            // Measured: the detail replaces the album at once; Back returns with the cursor unchanged.
            if (openDetail is not null) { Detail = openDetail(Order[p]); detailCloseUpdates = 0; Revision++; }
            return CardAlbumOutcome.DetailRequested;
        }
        return CardAlbumOutcome.None;
    }

    private void HandleEdge(InputFrame input)
    {
        if (input.IsPressed(InputAction.Right) || input.IsPressed(InputAction.Confirm)) StartTurn(1);
        // Inferred: Left returns to the last column of the row the edge was entered from.
        else if (input.IsPressed(InputAction.Left)) { Focus = CardAlbumFocus.Pocket; RightPage = true; Pocket = lastRow * 3 + 2; Revision++; }
    }

    private void HandleBar(InputFrame input)
    {
        if (input.IsPressed(InputAction.Right)) { BarButton = (BarButton + 1) % BarButtons; Revision++; }
        else if (input.IsPressed(InputAction.Left)) { BarButton = (BarButton + BarButtons - 1) % BarButtons; Revision++; }
        // Inferred: Up or Switch returns to the pockets where the cursor was.
        else if (input.IsPressed(InputAction.Up) || input.IsPressed(InputAction.Switch)) { Focus = CardAlbumFocus.Pocket; Revision++; }
        else if (input.IsPressed(InputAction.Confirm))
            switch (BarButton)
            {
                case 0: StartFlip(0); break;
                case 1: StepCard(-1); break;
                case 2: StartTurn(-1); break;
                case 3: StartTurn(1); break;
                case 4: StepCard(1); break;
                case 5: StartFlip(Views - 1); break;
            }
    }

    /// <summary>Measured: the double arrows move the highlighted card by one album position while focus stays on the bar.</summary>
    private void StepCard(int delta)
    {
        int current = CardAlbumLayout.Position(View, RightPage, Pocket, Order.Count) ?? 0;
        int next = Math.Clamp(current + delta, 0, Order.Count - 1);
        var (view, right, pocket) = CardAlbumLayout.Locate(next);
        if (view != View) { View = view; }
        RightPage = right; Pocket = pocket; lastPreview = Collection.Owns(Order[next]) ? Order[next] : null;
        Revision++;
    }

    private void StartTurn(int direction)
    {
        int target = View + direction;
        if (target < 0 || target >= Views) return;
        Turn = new(direction, View, 0); turnClock = TimeSpan.Zero;
        View = target;
        // Measured: the cursor keeps its page and pocket; a page that does not exist in the new view moves it to the other page (inferred).
        if (!PageExists(RightPage)) RightPage = !RightPage;
        if (Focus == CardAlbumFocus.Edge && View + 1 >= Views) { Focus = CardAlbumFocus.Pocket; RightPage = false; Pocket = lastRow * 3 + 2; }
        Revision++;
    }

    private void StartFlip(int target)
    {
        if (target == View) return;
        flipTarget = target;
        ContinueFlip();
    }

    private void ContinueFlip()
    {
        if (flipTarget < 0) return;
        if (View == flipTarget) { flipTarget = -1; return; }
        StartTurn(flipTarget > View ? 1 : -1);
    }

    private bool PageExists(bool right) => CardAlbumLayout.Position(View, right, 0, Order.Count) is not null;

    private void Move(bool right, int pocket)
    {
        // Inferred: a pocket past the album's last card cannot be focused (cursor limits on the last view were not captured).
        if (CardAlbumLayout.Position(View, right, pocket, Order.Count) is null) return;
        RightPage = right; Pocket = pocket; Revision++;
        lastPreview = PreviewCard;
    }

    private void Remember() => lastPreview = PreviewCard;

    /// <summary>Jumps to a measured Page Jump target: the view and pocket of an album position.</summary>
    public void JumpTo(int position)
    {
        var (view, right, pocket) = CardAlbumLayout.Locate(Math.Clamp(position, 0, Order.Count - 1));
        View = view; RightPage = right; Pocket = pocket; Focus = CardAlbumFocus.Pocket; Revision++;
        int p = CardAlbumLayout.Position(View, RightPage, Pocket, Order.Count) ?? 0;
        lastPreview = Collection.Owns(Order[p]) ? Order[p] : null;
    }
}
