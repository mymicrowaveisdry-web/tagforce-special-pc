namespace TagForceSpecial.PC.Game;

public sealed record HostOptions(string? CachePath = null, bool PixelExact = false, string? CapturePath = null, bool Smoke = false)
{
    public static HostOptions Parse(string[] args)
    {
        string? cache = null, capture = null; bool pixel = false, smoke = false;
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--cache" when i + 1 < args.Length: cache = Path.GetFullPath(args[++i]); break;
                case "--capture" when i + 1 < args.Length: capture = Path.GetFullPath(args[++i]); break;
                case "--pixel-exact": pixel = true; break;
                case "--smoke": smoke = true; break;
                default: throw new ArgumentException("Usage: PC [--cache <cache>] [--pixel-exact] [--capture <new.png>] [--smoke]");
            }
        return new(cache, pixel, capture, smoke);
    }
}
