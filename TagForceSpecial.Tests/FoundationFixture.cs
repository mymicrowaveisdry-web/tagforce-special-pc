using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;

namespace TagForceSpecial.Tests;

internal sealed class FoundationFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "tagforce-foundations-" + Guid.NewGuid().ToString("N"));
    internal string Iso => Path.Combine(Root, "synthetic.iso");
    internal string CatalogPath => Path.Combine(Root, "catalog");
    internal string CachePath => Path.Combine(Root, "cache");
    internal byte[] Payload { get; } = "Synthetic source input, not game bytes."u8.ToArray();
    internal CardCatalog Catalog { get; }

    internal FoundationFixture()
    {
        Directory.CreateDirectory(Root);
        byte[] image = new byte[25 * 2048];
        image[16 * 2048] = 1; "CD001"u8.CopyTo(image.AsSpan(16 * 2048 + 1)); image[16 * 2048 + 6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(16 * 2048 + 128), 2048);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(16 * 2048 + 130), 2048);
        Record([0], 20, 2048, true).CopyTo(image, 16 * 2048 + 156);
        image[17 * 2048] = 255; "CD001"u8.CopyTo(image.AsSpan(17 * 2048 + 1)); image[17 * 2048 + 6] = 1;
        DirectoryRecord(20, "PSP_GAME", 21, 2048, true);
        DirectoryRecord(21, "USRDIR", 22, 2048, true);
        DirectoryRecord(22, "SRC.BIN;1", 23, Payload.Length, false);
        Payload.CopyTo(image, 23 * 2048);
        File.WriteAllBytes(Iso, image);
        Catalog = new(new(Hash(Payload), "synthetic-source-v1"),
            [new(1, "Synthetic monster", "Authored test input", new(1000, 1000, 4), Kind: CardKind.Monster, Effects: CardEffectBinding.None)]);
        CardCatalogPackage.Write(CatalogPath, Catalog, new("synthetic", "1", "en", "synthetic-source"));

        void DirectoryRecord(int sector, string name, int target, int length, bool directory) =>
            Record(Encoding.ASCII.GetBytes(name), target, length, directory).CopyTo(image, sector * 2048);
    }

    internal GameBuildProfile Profile(string mapping = "mapping-v1", string conversion = "conversion-v1", int schema = 1,
        long? requiredLength = null, string? requiredHash = null) =>
        new("synthetic-build", new FileInfo(Iso).Length, Hash(File.ReadAllBytes(Iso)), schema, mapping, conversion, Hash(Payload),
            [new("PSP_GAME/USRDIR/SRC.BIN", requiredLength ?? Payload.Length, requiredHash ?? Hash(Payload), "source/input.bin")],
            new[] { "manifest.json", "cards.json" }.Select(name =>
            {
                string file = Path.Combine(CatalogPath, name);
                return new CacheFileIdentity("catalog/" + name, new FileInfo(file).Length, Hash(File.ReadAllBytes(file)));
            }));

    internal void EditImage(Action<byte[]> edit)
    {
        byte[] image = File.ReadAllBytes(Iso); edit(image); File.WriteAllBytes(Iso, image);
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static byte[] Record(byte[] name, int sector, int length, bool directory)
    {
        var row = new byte[33 + name.Length + (name.Length % 2 == 0 ? 1 : 0)];
        row[0] = (byte)row.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2), (uint)sector);
        BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(6), (uint)sector);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(10), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(14), (uint)length);
        row[25] = directory ? (byte)2 : (byte)0; row[32] = (byte)name.Length; name.CopyTo(row, 33);
        return row;
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
}
