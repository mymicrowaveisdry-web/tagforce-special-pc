using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Deck Edit core screen (list layout) drawn into one retained layer from the measured profile. Static draws replay as recorded;
/// rows repeat the row-0 template with per-card pieces from the learned tables; text uses the PSP font at measured anchors and source strings.
/// The spinning background is frozen at its recorded instant (its animation is unmeasured).</summary>
public sealed partial class TitleFlowVisuals
{
    public DeckEditProfile DeckEditProfile { get; } = DeckEditProfile.LoadEmbedded();
    public RenderTarget2D DeckEditLayer { get; private set; } = null!;
    /// <summary>Inferred lookups used by the last rendered frame (never observed on a recorded original row).</summary>
    public List<string> DeckEditInferred { get; } = [];
    private OriginalScreenAssetResolver? deckAssets;
    private byte[] deckArchive = [];

    private void InitializeDeckEdit(OriginalScreenAssetResolver assets)
    {
        deckAssets = assets;
        deckArchive = assets.ResolveBytes(DeckEditResources.ArchiveKey);
        // Pinned keys must verify; the named supplements are decoded but not yet seen on a recorded screen.
        var supplements = DeckEditResources.Supplements.Select(s => s.AssetKey).ToHashSet(StringComparer.Ordinal);
        // EBOOT dialog textures are already decoded from the stored EBOOT by the title flow.
        foreach (string key in DeckEditProfile.AssetKeys().Where(k => !textures.ContainsKey(k)))
            textures.Add(key, (supplements.Contains(key) ? assets.ResolveUnverified(key) : assets.Resolve(key)).Texture);
        DeckEditLayer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
    }

    private string DeckText(DeckEditTextSource source) => DeckText(source, 0, null);
    private string DeckText(DeckEditTextSource source, int start, int? chars)
    {
        byte[] blob = source.Source switch { "deck" => deckArchive, "eboot" => eboot, "title-menu" => titleMenu, _ => throw new InvalidDataException("Unknown text source.") };
        if (source.Offset < 0 || source.Offset >= blob.Length) throw new InvalidDataException("Invalid Deck Edit text reference.");
        int end = source.Offset;
        while (end + 1 < blob.Length && (blob[end] != 0 || blob[end + 1] != 0)) end += 2;
        string message = TitleFlowText.Normalize(System.Text.Encoding.Unicode.GetString(blob, source.Offset, end - source.Offset));
        if (start > message.Length || chars is { } c && start + c > message.Length) throw new InvalidDataException("Deck Edit text reference exceeds its message.");
        return chars is { } n ? message.Substring(start, n) : message[start..];
    }

    private Texture2D? Thumbnail(uint cardId)
    {
        string key = CardArtworkProvider.ThumbnailKey(cardId);
        if (textures.TryGetValue(key, out var existing)) return existing;
        // Thumbnails are decoded from the disc slot but not pixel-verified; a card without one shows no art.
        try { var texture = deckAssets!.ResolveUnverified(key).Texture; textures[key] = texture; return texture; }
        catch (Exception e) when (e is KeyNotFoundException or InvalidDataException) { return null; }
    }

    private static FlowDraw Shifted(FlowDraw draw, float dx, float dy) =>
        dx == 0 && dy == 0 ? draw : draw with { Points = Array.ConvertAll(draw.Points, p => new[] { p[0] + dx, p[1] + dy }) };

    /// <summary>The same quad with its UV rectangle replaced; vertices keep their corner roles.</summary>
    private static FlowDraw WithUv(FlowDraw draw, float[] rect, string? key = null)
    {
        float minU = draw.Uv.Min(u => u[0]), maxU = draw.Uv.Max(u => u[0]), minV = draw.Uv.Min(u => u[1]), maxV = draw.Uv.Max(u => u[1]);
        float[] Map(float[] uv) => [maxU > minU ? rect[0] + (uv[0] - minU) / (maxU - minU) * (rect[2] - rect[0]) : rect[0],
            maxV > minV ? rect[1] + (uv[1] - minV) / (maxV - minV) * (rect[3] - rect[1]) : rect[1]];
        return draw with { AssetKey = key ?? draw.AssetKey, Uv = Array.ConvertAll(draw.Uv, Map) };
    }

    private void DrawField(DeckEditField field, string value, float dy = 0)
    {
        if (value.Length == 0) return;
        float[]? positions = field.Align == "right" && field.Pitch is { } pitch
            ? Enumerable.Range(0, value.Length).Select(i => field.X - (value.Length - 1 - i) * pitch).ToArray() : null;
        DrawLine(value, positions?[0] ?? field.X, field.Y + dy, field.ScaleX, field.ScaleY, field.Color, field.ShadowAlpha, positions);
    }

    private float TextWidth(string value, float scaleX)
    {
        if (font is null) return value.Length * CellSize * scaleX / 2;
        return value.Sum(c => font.Glyph(c)?.AdvanceH26_6 / 64f * scaleX ?? CellSize * scaleX / 2);
    }

    /// <summary>Greedy word wrap at the measured panel width, using the PSP font's own advances.</summary>
    private List<string> Wrap(string text, float width, float scaleX)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && TextWidth(next, scaleX) > width) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        return lines;
    }

    private static string Stat(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private void Infer(string what) { if (!DeckEditInferred.Contains(what)) DeckEditInferred.Add(what); }

    private float[]? Lookup(string table, uint key, string what)
    {
        if (DeckEditProfile.Uv(table, key) is not { } found) return null;
        if (found.Inferred) Infer($"{what} {key}");
        return found.Value;
    }

    private void DrawRow(DeckEditEntry entry, int row)
    {
        var p = DeckEditProfile;
        var card = entry.Card;
        uint kind = card.Codes?.RawKind ?? 0;
        float dy = row * p.RowPitch;
        if (p.PanelKey(kind) is { } panel)
        {
            if (panel.Inferred) Infer($"row panel for kind {kind}");
            foreach (string part in new[] { "panel-left", "panel-right" }) DrawMeasured(Shifted(p.Row[part] with { AssetKey = panel.Value }, 0, dy));
        }
        if (Lookup("cardFrameByKind", kind, "card frame cell for kind") is { } frame)
            foreach (string part in new[] { "shadow", "card-frame" }) DrawMeasured(Shifted(WithUv(p.Row[part], frame), 0, dy));
        if (card.SourceRow is { } source && p.GetMiniImage(source.InternalIndex) is { } mini)
            DrawMeasured(Shifted(WithUv(p.Row["mini"], mini.Uv, mini.AssetKey), 0, dy));
        if (card.Codes is { } codes)
        {
            if (Lookup("attributeIcon", codes.Attribute, "attribute icon") is { } attribute) DrawMeasured(Shifted(WithUv(p.Row["attribute"], attribute), 0, dy));
            if (Lookup("typeIcon", codes.Type, "type icon") is { } type) DrawMeasured(Shifted(WithUv(p.Row["type"], type), 0, dy));
            if (Lookup("levelIcon", kind, "level icon for kind") is { } level) DrawMeasured(Shifted(WithUv(p.Row["level"], level), 0, dy));
            if (Lookup("columnIcon0", kind, "deck column icon for kind") is { } column0) DrawMeasured(Shifted(WithUv(p.Row["column0"], column0), 0, dy));
        }
        if (p.Uv("columnIcon1", "all") is { } column1) DrawMeasured(Shifted(WithUv(p.Row["column1"], column1.Value), 0, dy));
        if (p.Uv("columnIcon2", "all") is { } column2) DrawMeasured(Shifted(WithUv(p.Row["column2"], column2.Value), 0, dy));
        var f = p.Fields;
        float textDy = p.RowTop + dy;
        DrawField(f.RowName, card.Name, textDy);
        DrawField(f.RowOwned, DeckText(f.RowOwned.Source!) + entry.Owned.ToString(CultureInfo.InvariantCulture), textDy);
        // Observed: Spell and Trap rows carry no ATK/DEF.
        if (card.Codes?.RawKind is not (13 or 14) && card.Statistics is { } stats)
        {
            DrawField(f.RowAttackLabel, DeckText(f.RowAttackLabel.Source!), textDy);
            DrawField(f.RowAttack, Stat(stats.Attack), textDy);
            DrawField(f.RowSlash, DeckText(f.RowSlash.Source!), textDy);
            DrawField(f.RowDefenseLabel, DeckText(f.RowDefenseLabel.Source!), textDy);
            DrawField(f.RowDefense, Stat(stats.Defense), textDy);
        }
        int[] columns = [entry.InMainOrExtra, entry.InSide, entry.InTrunk];
        for (int i = 0; i < 3; i++) DrawField(f.RowColumns[i], columns[i].ToString(CultureInfo.InvariantCulture), textDy);
    }

    private void DrawPreview(CardDefinition card)
    {
        var p = DeckEditProfile;
        uint kind = card.Codes?.RawKind ?? 0;
        if (p.FramePalette(kind) is { } palette)
        {
            if (palette.Inferred) Infer($"preview frame palette for kind {kind}");
            string key = $"deck.all.frame.p{palette.Value:00}";
            if (textures.ContainsKey(key)) DrawMeasured(p.Preview.Frame with { AssetKey = key });
        }
        if (Thumbnail(card.GameCardId) is not null)
        {
            string key = CardArtworkProvider.ThumbnailKey(card.GameCardId);
            DrawMeasured(p.Preview.Art with { AssetKey = key });
            DrawMeasured(p.Preview.Name with { AssetKey = key });
        }
        foreach (var draw in p.Preview.Statics) DrawMeasured(draw);
        var f = p.Fields;
        if (card.Codes?.RawKind is not (13 or 14) && card.Statistics is { } stats)
        {
            string slash = DeckText(f.PreviewSlash);
            DrawField(f.PreviewAttackLabel, DeckText(f.PreviewAttackLabel.Source!) + slash);
            DrawField(f.PreviewAttack, Stat(stats.Attack));
            DrawField(f.PreviewDefenseLabel, DeckText(f.PreviewDefenseLabel.Source!) + slash);
            DrawField(f.PreviewDefense, Stat(stats.Defense));
        }
        // Name (cyan) then card text (white). The bracketed type line (green) needs type names not yet mapped from source; omitted.
        var lines = p.Fields.PreviewLines;
        var wrapped = Wrap(card.Name, lines.Width, lines.ScaleX).Select(l => (l, lines.NameColor))
            .Concat(Wrap(card.Description, lines.Width, lines.ScaleX).Select(l => (l, lines.TextColor))).ToList();
        int fit = (int)((272 - lines.FirstY) / lines.LineHeight);
        for (int i = 0; i < Math.Min(fit, wrapped.Count); i++)
            DrawLine(wrapped[i].l, lines.X, lines.FirstY + i * lines.LineHeight, lines.ScaleX, lines.ScaleY, wrapped[i].Item2, lines.ShadowAlpha);
    }

    private void DrawTabs(DeckEditSession session, DeckEditTab active)
    {
        var p = DeckEditProfile;
        foreach (var draw in p.Tabs[DeckEditProfile.TabCycle[(int)active]]) DrawMeasured(draw);
        var t = p.TabText;
        // Tab label order on screen: Deck, EX, Side, Trunk.
        DeckEditTab[] onScreen = [DeckEditTab.Deck, DeckEditTab.Extra, DeckEditTab.Side, DeckEditTab.Trunk];
        for (int i = 0; i < 4; i++)
        {
            bool on = onScreen[i] == active;
            float y = on ? t.ActiveY : t.InactiveY;
            int color = on ? t.ActiveColor : t.InactiveColor;
            DrawLine(DeckText(t.Labels[i].Source), t.Labels[i].X, y, t.ScaleX, t.ScaleY, color, t.ShadowAlpha);
            string count = session.Count(onScreen[i]).ToString(CultureInfo.InvariantCulture);
            var positions = Enumerable.Range(0, count.Length).Select(k => t.CountRight[i] - (count.Length - 1 - k) * t.Pitch).ToArray();
            DrawLine(count, positions[0], y, t.ScaleX, t.ScaleY, color, t.ShadowAlpha, positions);
        }
    }

    public void RenderDeckEdit(DeckEditSession session, string deckName) => WithTarget(DeckEditLayer, () =>
    {
        var p = DeckEditProfile;
        DeckEditInferred.Clear(); FallbackLines.Clear();
        device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        foreach (var draw in p.Background.Concat(p.Window)) DrawMeasured(draw);
        bool moving = session.Prompt == DeckEditPrompt.Move;
        var shownTab = moving ? session.Destination : session.Tab;
        DrawTabs(session, shownTab);
        var f = p.Fields;
        DrawField(f.DeckName, deckName);
        DrawField(f.PossessionLabel, DeckText(f.PossessionLabel.Source!));
        DrawField(f.PossessionCount, session.CardTypesInPossession.ToString(CultureInfo.InvariantCulture));
        DrawField(f.Help, DeckText(f.Help.Source!));
        var entries = session.Entries(shownTab);
        int scroll = moving ? 0 : session.Scroll;
        for (int row = 0; row < p.VisibleRows && scroll + row < entries.Count; row++) DrawRow(entries[scroll + row], row);
        if (!moving) foreach (var draw in p.Cursor) DrawMeasured(Shifted(draw, 0, (session.Cursor - session.Scroll) * p.RowPitch));
        if (session.Focused is { } focused) DrawPreview(focused.Card);
        if (moving && session.Focused is { } picked) DrawMoveOverlay(picked.Card, session.Destination);
        if (session.Prompt == DeckEditPrompt.ExitQuestion) DrawExitDialog(session.PromptFocus);
    });

    private void DrawMoveOverlay(CardDefinition card, DeckEditTab destination)
    {
        var m = DeckEditProfile.MoveOverlay;
        DrawMeasured(m.Shade); DrawMeasured(m.Mask); DrawMeasured(m.Box);
        uint kind = card.Codes?.RawKind ?? 0;
        if (Lookup("cardFrameByKind", kind, "card frame cell for kind") is { } frame) DrawMeasured(WithUv(m.CardFrame, frame));
        if (card.SourceRow is { } source && DeckEditProfile.GetMiniImage(source.InternalIndex) is { } mini) DrawMeasured(WithUv(m.Mini, mini.Uv, mini.AssetKey));
        string label = DeckText(DeckEditProfile.TabText.Labels[destination switch { DeckEditTab.Deck => 0, DeckEditTab.Extra => 1, DeckEditTab.Side => 2, _ => 3 }].Source);
        string message = DeckText(DeckEditProfile.MovePrompt).Replace("%s", label, StringComparison.Ordinal);
        foreach (var line in m.Lines)
        {
            if (line.Start >= message.Length) continue;
            string part = line.Chars is { } n ? message.Substring(line.Start, Math.Min(n, message.Length - line.Start)) : message[line.Start..];
            DrawLine(part, line.X, line.Y, m.ScaleX, m.ScaleY, m.Color, m.ShadowAlpha);
        }
    }

    private void DrawExitDialog(int focus)
    {
        var d = DeckEditProfile.ExitDialog;
        DrawMeasured(d.Mask); DrawMeasured(d.Panel);
        // Recorded with OK focused: focus pieces at button 0, the plain piece at button 1.
        float focusX = d.Buttons[0].X, plainX = d.Buttons[1].X;
        for (int b = 0; b < 2; b++)
        {
            if (b == focus) foreach (var piece in d.Focus) DrawMeasured(Shifted(piece, d.Buttons[b].X - focusX, 0));
            else DrawMeasured(Shifted(d.Plain, d.Buttons[b].X - plainX, 0));
            var label = d.Buttons[b].Label;
            DrawLine(Source(label.Source, label.Offset, label.Start, label.Chars), d.Buttons[b].LabelX, d.LabelY, d.LabelScaleX, d.LabelScaleY, d.LabelColor, d.LabelShadow);
        }
        string message = DeckText(DeckEditProfile.ExitQuestion);
        foreach (var line in d.Lines)
            DrawLine(message.Substring(line.Start, Math.Min(line.Chars ?? message.Length, message.Length - line.Start)), line.X, line.Y, d.ScaleX, d.ScaleY, d.Color, d.ShadowAlpha);
        if (focus == 1 && d.CancelFocusInferred) Infer("exit dialog CANCEL focus pieces");
    }
}
