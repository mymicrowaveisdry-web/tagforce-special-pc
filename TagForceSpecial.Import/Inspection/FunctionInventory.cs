using System.Globalization;

namespace TagForceSpecial.Import.Inspection;

public sealed record FunctionCandidate(uint Address, int BodyBytes);

/// <summary>Reads the existing address/body_bytes registry, shared by CLI progress and desktop analysis.</summary>
public static class FunctionInventory
{
    public static IReadOnlyList<FunctionCandidate> Read(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length < 2 || lines[0] != "address,body_bytes")
            throw new InvalidDataException("Expected candidate registry header: address,body_bytes.");
        var found = new HashSet<uint>();
        var functions = new List<FunctionCandidate>();
        foreach (string line in lines.Skip(1))
        {
            var fields = line.Split(',');
            if (fields.Length != 2 || !fields[0].StartsWith("0x", StringComparison.Ordinal) ||
                !uint.TryParse(fields[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0 ||
                !found.Add(address))
                throw new InvalidDataException("Invalid or duplicate discovered function.");
            functions.Add(new(address, size));
        }
        return functions.AsReadOnly();
    }
}
