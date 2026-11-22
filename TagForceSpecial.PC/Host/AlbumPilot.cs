using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.Import.Resources;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.App.Screens.Album;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>Phase 4 GPU pilot: the Card Album layer (captured 3D binder, depth EQUAL overlays, 2D parts) composed by the shared album scene
/// and rasterized on the RenderingDevice. Uses the reference all-obtained collection, as the MonoGame host's smoke did.</summary>
internal sealed class AlbumPilot : IDisposable
{
    private const string FontKey = "pgf.album";
    private readonly CardAlbumScene scene;
    private readonly IReadOnlyList<uint> order;
    private readonly CardAlbumCollection collection;
    public GeLayer Layer { get; }

    private AlbumPilot(CardAlbumScene scene, IReadOnlyList<uint> order, GeLayer layer)
    {
        this.scene = scene; this.order = order; Layer = layer;
        collection = CardAlbumCollection.ReferenceAllObtained(order);
    }

    /// <summary>Null when the import has no captured binder package (the album then shows its PC notice in the MonoGame host).</summary>
    public static AlbumPilot? TryCreate(PrivateStorage storage, ScreenAssets assets, FlowTextures textures, FlowTextSources text, CardCatalog catalog, byte[] font)
    {
        string binderRoot = storage.PathFor(AlbumReferenceStage.Folder);
        if (!File.Exists(System.IO.Path.Combine(binderRoot, CardAlbumBinder.FileName))) return null;
        var binder = CardAlbumBinder.Load(binderRoot);
        text.Add("album", assets.Bytes(CardAlbumResources.ArchiveKey));
        textures.Faces = new CardFaceSource(assets.Bytes(CardAlbumResources.FacesKey));
        var glyphs = new PgfGlyphAtlas(font, GameFonts.MenuSha256);
        var scene = new CardAlbumScene(CardAlbumProfile.LoadEmbedded(), binder, text, glyphs, FontKey);
        foreach (string key in scene.TextureKeys) if (!key.StartsWith(GeTextureKeys.EbootPrefix, StringComparison.Ordinal)) textures.AddImported(key);
        var layer = new GeLayer(t => t.Key == FontKey ? (glyphs.Pixels, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size) : textures.Pixels(t));
        return new(scene, CardAlbumLayout.Order(catalog), layer);
    }

    /// <summary>Renders a settled view with the hand over a pocket, or a turn in progress.</summary>
    public void Show(int view, bool rightPage, int pocket, CardAlbumTurn? turn = null) =>
        Layer.Render(scene.Compose(order, collection, view, rightPage, pocket, turn));

    public void Dispose() => Layer.Dispose();
}
