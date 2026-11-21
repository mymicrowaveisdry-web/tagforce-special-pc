using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

public enum FieldObjectKind { Hand, Monster, SpellTrap, Pendulum, FieldSpell, Graveyard, Banished, Deck, Extra }

/// <summary>One focusable board object: its row, kind, owner, zone or hand index, world x and the card on it (null when empty).</summary>
public readonly record struct FieldObject(int Row, FieldObjectKind Kind, bool Own, int Index, float X, CardView? Card)
{
    /// <summary>Hand cards are identified by instance (the hand reorders as cards leave); everything else by kind, owner and index.</summary>
    public bool Same(FieldObject other) => Kind == other.Kind && Own == other.Own &&
        (Kind == FieldObjectKind.Hand ? Card?.InstanceId is { } id ? id == other.Card?.InstanceId : Index == other.Index : Index == other.Index);
}

/// <summary>Measured Duel Field focus graph (13.2): six rows cycling bottom to top (own hand row, own Spell/Trap, own monsters, opponent
/// monsters, opponent Spell/Trap, opponent hand row); objects ordered left to right on screen; Left/Right wrap within a row; Up/Down keep
/// the column by nearest world x. Empty slots and pads take focus.</summary>
public static class DuelFieldRows
{
    public const int OwnBottom = 0, OwnSpellTrap = 1, OwnMonster = 2, OpponentMonster = 3, OpponentSpellTrap = 4, OpponentTop = 5, Count = 6;

    public static IReadOnlyList<FieldObject>[] Build(DuelView view)
    {
        var self = view.Self; var opponent = view.Opponent;
        var rows = new List<FieldObject>[Count];
        rows[OwnBottom] = [new(OwnBottom, FieldObjectKind.Extra, true, 0, -DuelLayoutProfile.PileX, null),
            .. self.Hand.Select((c, i) => new FieldObject(OwnBottom, FieldObjectKind.Hand, true, i, DuelLayoutProfile.HandWorld(i, self.Hand.Count, false).Translation.X, c)),
            new(OwnBottom, FieldObjectKind.Deck, true, 0, DuelLayoutProfile.PileX, null)];
        rows[OwnSpellTrap] = SpellTrapRow(OwnSpellTrap, self, true);
        rows[OwnMonster] = [new(OwnMonster, FieldObjectKind.FieldSpell, true, 0, -DuelLayoutProfile.PileX, self.FieldSpell),
            .. Slots(OwnMonster, FieldObjectKind.Monster, self.Monsters, true),
            new(OwnMonster, FieldObjectKind.Graveyard, true, 0, DuelLayoutProfile.PileX, Top(self.Graveyard)),
            new(OwnMonster, FieldObjectKind.Banished, true, 0, DuelLayoutProfile.BanishedX, null)];
        rows[OpponentMonster] = [new(OpponentMonster, FieldObjectKind.Banished, false, 0, -DuelLayoutProfile.BanishedX, null),
            new(OpponentMonster, FieldObjectKind.Graveyard, false, 0, -DuelLayoutProfile.PileX, Top(opponent.Graveyard)),
            .. Slots(OpponentMonster, FieldObjectKind.Monster, opponent.Monsters, false),
            new(OpponentMonster, FieldObjectKind.FieldSpell, false, 0, DuelLayoutProfile.PileX, opponent.FieldSpell)];
        rows[OpponentSpellTrap] = SpellTrapRow(OpponentSpellTrap, opponent, false);
        rows[OpponentTop] = [new(OpponentTop, FieldObjectKind.Deck, false, 0, -DuelLayoutProfile.PileX, null),
            .. opponent.Hand.Select((c, i) => new FieldObject(OpponentTop, FieldObjectKind.Hand, false, i,
                DuelLayoutProfile.OpponentHandWorld(i, opponent.Hand.Count).Translation.X, c)),
            new(OpponentTop, FieldObjectKind.Extra, false, 0, DuelLayoutProfile.PileX, null)];
        return rows;
    }

    private static List<FieldObject> SpellTrapRow(int row, PlayerView owner, bool own) =>
        [new(row, FieldObjectKind.Pendulum, own, 0, -DuelLayoutProfile.PendulumX, owner.Pendulum[0]),
         .. Slots(row, FieldObjectKind.SpellTrap, owner.SpellTraps, own),
         new(row, FieldObjectKind.Pendulum, own, 1, DuelLayoutProfile.PendulumX, owner.Pendulum[1])];

    private static IEnumerable<FieldObject> Slots(int row, FieldObjectKind kind, IReadOnlyList<CardView?> cards, bool own) =>
        cards.Select((c, i) => new FieldObject(row, kind, own, i, DuelLayoutProfile.FirstSlotX + DuelLayoutProfile.SlotPitch * i, c));

    private static CardView? Top(IReadOnlyList<CardView> pile) => pile.Count > 0 ? pile[^1] : null;
}

/// <summary>The focused board object and its measured movement.</summary>
public sealed class DuelFieldFocus
{
    private IReadOnlyList<FieldObject>[] rows = new IReadOnlyList<FieldObject>[DuelFieldRows.Count];
    public FieldObject Current { get; private set; }
    public bool Ready { get; private set; }

    /// <summary>Rebuilds the rows from a new view, keeping the same object when it still exists, else the nearest in its row.
    /// Measured start: the rightmost hand card (the Deck when the hand is empty).</summary>
    public void Sync(DuelView view)
    {
        rows = DuelFieldRows.Build(view);
        if (!Ready)
        {
            var bottom = rows[DuelFieldRows.OwnBottom];
            Current = bottom.Count > 2 ? bottom[^2] : bottom[^1];
            Ready = true;
            return;
        }
        var row = rows[Current.Row];
        var current = Current;
        Current = row.Any(o => o.Same(current)) ? row.First(o => o.Same(current)) : Nearest(row, Current.X);
    }

    public void FocusOn(FieldObject target) { if (rows[target.Row].Any(o => o.Same(target))) Current = rows[target.Row].First(o => o.Same(target)); }

    public IReadOnlyList<FieldObject> Row(int row) => rows[row];

    /// <summary>Left/Right: neighbour in the row with wrap. Up/Down: next row in the cycle, nearest world x.</summary>
    public void Move(int dx, int dy)
    {
        if (dx != 0)
        {
            var row = rows[Current.Row];
            int index = IndexOf(row, Current);
            Current = row[((index + dx) % row.Count + row.Count) % row.Count];
        }
        if (dy != 0)
        {
            int next = ((Current.Row + dy) % DuelFieldRows.Count + DuelFieldRows.Count) % DuelFieldRows.Count;
            Current = Nearest(rows[next], Current.X);
        }
    }

    private static int IndexOf(IReadOnlyList<FieldObject> row, FieldObject target)
    {
        for (int i = 0; i < row.Count; i++) if (row[i].Same(target)) return i;
        return 0;
    }

    /// <summary>Nearest by world x; ties (not observed) go to the leftmost.</summary>
    public static FieldObject Nearest(IReadOnlyList<FieldObject> row, float x) => row.OrderBy(o => MathF.Abs(o.X - x)).ThenBy(o => o.X).First();
}
