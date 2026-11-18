using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Builds the Card Album from the import (pinned textures, label archive, card faces), the private captured binder package and the
/// profile's collection, and keeps its renderer between visits. Null when any required input is missing.</summary>
public sealed class CardAlbumScreen : IDisposable
{
    public const string CollectionFile = "collection.json";
    private readonly CardCatalog catalog;
    private readonly IReadOnlyList<uint> order;
    public CardAlbumRenderer Renderer { get; }

    private CardAlbumScreen(CardCatalog catalog, CardAlbumRenderer renderer)
    {
        this.catalog = catalog; Renderer = renderer; order = CardAlbumLayout.Order(catalog);
        initials = order.Select(id => CardAlbumLayout.PrimaryKey(catalog[id].Name) is { Length: > 0 } key && key[0] is >= 'A' and <= 'Z' ? key[0] : '*').ToArray();
    }
    private readonly char[] initials;

    public static CardAlbumScreen? TryCreate(GraphicsDevice device, TitleFlowVisuals visuals, OriginalScreenAssetResolver assets, CardCatalog catalog, string? binderRoot)
    {
        if (binderRoot is null) { Console.Error.WriteLine("CARD_ALBUM_PENDING no captured binder package (pass --album-reference)."); return null; }
        try
        {
            var binder = CardAlbumBinder.Load(binderRoot);
            var profile = CardAlbumProfile.LoadEmbedded();
            var binderKeys = binder.Meshes.Select(m => m.Member).OfType<string>().Distinct(StringComparer.Ordinal).Select(CardAlbumResources.KeyFor);
            visuals.UseAlbumAssets(assets, profile.AssetKeys().Concat(binderKeys), CardAlbumResources.ArchiveKey, CardAlbumResources.CardInfoKey);
            visuals.UseEbootTexture(profile.CardBack.Texture);
            var faces = new CardFaceSource(assets.ResolveBytes(CardAlbumResources.FacesKey));
            return new(catalog, new CardAlbumRenderer(device, visuals, binder, profile, faces, catalog));
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("CARD_ALBUM_PENDING " + e.Message);
            return null;
        }
    }

    /// <summary>A new visit: the profile's collection file when present, else the measured everything-obtained reference state.</summary>
    public CardAlbumSession Open(string? collectionPath)
    {
        var collection = collectionPath is not null && File.Exists(collectionPath)
            ? CardAlbumCollection.FromPlayer(DeckFiles.ReadCollection(collectionPath))
            : CardAlbumCollection.ReferenceAllObtained(order);
        var session = new CardAlbumSession(order, collection, initials);
        var close = Renderer.Profile.PageJump.Close;
        session.UseJumpTiming(close.TurnFrame, close.Length);
        return session;
    }

    public CardCatalog Catalog => catalog;

    public void Dispose() => Renderer.Dispose();
}
