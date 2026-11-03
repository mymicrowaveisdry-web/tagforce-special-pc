namespace TagForceSpecial.PC.Game;

public sealed record HostOptions(string? CachePath = null, bool PixelExact = false,
    string? CapturePath = null, bool Smoke = false, uint Seed = 0, string? ReplayPath = null, string? ResultCapturePath = null, int DeckVariant = 0,
    string? DataRoot = null, string? SetupSource = null, string? EditorCapture = null, bool SeedSpecified = false)
{
    public static HostOptions Parse(string[] args)
    {
        string? cache = null, capture = null, replay = null, resultCapture = null;
        uint seed = 0;
        int deckVariant = 0;
        string? dataRoot = null, setupSource = null, editorCapture = null;
        bool seedSpecified = false;
        bool pixel = false, smoke = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cache" when i + 1 < args.Length: cache = Path.GetFullPath(args[++i]); break;
                case "--capture" when i + 1 < args.Length: capture = Path.GetFullPath(args[++i]); break;
                case "--pixel-exact": pixel = true; break;
                case "--smoke": smoke = true; break;
                case "--seed" when i + 1 < args.Length && uint.TryParse(args[i + 1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out seed): seedSpecified = true; i++; break;
                case "--replay" when i + 1 < args.Length: replay = Path.GetFullPath(args[++i]); break;
                case "--capture-result" when i + 1 < args.Length: resultCapture = Path.GetFullPath(args[++i]); break;
                case "--deck" when i + 1 < args.Length && int.TryParse(args[i + 1], out deckVariant) && deckVariant is >= 0 and <= 2: i++; break;
                case "--data-root" when i + 1 < args.Length: dataRoot = Path.GetFullPath(args[++i]); break;
                case "--setup-smoke" when i + 1 < args.Length: setupSource = Path.GetFullPath(args[++i]); break;
                case "--capture-editor" when i + 1 < args.Length: editorCapture = Path.GetFullPath(args[++i]); break;
                default: throw new ArgumentException("Usage: PC [--cache <cache>] [--pixel-exact] [--capture <new.png>] [--smoke] [--deck <0|1|2>] [--seed <uint>] [--replay <new.json>] [--capture-result <new.png>]");
            }
        }
        if (setupSource is not null && !smoke) throw new ArgumentException("--setup-smoke requires --smoke.");
        return new(cache, pixel, capture, smoke, seed, replay, resultCapture, deckVariant, dataRoot, setupSource, editorCapture, seedSpecified);
    }
}
