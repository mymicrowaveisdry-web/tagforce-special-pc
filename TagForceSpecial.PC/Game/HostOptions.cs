namespace TagForceSpecial.PC.Game;

public sealed record HostOptions(string? CachePath = null, bool PixelExact = false,
    string? CapturePath = null, bool Smoke = false, uint Seed = 0, string? ReplayPath = null, string? ResultCapturePath = null, int DeckVariant = 0,
    string? DataRoot = null, string? SetupSource = null, string? EditorCapture = null, bool SeedSpecified = false,
    bool AssetDiagnostics = false, string DiagnosticScreen = "title", string? AssetPreview = null, bool PresentationSmoke = false,
    string? PresentationTracePath = null, string? TitleLayerCaptureRoot = null, string? PspFontPath = null, string? FlowCaptureRoot = null,
    IReadOnlyDictionary<int, int>? MainMenuFrameByRow = null, int? MainMenuEntryFrame = null, string? RealtimeWalkRoot = null)
{
    /// <summary>Pixel-exact game frame per focused Main Menu row, for same-instant comparisons with GE dumps.</summary>
    public IReadOnlyDictionary<int, int> MainMenuFrames => MainMenuFrameByRow ?? new Dictionary<int, int>();

    public static HostOptions Parse(string[] args)
    {
        string? cache = null, capture = null, replay = null, resultCapture = null;
        uint seed = 0;
        int deckVariant = 0;
        string? dataRoot = null, setupSource = null, editorCapture = null;
        string diagnosticScreen = "title";
        string? assetPreview = null, presentationTrace = null, titleLayers = null, pspFont = null, flowCapture = null;
        Dictionary<int, int>? menuFrames = null;
        int? menuEntryFrame = null;
        string? realtimeWalk = null;
        bool seedSpecified = false;
        bool pixel = false, smoke = false, assetDiagnostics = false, presentationSmoke = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cache" when i + 1 < args.Length: cache = Path.GetFullPath(args[++i]); break;
                case "--capture" when i + 1 < args.Length: capture = Path.GetFullPath(args[++i]); break;
                case "--pixel-exact": pixel = true; break;
                case "--presentation-smoke": presentationSmoke = true; pixel = true; break;
                case "--presentation-trace" when i + 1 < args.Length: presentationTrace = Path.GetFullPath(args[++i]); break;
                case "--capture-title-layers" when i + 1 < args.Length: titleLayers = Path.GetFullPath(args[++i]); break;
                case "--psp-font" when i + 1 < args.Length: pspFont = Path.GetFullPath(args[++i]); break;
                case "--capture-flow" when i + 1 < args.Length: flowCapture = Path.GetFullPath(args[++i]); break;
                case "--main-menu-entry-frame" when i + 1 < args.Length:
                    menuEntryFrame = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--presentation-realtime" when i + 1 < args.Length: realtimeWalk = Path.GetFullPath(args[++i]); break;
                case "--main-menu-frames" when i + 1 < args.Length:
                    menuFrames = args[++i].Split(',').Select(pair => pair.Split(':')).ToDictionary(
                        pair => int.Parse(pair[0], System.Globalization.CultureInfo.InvariantCulture),
                        pair => int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--asset-diagnostics": assetDiagnostics = true; break;
                case "--asset-preview" when i + 1 < args.Length:
                    assetPreview = args[++i]; assetDiagnostics = true; break;
                case "--asset-screen" when i + 1 < args.Length:
                    diagnosticScreen = args[++i];
                    if (diagnosticScreen is not ("title" or "main-menu" or "deck-editor" or "duel" or "story" or "unassigned"))
                        throw new ArgumentException("Unknown diagnostic screen.");
                    assetDiagnostics = true; break;
                case "--smoke": smoke = true; break;
                case "--seed" when i + 1 < args.Length && uint.TryParse(args[i + 1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out seed): seedSpecified = true; i++; break;
                case "--replay" when i + 1 < args.Length: replay = Path.GetFullPath(args[++i]); break;
                case "--capture-result" when i + 1 < args.Length: resultCapture = Path.GetFullPath(args[++i]); break;
                case "--deck" when i + 1 < args.Length && int.TryParse(args[i + 1], out deckVariant) && deckVariant is >= 0 and <= 2: i++; break;
                case "--data-root" when i + 1 < args.Length: dataRoot = Path.GetFullPath(args[++i]); break;
                case "--setup-smoke" when i + 1 < args.Length: setupSource = Path.GetFullPath(args[++i]); break;
                case "--capture-editor" when i + 1 < args.Length: editorCapture = Path.GetFullPath(args[++i]); break;
                default: throw new ArgumentException("Usage: PC [--cache <cache>] [--pixel-exact] [--capture <new.png>] [--smoke] [--deck <0|1|2>] [--seed <uint>] [--replay <new.json>] [--capture-result <new.png>] [--asset-diagnostics] [--asset-screen <screen>] [--data-root <private-root>]");
            }
        }
        if (setupSource is not null && !smoke) throw new ArgumentException("--setup-smoke requires --smoke.");
        if (assetDiagnostics && smoke) throw new ArgumentException("--asset-diagnostics cannot run duel smoke automation.");
        if (presentationSmoke && (smoke || assetDiagnostics || setupSource is not null || capture is not null || editorCapture is not null))
            throw new ArgumentException("--presentation-smoke requires an isolated presentation-only launch.");
        if (flowCapture is not null && !presentationSmoke) throw new ArgumentException("--capture-flow requires --presentation-smoke.");
        if ((menuFrames is not null || menuEntryFrame is not null) && !presentationSmoke) throw new ArgumentException("--main-menu-frames and --main-menu-entry-frame require --presentation-smoke.");
        if (realtimeWalk is not null && (pixel || presentationSmoke || smoke || assetDiagnostics || setupSource is not null || capture is not null || editorCapture is not null))
            throw new ArgumentException("--presentation-realtime requires an isolated normal-speed launch.");
        if (presentationTrace is not null && !presentationSmoke) throw new ArgumentException("--presentation-trace requires --presentation-smoke.");
        if (titleLayers is not null && (capture is null || presentationSmoke || smoke || assetDiagnostics))
            throw new ArgumentException("--capture-title-layers requires a title --capture launch.");
        return new(cache, pixel, capture, smoke, seed, replay, resultCapture, deckVariant, dataRoot, setupSource, editorCapture, seedSpecified, assetDiagnostics, diagnosticScreen, assetPreview, presentationSmoke, presentationTrace, titleLayers, pspFont, flowCapture, menuFrames, menuEntryFrame, realtimeWalk);
    }
}
