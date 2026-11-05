using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Import;
using TagForceSpecial.PC.Setup;
using TagForceSpecial.PC.UI;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private DiagnosticLayout? diagnosticLayout;
    private IReadOnlyList<AssetDiagnostic> assetDiagnostics = [];
    private sealed record PreviewPixels(string AssetKey, int Width, int Height, byte[] Rgba, bool Opaque);
    private sealed record DiagnosticSnapshot(IReadOnlyList<AssetDiagnostic> Rows, PreviewPixels? Preview);
    private Task<DiagnosticSnapshot>? diagnosticTask;
    private Texture2D? diagnosticPreview;
    private BlendState? diagnosticPreviewBlend;
    private int diagnosticScreenIndex, diagnosticAssetIndex;
    private static readonly string[] DiagnosticScreens = ["title", "main-menu", "deck-editor", "duel", "story", "unassigned"];

    private void OpenAssetDiagnostics()
    {
        diagnosticLayout ??= new(Path.Combine(AppContext.BaseDirectory, "UI/Gum/Screens/AssetDiagnostics.gusx"));
        if (diagnosticTask is not null) { screen = ScreenId.AssetDiagnostics; return; }
        var storage = setup?.Storage;
        diagnosticTask = Task.Run(() => InspectPrivatePresentation(storage, options.AssetPreview));
        diagnosticAssetIndex = 0;
        screen = ScreenId.AssetDiagnostics;
    }

    private static DiagnosticSnapshot InspectPrivatePresentation(PrivateStorage? storage, string? previewKey)
    {
        try
        {
            var registry = storage is null ? null : new PresentationCacheStore(storage).OpenActive();
            var requirements = AssetDiagnostics.DefaultRequirements.ToList();
            if (registry is not null)
                foreach (string key in registry.Entries.Keys.Except(requirements.Select(r => r.AssetKey), StringComparer.Ordinal))
                {
                    // Screen usage is not established by a filename or a semantic-key prefix.
                    requirements.Add(new("unassigned", key));
                }
            PreviewPixels? preview = null;
            if (previewKey is not null && registry is not null && registry.Entries.ContainsKey(previewKey) &&
                registry.VerifiedReferenceScope(previewKey) is not null)
            {
                var resource = registry.Resolve(previewKey);
                if (resource.Manifest.Output.Width is > 0 and <= 256 && resource.Manifest.Output.Height is > 0 and <= 256)
                    preview = new(previewKey, resource.Manifest.Output.Width.Value, resource.Manifest.Output.Height.Value,
                        resource.Content, PresentationReferenceCases.Cases.Single(c => c.AssetKey == previewKey).NativeCard);
            }
            return new(AssetDiagnostics.Inspect(registry, requirements), preview);
        }
        catch (Exception error) when (SetupStore.Recoverable(error))
        { return new(AssetDiagnostics.Inspect(null, AssetDiagnostics.DefaultRequirements, invalidPackage: true), null); }
    }

    private void UpdateAssetDiagnostics(GuiButtons buttons)
    {
        if (diagnosticTask is { IsCompleted: true })
        {
            var snapshot = diagnosticTask.GetAwaiter().GetResult(); diagnosticTask = null;
            assetDiagnostics = snapshot.Rows;
            graphicsThread.Check();
            diagnosticPreview?.Dispose(); diagnosticPreview = null;
            diagnosticPreviewBlend?.Dispose(); diagnosticPreviewBlend = null;
            if (snapshot.Preview is { } data)
            {
                diagnosticPreview = new(GraphicsDevice, data.Width, data.Height);
                diagnosticPreview.SetData(data.Rgba);
                // Native JPEG CSC alpha is unused/zero. Preserve source bytes and use an explicit opaque material.
                diagnosticPreviewBlend = new()
                {
                    ColorSourceBlend = data.Opaque ? Blend.One : Blend.SourceAlpha,
                    ColorDestinationBlend = data.Opaque ? Blend.Zero : Blend.InverseSourceAlpha,
                    AlphaSourceBlend = data.Opaque ? Blend.Zero : Blend.One,
                    AlphaDestinationBlend = data.Opaque ? Blend.One : Blend.InverseSourceAlpha
                };
            }
        }
        if (buttons.Back) { savedFocus[ScreenId.Setup] = 7; screen = ScreenId.Setup; return; }
        int delta = buttons.Right ? 1 : buttons.Left ? -1 : 0;
        if (delta != 0)
        { diagnosticScreenIndex = (diagnosticScreenIndex + delta + DiagnosticScreens.Length) % DiagnosticScreens.Length; diagnosticAssetIndex = 0; }
        int count = assetDiagnostics.Count(r => r.Screen == DiagnosticScreens[diagnosticScreenIndex]);
        if (count > 0)
            diagnosticAssetIndex = (diagnosticAssetIndex + (buttons.Down ? 1 : buttons.Up ? -1 : 0) + count) % count;
        if (buttons.Confirm) OpenAssetDiagnostics();
    }

    private void DrawAssetDiagnostics()
    {
        var layout = diagnosticLayout!;
        var gold = new Color(238, 196, 104); var muted = new Color(143, 162, 187);
        if (options.AssetPreview is { } previewKey)
        {
            renderer.Text("SOURCE PIXEL PREVIEW", 8, 12, gold);
            renderer.Wrapped(previewKey, 8, 40, 32, 4, Color.White);
            renderer.Wrapped(diagnosticTask is not null ? "Checking private source..." : diagnosticPreview is null ?
                "No verified preview is available." : "Native texture pixels. Original screen layout unverified.", 8, 90, 32, 5, muted);
            renderer.Wrapped("Native alpha bytes are preserved. Card art uses an opaque display material.", 8, 154, 32, 5, muted);
            renderer.Text("ENTER: RECHECK  ESC: BACK", 8, 241);
            return;
        }
        void Label(string anchor, string text, Color color)
        {
            var rect = layout.Bounds(anchor);
            renderer.Wrapped(text, rect.X, rect.Y, rect.Width / 6, rect.Height / 9, color);
        }
        Label("Title", layout.Text("Title"), gold);
        if (diagnosticTask is not null)
        {
            Label("Source", "CHECKING PRIVATE PACKAGE AND SOURCE ATTRIBUTION...", muted);
            Label("Footer", "ESC/B: RETURN TO SETUP", Color.White);
            return;
        }
        string selected = DiagnosticScreens[diagnosticScreenIndex];
        var rows = assetDiagnostics.Where(r => r.Screen == selected).ToArray();
        Label("ScreenLabel", "SCREEN: " + selected.ToUpperInvariant(), Color.White);
        Label("Summary", string.Join(" / ", AssetDiagnostics.Statuses.Select(s => s.ToUpperInvariant() + " " + rows.Count(r => r.Status == s))), muted);
        var area = layout.Bounds("Rows");
        int first = diagnosticAssetIndex / 3 * 3;
        for (int i = first; i < Math.Min(rows.Length, first + 3); i++)
        {
            int y = area.Y + (i - first) * 20;
            string key = rows[i].AssetKey.Length > 70 ? rows[i].AssetKey[..67] + "..." : rows[i].AssetKey;
            renderer.Text((i == diagnosticAssetIndex ? "> " : "  ") + key, area.X, y,
                i == diagnosticAssetIndex ? gold : Color.White);
            renderer.Text("  " + rows[i].Status, area.X, y + 9, muted);
        }
        if (rows.Length > 0)
        {
            var item = rows[Math.Min(diagnosticAssetIndex, rows.Length - 1)];
            Label("Source", "SOURCE: " + item.Source + " " + item.Detail, muted);
        }
        Label("Footer", layout.Text("Footer"), Color.White);
    }

    private void DrawDiagnosticPreviewTexture()
    {
        if (screen != ScreenId.AssetDiagnostics || options.AssetPreview is null || diagnosticTask is not null || diagnosticPreview is null) return;
        graphicsThread.Check();
        presenter.Begin(blendState: diagnosticPreviewBlend, samplerState: SamplerState.PointClamp);
        // Authored tooling placement at native size, not reconstructed original UI geometry.
        presenter.Draw(diagnosticPreview, new Rectangle(212, 8, diagnosticPreview.Width, diagnosticPreview.Height), Color.White);
        presenter.End();
    }
}
