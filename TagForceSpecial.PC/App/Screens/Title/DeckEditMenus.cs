using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>One open popup and its focused row or cell; <see cref="OnRun"/> is the Run button below a grid.</summary>
public sealed record DeckEditMenuLevel(string Id, int Focus, bool OnRun = false);

/// <summary>Deck Edit Menu chain (Triangle on the list), from the menus-20261115 walks. Observed: back (original Cross) closes one level
/// everywhere; greyed rows take the cursor and do nothing; Attach/Remove Label toggles on the focused card and the menu stays; Search for
/// Related Cards, Reset Deck and Empty Deck apply at once; Sort re-sorts live as the cursor moves and Right opens Order Normal/Inverse,
/// where picking closes all menus; grid cells toggle with confirm and Run closes the menus, narrows the list and shows "%d card(s) have met
/// these conditions." over it.
/// Inferred: list and grid cursors wrap; Down from a grid's last row reaches Run; menus close after Reset Narrow Search Results, Undo,
/// Reset Deck, Empty Deck and a Label Display pick; back from Sort restores the saved sort. Rows the PC does not recreate open the PC notice.</summary>
public sealed class DeckEditMenus
{
    public static readonly string[] PopupIds = ["main", "search", "narrow", "sort", "sort-order", "deck-recipe", "music", "label-display", "card-frame",
        "attribute", "forbidden", "layout", "level", "scale", "spell-trap-icon", "type", "key-search"];
    /// <summary>Narrow Search rows in order: the seven filtering panels, then Key Search.</summary>
    public static readonly string[] NarrowGrids = ["card-frame", "type", "attribute", "level", "scale", "spell-trap-icon", "forbidden", "key-search"];
    private const int MainLabel = 1, MainDestiny = 2, MainResetNarrow = 7, MainUndo = 8;
    private readonly DeckEditMenuProfile profile;
    private readonly List<DeckEditMenuLevel> stack = [];
    private readonly Dictionary<DeckEditNarrowPanel, HashSet<int>> pending = Enum.GetValues<DeckEditNarrowPanel>().ToDictionary(p => p, _ => new HashSet<int>());
    private (DeckEditSortKey? Key, bool Inverse) savedSort;

    public DeckEditMenus(DeckEditMenuProfile profile) => this.profile = profile;

    public IReadOnlyList<DeckEditMenuLevel> Stack => stack;
    /// <summary>A popup or the Run notice (shown after the menus close) is open.</summary>
    public bool Open => stack.Count > 0 || RunCount is not null;
    public DeckEditMenuLevel? Top => stack.Count == 0 ? null : stack[^1];
    /// <summary>Frames since the focused popup opened: drives the focus band pulse.</summary>
    public int Frame { get; private set; }
    /// <summary>Pixel-exact runs hold the focus band at its first pulse value, as the Deck Menu does.</summary>
    public bool HoldPulse { get; set; }
    public int PulseFrame => HoldPulse ? 0 : Frame;
    /// <summary>The row whose PC notice is open (its popup and row index), or null.</summary>
    public (string Popup, int Row)? Notice { get; private set; }
    /// <summary>Count shown by the Run notice while it is open.</summary>
    public int? RunCount { get; private set; }
    /// <summary>Grid selections not yet run, per filtering panel.</summary>
    public IReadOnlyDictionary<DeckEditNarrowPanel, HashSet<int>> Pending => pending;
    /// <summary>Music row picked last (audio is a separate phase: no sound changes).</summary>
    public int Music { get; private set; }

    /// <summary>Everything the menu layer draws from (stack, focus band alpha, notices, pending toggles), for the frame cache.</summary>
    public string Signature => !Open ? "" : string.Join(';', stack.Select(l => $"{l.Id}:{l.Focus}:{l.OnRun}")) +
        $"|{profile.Pulse.AlphaAt(PulseFrame)}|{Notice}|{RunCount}|" + string.Join(',', pending.Select(p => string.Join('.', p.Value.Order())));

    public static DeckEditNarrowPanel? PanelOf(string gridId) => Array.IndexOf(NarrowGrids, gridId) is int i and >= 0 and < 7 ? (DeckEditNarrowPanel)i : null;

    public bool IsDisabled(string popup, int row, DeckEditSession session) => popup == "main" && row switch
    {
        MainDestiny => true,
        MainResetNarrow => !session.Query.Narrowed,
        MainUndo => !session.CanUndo,
        _ => false
    };

    public void OpenMain() { stack.Clear(); Push("main", 0); }
    public void CloseAll() { stack.Clear(); Notice = null; RunCount = null; }

    /// <summary>Pointer focus on a row or cell of the top popup, or on its Run button. Sort previews the pointed key as moving would.</summary>
    public void Point(int focus, bool onRun, DeckEditSession session)
    {
        if (Top is not { } top || Notice is not null || RunCount is not null || focus < 0 || focus >= profile[top.Id].Count) return;
        if (top.Focus == focus && top.OnRun == onRun) return;
        SetTop(top with { Focus = focus, OnRun = onRun && profile[top.Id].Run is not null });
        if (top.Id == "sort" && !onRun) { session.Query.SortKey = (DeckEditSortKey)focus; session.ListChanged(); }
    }

    private void Push(string id, int focus) { stack.Add(new(id, focus)); Frame = 0; }
    private void Pop() { stack.RemoveAt(stack.Count - 1); Frame = 0; }
    private void SetTop(DeckEditMenuLevel level) => stack[^1] = level;

    /// <summary>One frame of input while a menu is open. PC confirm is PSP Cross (original Circle), PC back PSP Circle (original Cross).</summary>
    public void Handle(PspInputFrame input, DeckEditSession session)
    {
        Frame++;
        bool confirm = input.IsPressed(PspAction.Cross), back = input.IsPressed(PspAction.Circle);
        if (Notice is not null) { if (confirm || back) Notice = null; return; }
        if (RunCount is not null)
        {
            if (confirm || back) RunCount = null;
            return;
        }
        var top = Top!;
        var popup = profile[top.Id];
        if (back)
        {
            if (top.Id == "sort") { (session.Query.SortKey, session.Query.Inverse) = savedSort; session.ListChanged(); }
            Pop();
            return;
        }
        if (popup.IsGrid) HandleGrid(input, confirm, session, top, popup);
        else HandleList(input, confirm, session, top, popup);
    }

    private void HandleList(PspInputFrame input, bool confirm, DeckEditSession session, DeckEditMenuLevel top, DeckEditMenuPopup popup)
    {
        int count = popup.Count;
        if (input.IsPressed(PspAction.Down) || input.IsPressed(PspAction.Up))
        {
            int next = (top.Focus + (input.IsPressed(PspAction.Down) ? 1 : count - 1)) % count;
            SetTop(top with { Focus = next });
            if (top.Id == "sort") { session.Query.SortKey = (DeckEditSortKey)next; session.ListChanged(); }
            return;
        }
        if (top.Id == "sort" && (input.IsPressed(PspAction.Right) || confirm)) { Push("sort-order", session.Query.Inverse ? 1 : 0); return; }
        if (top.Id == "sort-order" && input.IsPressed(PspAction.Left)) { Pop(); return; }
        if (!confirm || IsDisabled(top.Id, top.Focus, session)) return;
        Pick(top.Id, top.Focus, session);
    }

    private void Pick(string id, int row, DeckEditSession session)
    {
        switch (id, row)
        {
            case ("main", MainLabel): session.ToggleLabel(); break;
            case ("main", 3): Push("search", 0); break;
            case ("main", 4): Push("deck-recipe", 0); break;
            case ("main", 5): Push("layout", 0); break;
            case ("main", 6): Push("music", Music); break;
            case ("main", MainResetNarrow): session.Query.ResetNarrow(); session.ListChanged(); CloseAll(); break;
            case ("main", MainUndo): session.Undo(); CloseAll(); break;
            case ("search", 0):
                savedSort = (session.Query.SortKey, session.Query.Inverse);
                Push("sort", (int)(session.Query.SortKey ?? DeckEditSortKey.Name));
                if (session.Query.SortKey is null) { session.Query.SortKey = DeckEditSortKey.Name; session.ListChanged(); }
                break;
            case ("search", 1):
                foreach (var (panel, cells) in session.Query.Narrow) { pending[panel].Clear(); pending[panel].UnionWith(cells); }
                Push("narrow", 0);
                break;
            case ("search", 3):
                if (session.Focused is { } entry) { session.Query.RelatedTo = entry.Card; session.ListChanged(); }
                CloseAll();
                break;
            case ("search", 4): Push("label-display", (int)session.Query.LabelDisplay); break;
            case ("narrow", _): Push(NarrowGrids[row], 0); break;
            case ("sort-order", _):
                session.Query.Inverse = row == 1;
                session.ListChanged();
                CloseAll();
                break;
            case ("deck-recipe", 3): session.ResetDeck(); CloseAll(); break;
            case ("deck-recipe", 4): session.EmptyDeck(); CloseAll(); break;
            case ("music", _): Music = row; Pop(); break;
            case ("label-display", _): session.Query.LabelDisplay = (DeckEditLabelDisplay)row; session.ListChanged(); CloseAll(); break;
            default: Notice = (id, row); break; // Card Details, Text Search, recipe rows, Change Deck Name: not recreated on the PC
        }
    }

    /// <summary>Grid rows are the cells sharing a top edge, left to right.</summary>
    public static int[][] GridRows(DeckEditMenuPopup popup) => popup.Cells!
        .Select((c, i) => (c, i)).GroupBy(x => x.c.Box[1]).OrderBy(g => g.Key)
        .Select(g => g.OrderBy(x => x.c.Box[0]).Select(x => x.i).ToArray()).ToArray();

    private void HandleGrid(PspInputFrame input, bool confirm, DeckEditSession session, DeckEditMenuLevel top, DeckEditMenuPopup popup)
    {
        var rows = GridRows(popup);
        int r = Array.FindIndex(rows, x => x.Contains(top.Focus)), c = Array.IndexOf(rows[r], top.Focus);
        bool hasRun = popup.Run is not null;
        if (top.OnRun)
        {
            if (input.IsPressed(PspAction.Up)) SetTop(top with { Focus = rows[^1][Math.Min(c, rows[^1].Length - 1)], OnRun = false });
            else if (input.IsPressed(PspAction.Down)) SetTop(top with { Focus = rows[0][Math.Min(c, rows[0].Length - 1)], OnRun = false });
            else if (confirm) Run(session);
            return;
        }
        if (input.IsPressed(PspAction.Right) || input.IsPressed(PspAction.Left))
        {
            int n = rows[r].Length;
            SetTop(top with { Focus = rows[r][(c + (input.IsPressed(PspAction.Right) ? 1 : n - 1)) % n] });
        }
        else if (input.IsPressed(PspAction.Down))
        {
            if (r + 1 < rows.Length) SetTop(top with { Focus = rows[r + 1][Math.Min(c, rows[r + 1].Length - 1)] });
            else if (hasRun) SetTop(top with { OnRun = true });
            else SetTop(top with { Focus = rows[0][Math.Min(c, rows[0].Length - 1)] });
        }
        else if (input.IsPressed(PspAction.Up))
        {
            if (r > 0) SetTop(top with { Focus = rows[r - 1][Math.Min(c, rows[r - 1].Length - 1)] });
            else if (hasRun) SetTop(top with { OnRun = true });
            else SetTop(top with { Focus = rows[^1][Math.Min(c, rows[^1].Length - 1)] });
        }
        else if (confirm)
        {
            if (PanelOf(top.Id) is { } panel)
            {
                if (!pending[panel].Add(top.Focus)) pending[panel].Remove(top.Focus);
            }
            else Notice = (top.Id, top.Focus); // Key Search (no genre data) and other layouts are not recreated on the PC
        }
    }

    /// <summary>Observed (run-21-notice): Run closes every menu and narrows the list, then shows the count over it until OK.</summary>
    private void Run(DeckEditSession session)
    {
        int count = session.CountMatching(pending);
        session.Query.ApplyNarrow(pending);
        session.ListChanged();
        CloseAll();
        RunCount = count;
    }

    public bool IsSelected(string gridId, int cell) => PanelOf(gridId) is { } panel && pending[panel].Contains(cell);
}
