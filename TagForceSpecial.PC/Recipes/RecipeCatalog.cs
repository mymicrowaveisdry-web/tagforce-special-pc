using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Recipes;

public enum RecipeListKind { User, Character }

public sealed record RecipeRecord(RecipeListKind Kind, int Slot, string Name, DateTime Updated,
    uint[] Main, uint[] Extra, uint[] Side, string RecordSha256, uint Address, string? Author = null, string Source = "runtime-save");
public sealed record RecipeTexture(string Key, string Sha256, int Width, int Height);
public sealed record RecipeLayout(FlowDraw[] Static, FlowDraw[] Row, FlowDraw[] Focus, float RowTop, float RowPitch,
    int VisibleRows, float NameX, float NameY, float DateX, float DateY,
    FlowDraw[]? Manage = null, FlowDraw[]? ManageFocus = null, FlowDraw[]? SortPanel = null,
    FlowDraw[]? SortFocus = null, FlowDraw[]? OrderPanel = null, FlowDraw[]? OrderFocus = null);
public sealed record RecipeDocument(int SchemaVersion, string BuildId, string IsoSha256, string SaveSha256,
    string RamSha256, string AnchorSha256, RecipeRecord[] Recipes, RecipeTexture[] Textures, RecipeLayout Layout);

/// <summary>Private source-derived recipe records and measured visuals; integrity does not establish original selection parity.</summary>
public sealed class RecipeCatalog
{
    private readonly PrivateStorage storage;
    private readonly Dictionary<string, RecipeTexture> textures;
    public IReadOnlyList<RecipeRecord> Records { get; }
    public RecipeLayout Layout { get; }
    public string Identity { get; }

    private RecipeCatalog(PrivateStorage storage, RecipeDocument doc, CardCatalog catalog)
    {
        this.storage = storage;
        if (doc.SchemaVersion != 1 || doc.BuildId != GameBuildProfile.English21.BuildId || doc.IsoSha256 != GameBuildProfile.English21.ImageSha256 ||
            !Hash(doc.SaveSha256) || !Hash(doc.RamSha256) || !Hash(doc.AnchorSha256) || doc.Recipes is null || doc.Recipes.Length > 2000 ||
            doc.Textures is null || doc.Textures.Length > 8000 || doc.Layout is null)
            throw new InvalidDataException("Recipe source identity or bounds are invalid.");
        var knownCards = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        var ids = new HashSet<(RecipeListKind, int)>();
        foreach (var r in doc.Recipes)
        {
            if (r is null || !Enum.IsDefined(r.Kind) || r.Slot is < 0 or > 1999 || !ids.Add((r.Kind, r.Slot)) || string.IsNullOrWhiteSpace(r.Name) ||
                r.Name.Length > 64 || r.Name.Any(char.IsControl) || r.Author is { Length: > 64 } || r.Author?.Any(char.IsControl) == true || !Hash(r.RecordSha256) || r.Address is < 0x08800000 or >= 0x0a000000 ||
                r.Main is null || r.Extra is null || r.Side is null || r.Main.Length > 60 || r.Extra.Length > 15 || r.Side.Length > 15 ||
                r.Main.Concat(r.Extra).Concat(r.Side).Any(id => !knownCards.Contains(id)))
                throw new InvalidDataException("Recipe records are malformed or reference an unknown catalog card.");
        }
        var l = doc.Layout;
        if (l.VisibleRows != 8 || l.RowPitch != 29 || l.RowTop != 36 ||
            l.Static is null || l.Row is null || l.Focus is null || l.Static.Length > 1000 || l.Row.Length > 32 || l.Focus.Length > 32)
            throw new InvalidDataException("Recipe layout is not a bounded measured list.");
        textures = new(StringComparer.Ordinal);
        foreach (var t in doc.Textures)
            if (t is null || !Hash(t.Sha256) || t.Width is < 1 or > 512 || t.Height is < 1 or > 512 ||
                string.IsNullOrWhiteSpace(t.Key) || t.Key.Length > 100 || !textures.TryAdd(t.Key, t))
                throw new InvalidDataException("Invalid recipe texture manifest.");
        var groups = new[] { l.Static, l.Row, l.Focus, l.Manage ?? [], l.ManageFocus ?? [],
            l.SortPanel ?? [], l.SortFocus ?? [], l.OrderPanel ?? [], l.OrderFocus ?? [] };
        foreach (var group in groups)
        {
            if (group.Length > 1000) throw new InvalidDataException("Recipe draw group exceeds its bound.");
            foreach (var draw in group)
                if (draw is null || draw.Primitive is < 3 or > 5 || draw.AssetKey is { } key && !textures.ContainsKey(key) ||
                    draw.Points is null || draw.Points.Length is < 3 or > 4096 ||
                    draw.Points.Any(p => p is null || p.Length != 2 || p.Any(v => !float.IsFinite(v))) ||
                    draw.Color is null || draw.Color.Length != 4 || draw.Color.Any(c => c is < 0 or > 255) ||
                    draw.Uv is null || draw.AssetKey is not null && draw.Uv.Length != draw.Points.Length ||
                    draw.Uv.Any(p => p is null || p.Length != 2 || p.Any(v => !float.IsFinite(v))) ||
                    draw.VertexColors is not null && draw.VertexColors.Length != draw.Points.Length ||
                    draw.Add is not null && (draw.Add.Length != 3 || draw.Add.Any(v => v is < 0 or > 255)))
                    throw new InvalidDataException("Invalid measured recipe draw.");
        }
        foreach (var t in textures.Values)
            if (!File.Exists(storage.PathFor("recipes/blobs/" + t.Sha256 + ".rgba")) ||
                new FileInfo(storage.PathFor("recipes/blobs/" + t.Sha256 + ".rgba")).Length != checked(t.Width * t.Height * 4))
                throw new InvalidDataException("Recipe texture data is missing or has the wrong size.");
        Records = Array.AsReadOnly(doc.Recipes);
        Layout = l;
        Identity = doc.RamSha256;
    }

    public static RecipeCatalog? Open(PrivateStorage storage, CardCatalog catalog)
    {
        if (!File.Exists(storage.PathFor("recipes/viewer.json"))) return null;
        return new(storage, storage.Read<RecipeDocument>("recipes/viewer.json", 8 * 1024 * 1024), catalog);
    }

    public (byte[] Pixels, int Width, int Height)? ReadTexture(string key)
    {
        if (!textures.TryGetValue(key, out var item)) return null;
        string path = storage.PathFor("recipes/blobs/" + item.Sha256 + ".rgba");
        if (new FileInfo(path).Length != checked(item.Width * item.Height * 4)) throw new InvalidDataException("Recipe texture size mismatch.");
        byte[] bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != item.Sha256) throw new InvalidDataException("Recipe texture integrity mismatch.");
        return (bytes, item.Width, item.Height);
    }

    private static bool Hash(string? text) => text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
