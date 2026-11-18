using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TagForceSpecial.Assets.Presentation;

/// <summary>A pack that lists a card, with the card's rarity group in it (0 common, 1 R, 2 SR, 3 UR: the Pack List badge).</summary>
public readonly record struct CardPackEntry(int Pack, int Rarity);

/// <summary>Card detail data read from the user's own original files (nothing bundled): the genre labels and their CARD_Genre bits, the
/// pack table and pack names, the quick-effect monster list and the kind classes, all from the EBOOT of the pinned build, plus
/// CARD_Genre.bin and CARD_Same.bin from duelsys/cardinfo_jpn.ehp. Locations come from disassembly of the card detail code
/// (genre list 0x199c8, icon cells 0x17e98, Spell Speed 0x4b8e4, pack rows 0x18104).</summary>
public sealed class CardDetailSource
{
    public const string EbootSha256 = "8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975";
    private const int TextBase = 0xC0;              // file offset of virtual address 0
    private const int GenreLabelText = 1496326;     // 51 UTF-16 labels, "New Card" first
    private const int GenreBitTable = 1591400;      // 50 x int32: CARD_Genre bit of each label (-1: none)
    private const int PackTable = 1589112;          // 12-byte entries: header address, price, count; header = 4 x {ids address, count}
    private const int PackNameText = 1220306;       // UTF-16 names of packs 1.., in table order
    private const int QuickMonsters = 0xF0F24 + TextBase, QuickMonsterCount = 293;
    private const int KindTable = 0xF0960 + TextBase, KindEntry = 10;
    public const int PackCount = 183, LabelCount = 51, AlternateArtworkLabel = 50;
    private readonly byte[] genres;
    private readonly (short Card, short Other, short Flag)[] same;
    private readonly HashSet<uint> quick;
    private readonly short[][] kinds;
    private readonly Dictionary<uint, List<CardPackEntry>> packsByCard = [];

    public IReadOnlyList<string> Labels { get; }
    public IReadOnlyList<int> LabelBits { get; }
    public IReadOnlyList<string> PackNames { get; }

    public CardDetailSource(byte[] eboot, byte[] cardInfoArchive)
    {
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(eboot)), EbootSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Card detail data needs the pinned EBOOT build.");
        Labels = Strings(eboot, GenreLabelText, LabelCount, false);
        LabelBits = Enumerable.Range(0, 50).Select(i => BinaryPrimitives.ReadInt32LittleEndian(eboot.AsSpan(GenreBitTable + 4 * i))).ToArray();
        if (LabelBits[0] != -1 || LabelBits.Skip(1).Any(b => b is < 0 or > 63)) throw new InvalidDataException("Unexpected genre bit table.");
        PackNames = [null!, .. Strings(eboot, PackNameText, PackCount - 1, true)];
        for (int pack = 1; pack < PackCount; pack++)
        {
            int header = BinaryPrimitives.ReadInt32LittleEndian(eboot.AsSpan(PackTable + 12 * pack));
            if (header == 0) continue;
            for (int rarity = 0; rarity < 4; rarity++)
            {
                int at = header + TextBase + 8 * rarity;
                int ids = BinaryPrimitives.ReadInt32LittleEndian(eboot.AsSpan(at)) + TextBase, count = BinaryPrimitives.ReadInt32LittleEndian(eboot.AsSpan(at + 4));
                if (count is < 0 or > 10000 || ids < 0 || ids + 2L * count > eboot.Length) throw new InvalidDataException("Unexpected pack table.");
                for (int k = 0; k < count; k++)
                {
                    uint id = BinaryPrimitives.ReadUInt16LittleEndian(eboot.AsSpan(ids + 2 * k));
                    if (!packsByCard.TryGetValue(id, out var list)) packsByCard[id] = list = [];
                    if (!list.Any(e => e.Pack == pack)) list.Add(new(pack, rarity));
                }
            }
        }
        var quickIds = Enumerable.Range(0, QuickMonsterCount).Select(i => BinaryPrimitives.ReadInt16LittleEndian(eboot.AsSpan(QuickMonsters + 2 * i))).ToArray();
        if (quickIds.Zip(quickIds.Skip(1)).Any(p => p.First >= p.Second)) throw new InvalidDataException("Unexpected quick-effect monster list.");
        quick = [.. quickIds.Select(v => (uint)v)];
        kinds = Enumerable.Range(0, 33).Select(i => Enumerable.Range(0, 5).Select(k => BinaryPrimitives.ReadInt16LittleEndian(eboot.AsSpan(KindTable + KindEntry * i + 2 * k))).ToArray()).ToArray();
        using var stream = new MemoryStream(cardInfoArchive, false);
        var index = new EhpReader().ReadIndex(stream);
        byte[] Member(string name)
        {
            var entry = index.Entries.FirstOrDefault(e => e.Name == name) ?? throw new InvalidDataException("cardinfo archive lacks " + name + ".");
            return cardInfoArchive.AsSpan((int)entry.Offset, (int)entry.Length).ToArray();
        }
        genres = Member("CARD_Genre.bin");
        byte[] sameBytes = Member("CARD_Same.bin");
        same = Enumerable.Range(0, sameBytes.Length / 6).Select(i => (BinaryPrimitives.ReadInt16LittleEndian(sameBytes.AsSpan(6 * i)),
            BinaryPrimitives.ReadInt16LittleEndian(sameBytes.AsSpan(6 * i + 2)), BinaryPrimitives.ReadInt16LittleEndian(sameBytes.AsSpan(6 * i + 4)))).ToArray();
    }

    private static string[] Strings(byte[] data, int offset, int count, bool skipPadding)
    {
        var result = new string[count];
        int at = offset;
        for (int i = 0; i < count; i++)
        {
            if (skipPadding) while (at + 1 < data.Length && data[at] == 0 && data[at + 1] == 0) at += 2;
            int end = at;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0)) end += 2;
            result[i] = Encoding.Unicode.GetString(data, at, end - at);
            at = end + 2;
        }
        return result;
    }

    /// <summary>Genre label indices shown for a card, in label order (the CARD GENRE tab and the panel icons): every label whose
    /// CARD_Genre bit is set, then "Card with a different artwork" when CARD_Same flags the card.</summary>
    public IReadOnlyList<int> GenresOf(uint cardId, int internalIndex)
    {
        var result = new List<int>();
        if (internalIndex < 0 || 8 * internalIndex + 8 > genres.Length) return result;
        ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(genres.AsSpan(8 * internalIndex));
        for (int label = 1; label < LabelBits.Count; label++)
            if ((bits >> LabelBits[label] & 1) != 0) result.Add(label);
        if (IsAlternateArtwork(cardId)) result.Add(AlternateArtworkLabel);
        return result;
    }

    /// <summary>0x1b8cc: a CARD_Same record for the card with a non-zero low flag byte, or one naming it as the other card with flag 1.</summary>
    public bool IsAlternateArtwork(uint cardId)
    {
        foreach (var (card, other, flag) in same)
            if ((card == cardId && (flag & 0xff) != 0) || (other == cardId && flag == 1)) return true;
        return false;
    }

    /// <summary>Texel cell (16x16) of a label's icon in icon06 (0x17e98).</summary>
    public static (int U, int V) IconCell(int label) => label switch
    {
        11 or 12 => ((label - 11) * 16, 148),
        46 => (112, 148),
        47 => (96, 148),
        >= 38 and < 46 => label < 40 ? (64 + (label - 38) * 16, 147) : ((label - 40) * 16, 164),
        48 => (112, 164),
        49 => (96, 164),
        AlternateArtworkLabel => (64, 64),
        < 11 => (label % 8 * 16, label / 8 * 16),
        _ => ((label - 2) % 8 * 16, (label - 2) / 8 * 16)
    };

    /// <summary>Spell Speed digit (0x4b8e4), or null when the panel shows none (Normal and other non-effect monsters, 0x19978): Spells 1
    /// (Quick-Play 2), Traps 2 (Counter 3), monsters 2 when listed as quick-effect monsters, else 1.</summary>
    public int? SpellSpeed(uint cardId, uint type, uint icon, uint getterKind)
    {
        int klass = KindRow(getterKind).Class;
        if (klass is > 0 and < 3) return null;
        if (type == 25) return icon == 5 ? 2 : 1;
        if (type == 26) return icon == 1 ? 3 : 2;
        return quick.Contains(cardId) ? 2 : 1;
    }

    /// <summary>Frame value of Xyz kinds in the kind table (getter kinds 22 and 23).</summary>
    public const int XyzFrame = 12;

    /// <summary>Kind table row (EBOOT 0xf0960, 10 bytes per getter kind): frame family, class (0 Spell/Trap, 1-2 no effect, 3+ effect), and
    /// the Pendulum flag (last field).</summary>
    public (int Frame, int Class, bool Pendulum) KindRow(uint getterKind) =>
        getterKind < kinds.Length ? (kinds[getterKind][0], kinds[getterKind][1], kinds[getterKind][4] != 0) : (0, 0, false);

    /// <summary>Packs listing the card, in table order.</summary>
    public IReadOnlyList<CardPackEntry> PacksOf(uint cardId) => packsByCard.TryGetValue(cardId, out var list) ? list : [];
}
