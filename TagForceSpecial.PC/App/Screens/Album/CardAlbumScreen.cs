using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Builds the Card Album from the import (pinned textures, label archive, card faces), the private captured binder package and the
/// profile's collection, and keeps its renderer between visits. Null when any required input is missing.</summary>
public sealed class CardAlbumScreen : IDisposable
{
    public const string CollectionFile = "collection.json";
    /// <summary>Data-root folder for the private captured binder package (binder.json and its .sha256), used without --album-reference.</summary>
    public const string ReferenceFolder = TagForceSpecial.Import.Stages.AlbumReferenceStage.Folder;
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
            var renderer = new CardAlbumRenderer(device, visuals, binder, profile, faces, catalog);
            var detailProfile = CardAlbumDetailProfile.LoadEmbedded();
            visuals.UseAlbumAssets(assets, detailProfile.AssetKeys(), CardAlbumResources.ArchiveKey, CardAlbumResources.CardInfoKey);
            renderer.Detail = new CardAlbumDetailRenderer(device, visuals, renderer, catalog, detailProfile,
                new CardDetailSource(visuals.SourceBytes("eboot"), visuals.SourceBytes("cardinfo")));
            return new(catalog, renderer);
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("CARD_ALBUM_PENDING " + e.Message);
            return null;
        }
    }

    /// <summary>A new visit: the profile's collection file when present, else the measured everything-obtained reference state. The detail's
    /// counters (owner decision, 17 Nov): Main and Side from <paramref name="inDeck"/>, the PC's current deck; the Trunk is the owned copies
    /// (the collection file's count, else the Deck Edit pool's <see cref="DeckEditSession.OwnedCopies"/> for supported cards, else 0) minus those.</summary>
    public CardAlbumSession Open(string? collectionPath, Func<uint, (int Main, int Side)>? inDeck = null)
    {
        PlayerCollection? player = collectionPath is not null && File.Exists(collectionPath) ? DeckFiles.ReadCollection(collectionPath) : null;
        var collection = player is not null ? CardAlbumCollection.FromPlayer(player) : CardAlbumCollection.ReferenceAllObtained(order);
        var session = new CardAlbumSession(order, collection, initials);
        if (Renderer.Detail is { } detail)
            session.UseDetail(id =>
            {
                var (main, side) = inDeck?.Invoke(id) ?? (0, 0);
                int owned = player is not null ? player.Counts.GetValueOrDefault(id) : ConfirmedCardPool.Contains(id) ? DeckEditSession.OwnedCopies : 0;
                var scroll = detail.Profile.Scroll;
                return new CardAlbumDetail(id, new(main, side, Math.Max(0, owned - main - side)), tab => detail.ContentHeight(id, tab), scroll.Visible, scroll.PixelsPerFrame);
            });
        var close = Renderer.Profile.PageJump.Close;
        session.UseJumpTiming(close.TurnFrame, close.Length);
        return session;
    }

    public CardCatalog Catalog => catalog;

    public void Dispose() => Renderer.Dispose();
}
