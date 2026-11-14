using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.Recipes;

namespace TagForceSpecial.PC.App.Screens.Title;

public enum RecipeSort { Id, Name, Date, CardCount }
public enum RecipeViewerModal { None, Sort, Manage, Notice }
public sealed record RecipeSelection(RecipeListKind Kind, int Slot, string Name, string SourceIdentity, string Verification);

/// <summary>Recipe focus and sorting are separate from the active deck; source recipes are never rewritten here.</summary>
public sealed class RecipeViewerSession(RecipeCatalog catalog)
{
    public RecipeCatalog Catalog { get; } = catalog;
    public RecipeListKind Kind { get; private set; }
    public RecipeSort Sort { get; private set; }
    public bool Inverse { get; private set; }
    public int Cursor { get; private set; }
    public int Scroll { get; private set; }
    public int CardCursor { get; private set; }
    public RecipeViewerModal Modal { get; private set; }
    public int ModalFocus { get; private set; }
    public bool OrderColumn { get; private set; }
    public bool PendingInverse { get; private set; }
    public string Notice { get; private set; } = "";
    public int Revision { get; private set; }
    public IReadOnlyList<RecipeRecord> Rows { get; private set; } = Order(catalog, RecipeListKind.User, RecipeSort.Id, false);
    public RecipeRecord? Focused => Rows.Count == 0 ? null : Rows[Cursor];
    public IReadOnlyList<uint> Cards => Focused is { } r ? r.Main.Concat(r.Extra).Concat(r.Side).ToArray() : [];
    public uint? FocusedCard => Cards.Count == 0 ? null : Cards[Math.Min(CardCursor, Cards.Count - 1)];
    public RecipeSelection? Selection => Focused is { } r ? new(r.Kind, r.Slot, r.Name, Catalog.Identity, "unverified") : null;

    private static IReadOnlyList<RecipeRecord> Order(RecipeCatalog source, RecipeListKind kind, RecipeSort sort, bool inverse)
    {
        var records = source.Records.Where(r => r.Kind == kind);
        IOrderedEnumerable<RecipeRecord> ordered = sort switch
        {
            RecipeSort.Name => records.OrderBy(r => r.Name, StringComparer.Ordinal),
            RecipeSort.Date => records.OrderBy(r => r.Updated),
            RecipeSort.CardCount => records.OrderBy(r => r.Main.Length + r.Extra.Length + r.Side.Length),
            _ => records.OrderBy(r => r.Slot)
        };
        var rows = ordered.ThenBy(r => r.Slot).ToArray();
        if (inverse) Array.Reverse(rows);
        return Array.AsReadOnly(rows);
    }

    public void SetOrder(RecipeSort sort, bool inverse)
    {
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        Sort = sort; Inverse = inverse;
        Rows = Order(Catalog, Kind, sort, inverse);
        Cursor = Scroll = CardCursor = 0;
        Revision++;
    }

    /// <summary>Returns true only when leaving; confirmation manages User recipes rather than replacing the active deck.</summary>
    public bool Handle(PspInputFrame input)
    {
        if (input.Pressed == PspAction.None) return false;
        bool confirm = input.IsPressed(PspAction.Cross), back = input.IsPressed(PspAction.Circle);
        int delta = input.IsPressed(PspAction.Down) ? 1 : input.IsPressed(PspAction.Up) ? -1 : 0;
        Revision++;
        if (Modal == RecipeViewerModal.Notice)
        {
            if (back || confirm) Modal = RecipeViewerModal.None;
            return false;
        }
        if (Modal == RecipeViewerModal.Manage)
        {
            if (back) { Modal = RecipeViewerModal.None; return false; }
            if (delta != 0) ModalFocus = (ModalFocus + delta + 2) % 2;
            if (confirm) { Notice = "Renaming and deleting original recipes are not available."; Modal = RecipeViewerModal.Notice; }
            return false;
        }
        if (Modal == RecipeViewerModal.Sort)
        {
            if (back) { Modal = RecipeViewerModal.None; return false; }
            if (input.IsPressed(PspAction.Right)) OrderColumn = true;
            if (input.IsPressed(PspAction.Left)) OrderColumn = false;
            if (delta != 0)
            {
                if (OrderColumn) PendingInverse = !PendingInverse;
                else ModalFocus = (ModalFocus + delta + 4) % 4;
            }
            if (confirm) { SetOrder((RecipeSort)ModalFocus, PendingInverse); Modal = RecipeViewerModal.None; }
            return false;
        }
        if (back) return true;
        if (input.IsPressed(PspAction.Square) || input.IsPressed(PspAction.L) || input.IsPressed(PspAction.R))
        {
            Kind = Kind == RecipeListKind.User ? RecipeListKind.Character : RecipeListKind.User;
            SetOrder(Sort, Inverse);
            return false;
        }
        if (input.IsPressed(PspAction.Triangle))
        {
            Modal = RecipeViewerModal.Sort; ModalFocus = (int)Sort; OrderColumn = false; PendingInverse = Inverse;
            return false;
        }
        if (delta != 0 && Rows.Count > 0)
        {
            Cursor = (Cursor + delta + Rows.Count) % Rows.Count;
            Scroll = Math.Clamp(Scroll, Math.Max(0, Cursor - Catalog.Layout.VisibleRows + 1), Cursor);
            CardCursor = 0;
        }
        if (confirm && Focused?.Kind == RecipeListKind.User) { Modal = RecipeViewerModal.Manage; ModalFocus = 0; }
        return false;
    }
}
