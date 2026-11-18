using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>GE render state of one captured binder draw.</summary>
public sealed record CardAlbumMeshState(bool Cull, int CullDirection, bool Blend, int BlendSource, int BlendDest, bool DepthTest, int DepthFunc,
    bool DepthWrite, bool Linear, bool ClampU, bool ClampV);

/// <summary>One captured binder draw: world-space triangles with baked lighting, and either an archive texture, a pocket backing that is drawn
/// only for owned cards (<paramref name="Slot"/> set, <paramref name="Face"/> false), or a pocket face that shows the slot's card face.</summary>
public sealed record CardAlbumMesh(string? Member, int? Slot, bool Face, CardAlbumMeshState State, VertexPositionColorTexture[] Vertices);

public sealed record CardAlbumFrame(string Name, int RightSheet, Matrix View, Matrix Projection, int[] Meshes);

/// <summary>Private captured binder package (binder.json v2 with its SHA-256 beside it): original model data, never committed. Settled
/// first/middle/last views and page-turn keyframes. Pocket slots are relative to the first pocket of the frame's right sheet.</summary>
public sealed class CardAlbumBinder
{
    public const string FileName = "binder.json";
    public IReadOnlyList<CardAlbumMesh> Meshes { get; }
    public IReadOnlyDictionary<string, CardAlbumFrame> Frames { get; }
    public string Sha256 { get; }

    private CardAlbumBinder(IReadOnlyList<CardAlbumMesh> meshes, IReadOnlyDictionary<string, CardAlbumFrame> frames, string sha)
    { Meshes = meshes; Frames = frames; Sha256 = sha; }

    public static CardAlbumBinder Load(string directory)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(directory, FileName));
        string sha = Convert.ToHexStringLower(SHA256.HashData(data));
        string pinned = File.ReadAllText(Path.Combine(directory, FileName + ".sha256")).Trim();
        if (!string.Equals(sha, pinned, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Card Album binder package hash mismatch.");
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2 || root.GetProperty("buildId").GetString() != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Card Album binder package schema/source mismatch.");
        var meshes = root.GetProperty("meshes").EnumerateArray().Select(ReadMesh).ToArray();
        var frames = new Dictionary<string, CardAlbumFrame>(StringComparer.Ordinal);
        foreach (var f in root.GetProperty("frames").EnumerateObject())
        {
            var camera = f.Value.GetProperty("camera");
            int[] ids = f.Value.GetProperty("meshes").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            if (ids.Any(i => i < 0 || i >= meshes.Length)) throw new InvalidDataException("Card Album binder frame references a missing mesh.");
            frames[f.Name] = new(f.Name, f.Value.GetProperty("rightSheet").GetInt32(), ViewMatrix(Floats(camera.GetProperty("view"), 12)),
                ProjectionMatrix(Floats(camera.GetProperty("projection"), 16)), ids);
        }
        foreach (string required in new[] { "first", "middle", "last" })
            if (!frames.ContainsKey(required)) throw new InvalidDataException("Card Album binder package lacks the " + required + " view.");
        var cursors = new Dictionary<string, int[]>(StringComparer.Ordinal);
        if (root.TryGetProperty("cursors", out var c))
            foreach (var entry in c.EnumerateObject())
            {
                int[] ids = entry.Value.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                if (ids.Any(i => i < 0 || i >= meshes.Length)) throw new InvalidDataException("Card Album binder cursor references a missing mesh.");
                cursors[entry.Name] = ids;
            }
        return new(meshes, frames, sha) { Cursors = cursors };
    }

    private static float[] Floats(JsonElement e, int count)
    {
        var values = e.EnumerateArray().Select(v => v.GetSingle()).ToArray();
        return values.Length == count ? values : throw new InvalidDataException("Card Album binder matrix has the wrong size.");
    }

    private static CardAlbumMesh ReadMesh(JsonElement e)
    {
        var s = e.GetProperty("state");
        var state = new CardAlbumMeshState(s.GetProperty("cull").GetBoolean(), s.GetProperty("cullDirection").GetInt32(), s.GetProperty("blend").GetBoolean(),
            s.GetProperty("blendSource").GetInt32(), s.GetProperty("blendDest").GetInt32(), s.GetProperty("depthTest").GetBoolean(),
            s.GetProperty("depthFunc").GetInt32(), s.GetProperty("depthWrite").GetBoolean(), s.GetProperty("linear").GetBoolean(),
            s.GetProperty("clampU").GetBoolean(), s.GetProperty("clampV").GetBoolean());
        string? member = null; int? slot = null; bool face = false;
        if (e.GetProperty("texture") is { ValueKind: JsonValueKind.Object } t)
        {
            face = t.GetProperty("kind").GetString() == "face-slot";
            if (t.TryGetProperty("member", out var m)) member = m.GetString();
            if (t.TryGetProperty("slot", out var sl)) slot = sl.GetInt32();
        }
        var vertices = e.GetProperty("vertices").EnumerateArray().Select(v =>
        {
            var a = v.EnumerateArray().Select(x => x.GetSingle()).ToArray();
            if (a.Length != 9) throw new InvalidDataException("Card Album binder vertex has the wrong size.");
            return new VertexPositionColorTexture(new Vector3(a[0], a[1], a[2]), new Color(a[5], a[6], a[7], a[8]), new Vector2(a[3], a[4]));
        }).ToArray();
        if (vertices.Length % 3 != 0) throw new InvalidDataException("Card Album binder mesh is not a triangle list.");
        return new(member, slot, face, state, vertices);
    }

    /// <summary>PSP 4x3 matrices store columns of a column-vector transform; XNA uses row vectors, so each stored triple is one row.</summary>
    public static Matrix ViewMatrix(float[] m) => new(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);

    /// <summary>The PSP projection, with depth remapped from the GE range (larger is nearer under GEQUAL) to MonoGame's 0..1 with LessEqual:
    /// z' = 0.5·z + 0.5·w. X and Y already map to the 480x272 viewport exactly as the captured viewport does.</summary>
    public static Matrix ProjectionMatrix(float[] p) => new(
        p[0], p[1], 0.5f * p[2] + 0.5f * p[3], p[3],
        p[4], p[5], 0.5f * p[6] + 0.5f * p[7], p[7],
        p[8], p[9], 0.5f * p[10] + 0.5f * p[11], p[11],
        p[12], p[13], 0.5f * p[14] + 0.5f * p[15], p[15]);

    /// <summary>The settled frame for a view: the first and last views were captured as such; every other view uses the captured middle view
    /// (stack heights at other positions are unverified).</summary>
    public CardAlbumFrame Settled(int view, int views) => Frames[view == 0 ? "first" : view == views - 1 ? "last" : "middle"];

    /// <summary>Keyframes of a turn: captured from the first view, the middle and the end where available, else the first-view turns.</summary>
    public IReadOnlyList<CardAlbumFrame> TurnFrames(int direction, int fromView, int views)
    {
        string kind = direction > 0 ? "turnR" : "turnL";
        int lowView = direction > 0 ? fromView : fromView - 1;
        string suffix = lowView == 0 ? "" : lowView + 1 == views - 1 ? "-nearend" : "-mid";
        var frames = Frames.Values.Where(f => f.Name.StartsWith(kind + suffix + "/", StringComparison.Ordinal)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (frames.Length == 0 && suffix != "")
            frames = Frames.Values.Where(f => f.Name.StartsWith(kind + "/", StringComparison.Ordinal)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        return frames;
    }

    /// <summary>Captured 3D pocket highlight for a focus, keyed "first|middle|last/right|left/pocket"; null when that focus was not captured.</summary>
    public IReadOnlyDictionary<string, int[]> Cursors { get; private init; } = new Dictionary<string, int[]>();

    public int[]? Cursor(int view, int views, bool rightPage, int pocket)
    {
        string settled = view == 0 ? "first" : view == views - 1 ? "last" : "middle", page = rightPage ? "right" : "left";
        if (Cursors.TryGetValue($"{settled}/{page}/{pocket}", out var ids)) return ids;
        // Approximation: uncaptured right-page highlights reuse the first view's, uncaptured left-page ones the middle view's.
        return Cursors.TryGetValue($"{(rightPage ? "first" : "middle")}/{page}/{pocket}", out ids) ? ids : null;
    }

    private readonly Dictionary<(string, string), bool> compatible = [];

    /// <summary>Whether two keyframes draw the same meshes in the same order (same textures, slots, states and vertex counts), so their
    /// vertices can be blended one to one.</summary>
    public bool Compatible(CardAlbumFrame a, CardAlbumFrame b)
    {
        if (compatible.TryGetValue((a.Name, b.Name), out bool known)) return known;
        bool same = a.Meshes.Length == b.Meshes.Length && a.Meshes.Zip(b.Meshes).All(p =>
        {
            CardAlbumMesh x = Meshes[p.First], y = Meshes[p.Second];
            return x.Vertices.Length == y.Vertices.Length && x.Member == y.Member && x.Slot == y.Slot && x.Face == y.Face && x.State == y.State;
        });
        compatible[(a.Name, b.Name)] = same;
        return same;
    }

    /// <summary>The view whose right sheet a turn frame's slots are relative to: the destination of a forward turn, the start of a backward one.</summary>
    public static int TurnBaseView(int direction, int fromView) => direction > 0 ? fromView + 1 : fromView;
}
