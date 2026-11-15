using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Cards;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Recipes;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed partial class TitleFlowVisuals
{
    public RenderTarget2D? RecipeLayer { get; private set; }

    private string? RecipeTexture(RecipeCatalog source, string key)
    {
        string local = "recipe/" + source.Identity + "/" + key;
        if (textures.ContainsKey(local)) return local;
        if (source.ReadTexture(key) is not { } item) return null;
        var texture = new Texture2D(device, item.Width, item.Height);
        texture.SetData(item.Pixels);
        textures.AddOwned(local, texture);
        return local;
    }

    private void DrawRecipe(RecipeCatalog source, FlowDraw draw, float dy = 0)
    {
        string? key = draw.AssetKey is { } asset ? RecipeTexture(source, asset) : null;
        if (draw.AssetKey is not null && key is null) throw new InvalidDataException("Recipe layout texture is missing.");
        DrawMeasured(Shifted(draw with { AssetKey = key }, 0, dy));
    }

    /// <summary>Measured recipe regions share Deck Edit's preview renderer, card catalog and private texture path.</summary>
    public void RenderRecipes(RecipeViewerSession session, CardCatalog catalog)
    {
        RecipeLayer ??= new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        WithTarget(RecipeLayer, () =>
        {
            FallbackLines.Clear(); DeckEditInferred.Clear();
            device.RasterizerState = RasterizerState.CullNone;
            device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Black, 1, 0);
            foreach (var draw in session.Catalog.Layout.Static) DrawRecipe(session.Catalog, draw);
            DrawLine("Recipe Viewer", 156, 1, .6f, .54f, 0xffffff, 128);
            DrawLine("unverified", 252, 5, .2f, .3f, 0xffff00, 128);
            DrawLine(session.Kind == RecipeListKind.User ? "User Recipes" : "Character Recipes", 166, 21, .5f, .45f, 0xffffff, 128);
            var l = session.Catalog.Layout;
            for (int row = 0; row < l.VisibleRows && session.Scroll + row < session.Rows.Count; row++)
            {
                var recipe = session.Rows[session.Scroll + row];
                float dy = row * l.RowPitch;
                foreach (var draw in l.Row) DrawRecipe(session.Catalog, draw, dy);
                string name = recipe.Name.Length <= 22 ? recipe.Name : recipe.Name[..21] + "…";
                DrawLine(name, l.NameX, l.NameY + dy, .5f, .45f, 0, 64);
                string detail = recipe.Kind == RecipeListKind.Character ? recipe.Author ?? "" :
                    recipe.Slot.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + " " + recipe.Updated.ToString("yy'/'MM'/'dd HH':'mm", System.Globalization.CultureInfo.InvariantCulture);
                DrawLine(detail, l.DateX, l.DateY + dy, .35f, .405f, 0, 64);
            }
            if (session.Focused is { } selected)
            {
                foreach (var draw in l.Focus) DrawRecipe(session.Catalog, draw, (session.Cursor - session.Scroll) * l.RowPitch);
                DrawRecipeSection(session, catalog, selected.Main, "Main Deck", 2, 18, 10);
                DrawRecipeSection(session, catalog, selected.Extra, "Extra Deck", 145, 162, 10);
                DrawRecipeSection(session, catalog, selected.Side, "Side Deck", 224, 241, 10);
                if (session.FocusedCard is { } id)
                {
                    string key = TagForceSpecial.Assets.Presentation.CardArtworkProvider.ThumbnailKey(id);
                    if (RecipeTexture(session.Catalog, key) is { } imported) textures.Alias(key, imported);
                    DrawPreview(catalog[id]);
                    bool supported = selected.Main.Concat(selected.Extra).Concat(selected.Side).All(ConfirmedCardPool.Contains);
                    DrawLine(supported ? "Confirmed card pool" : "Unsupported for PC duels", 1, 146, .32f, .36f, 0xffff00, 128);
                }
            }
            else
            {
                DrawLine("Recipes have not been imported", 152, 49, .35f, .405f, 0xffffff, 128);
                DrawLine("unverified", 152, 65, .4f, .45f, 0xffff00, 128);
            }
            if (session.Kind == RecipeListKind.Character)
                DrawLine("Imported subset; remaining recipes unverified", 152, 256, .28f, .36f, 0xffff00, 128);
            if (session.Modal != RecipeViewerModal.None) DrawRecipeModal(session);
        });
    }

    private void DrawRecipeSection(RecipeViewerSession session, CardCatalog catalog, uint[] cards, string title, float heading, float top, int columns)
    {
        DrawLine(title, 307, heading, .4f, .405f, 0xffffff, 128);
        DrawLine(cards.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), 460, heading, .4f, .405f, 0xffffff, 128);
        for (int i = 0; i < cards.Length; i++)
        {
            string key = TagForceSpecial.Assets.Presentation.CardArtworkProvider.ThumbnailKey(cards[i]);
            if (RecipeTexture(session.Catalog, key) is not { } texture) continue;
            int rows = Math.Max(1, (cards.Length + columns - 1) / columns);
            float available = heading < 100 ? 124 : heading < 200 ? 59 : 30;
            float pitch = Math.Min(30, available / rows);
            float x = 300 + i % columns * 17.5f, y = top + i / columns * pitch;
            float height = Math.Min(28, pitch - 1);
            if (DeckEditProfile.FrameKey(catalog[cards[i]].Codes?.RawKind ?? 0) is { } frameLookup &&
                DeckEditProfile.FrameGimPalette(frameLookup.Value) is { } palette &&
                RecipeTexture(session.Catalog, $"recipe.frame.{palette:00}") is { } frameKey)
            {
                var frame = DeckEditProfile.Preview.Frame;
                float minX = frame.Points.Min(p => p[0]), minY = frame.Points.Min(p => p[1]);
                float width = frame.Points.Max(p => p[0]) - minX, originalHeight = frame.Points.Max(p => p[1]) - minY;
                DrawMeasured(frame with { AssetKey = frameKey,
                    Points = Array.ConvertAll(frame.Points, p => new[] { x + (p[0] - minX) * 16.5f / width, y + (p[1] - minY) * height / originalHeight }) });
            }
            // Actual thumbnail art is square; the native tiny-card composite and limit badges remain unverified.
            float art = Math.Min(12.2f, height * .52f);
            DrawQuad(textures[texture], new(x + 2, y + height * .19f, x + 2 + art, y + height * .19f + art,
                0, 0, .875f, .625f, [255, 255, 255, 255], false), 0, 0, false);
        }
    }

    private void DrawRecipeModal(RecipeViewerSession session)
    {
        var source = session.Catalog;
        var l = source.Layout;
        DrawMeasured(DeckEditProfile.MoveOverlay.Shade);
        if (session.Modal == RecipeViewerModal.Sort)
        {
            foreach (var draw in l.SortPanel ?? []) DrawRecipe(source, draw);
            foreach (var draw in l.SortFocus ?? []) DrawRecipe(source, draw, session.ModalFocus * 19);
            foreach (var draw in l.OrderPanel ?? []) DrawRecipe(source, draw);
            foreach (var draw in l.OrderFocus ?? []) DrawRecipe(source, draw, session.PendingInverse ? 18 : 0);
            string[] sorts = ["Sort by ID", "Sort by Name", "Sort by Date", "Sort by Card Number"];
            for (int i = 0; i < sorts.Length; i++) DrawLine(sorts[i], 202, 100 + i * 19, .45f, .45f, 0, 64);
            DrawLine("Order Normal", 325, 85, .45f, .45f, 0, 64);
            DrawLine("Order Inverse", 325, 103, .45f, .45f, 0, 64);
        }
        else
        {
            foreach (var draw in l.Manage ?? []) DrawRecipe(source, draw);
            if (session.Modal == RecipeViewerModal.Manage)
            {
                foreach (var draw in l.ManageFocus ?? []) DrawRecipe(source, draw, session.ModalFocus * 19);
                DrawLine("Edit Recipe Name", 145, 119, .5f, .45f, 0, 64);
                DrawLine("Delete Recipe", 145, 138, .5f, .45f, 0, 64);
            }
            else
            {
                DrawLine("Original recipe changes unavailable", 128, 119, .35f, .405f, 0, 64);
                DrawLine("Source recipes remain unchanged", 128, 138, .35f, .405f, 0, 64);
            }
        }
    }
}
