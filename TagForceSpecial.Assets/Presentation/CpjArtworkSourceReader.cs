using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Assets.Presentation;

internal static class CpjArtworkSourceReader
{
    internal const string HeaderSource = "PSP_GAME/SYSDIR/EBOOT.BIN";
    internal const string BootSha256 = "8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975";
    internal const string HeaderSha256 = "c089d458a2d40b2adc8464e3fa4efa172331f668d9fa713bf8934627ad836b04";
    internal const int HeaderOffset = 1538280;

    public static SelectedPresentationSource Read(ValidatedGameSource source, IGameFiles files,
        PresentationResourceRequest request)
    {
        if (source.Profile.Fingerprint != GameBuildProfile.English21.Fingerprint || request.CardId is not > 0 || request.MemberName is not null)
            throw new InvalidDataException("CPJ requires the registered exact source profile and an explicit nonzero card ID.");
        var file = files.Find(request.SourcePath) ?? throw new InvalidDataException("Missing CPJ archive.");
        using var archive = files.OpenRead(file);
        var index = new CardImageIndexReader().ReadIndex(archive);
        if (index.Signature != "CPJ") throw new InvalidDataException("Full artwork requires a CPJ signature, never a thumbnail alias.");
        var slot = index.Entries.SingleOrDefault(e => e.CardId == request.CardId)
            ?? throw new KeyNotFoundException("Missing full-art card ID.");
        if (slot.StoredSlotBytes > PresentationPackage.MaximumResourceBytes) throw new InvalidDataException("Oversized CPJ slot.");
        string archiveHash = GameSourceValidator.Hash(archive, 0, file.Length);
        archive.Position = slot.Offset;
        byte[] bytes = new byte[checked((int)slot.StoredSlotBytes)]; archive.ReadExactly(bytes);
        var boot = files.Find(HeaderSource) ?? throw new InvalidDataException("Missing JPEG header source.");
        using var bootStream = files.OpenRead(boot);
        if (GameSourceValidator.Hash(bootStream, 0, boot.Length) != BootSha256 || HeaderOffset + 160 > boot.Length)
            throw new InvalidDataException("JPEG header source hash/bounds mismatch.");
        bootStream.Position = HeaderOffset;
        byte[] header = new byte[160]; bootStream.ReadExactly(header);
        for (int i = 0; i < header.Length; i++) header[i] ^= 92;
        if (Convert.ToHexStringLower(SHA256.HashData(header)) != HeaderSha256)
            throw new InvalidDataException("JPEG header identity mismatch.");
        return new(archiveHash, slot.Offset, bytes, [], header,
            new(request.CardId.Value, HeaderSource, BootSha256, HeaderOffset, 160, HeaderSha256, "", ""));
    }

    private sealed class SourceSlice(Stream source, long start, long length) : Stream
    {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            source.Position = checked(start + position);
            int count = source.Read(buffer[..(int)Math.Min(buffer.Length, length - position)]);
            position += count; return count;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long next = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length) + offset);
            if (next < 0 || next > length) throw new IOException("Archive seek exceeds source bounds.");
            return position = next;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
