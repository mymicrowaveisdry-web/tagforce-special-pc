using System.Text.Json;
using Godot;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Setup;
using FileAccess = System.IO.FileAccess;
using Path = System.IO.Path;

namespace TagForceSpecial.PC.Host;

/// <summary>Runs the scripted title route against isolated native saves and optionally captures each distinct view.</summary>
internal sealed class TitlePresentationSmoke : IDisposable
{
    private readonly record struct TraceStep(int Cycle, int Frame, string Pressed, string Held, string Expected, string State, int Saves);
    private readonly (PhysicalKey? Key, string Expected)[] steps = PresentationRoute.Build();
    private readonly List<TraceStep> trace = [];
    private readonly HashSet<string> captures = new(StringComparer.Ordinal);
    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory("tfsp-title-smoke-");
    private readonly string? tracePath, captureRoot;
    private TitleRoute route;
    private PlayerProfileStore profiles = null!;
    private InputEdges edges = null!;
    private PlayerProfile? lastSave;
    private int cycle, frame, saves, asserted, settleFrames;
    private string? pendingCapture;
    public TitleRouteView View { get; }

    public TitlePresentationSmoke(string? tracePath, string? captureRoot)
    {
        this.tracePath = tracePath;
        this.captureRoot = captureRoot;
        route = StartCycle();
        View = new TitleRouteView(route, KeyBindings.Default);
    }

    private TitleRoute StartCycle()
    {
        frame = saves = 0;
        lastSave = null;
        edges = new InputEdges();
        profiles = new PlayerProfileStore(new PrivateStorage(Path.Combine(scratch.FullName, $"cycle-{cycle + 1}")));
        return new TitleRoute(new TitleProfileHost(profiles), "Duelist", exact: true)
        {
            ScriptedStorage = () => steps[frame].Expected.StartsWith("StorageCheck/", StringComparison.Ordinal)
        };
    }

    public bool Advance(PspCanvas canvas, TimeSpan delta)
    {
        if (pendingCapture is { } name)
        {
            if (--settleFrames > 0) return false;
            using var picture = canvas.Capture();
            string target = Path.Combine(captureRoot!, name + ".png");
            if (File.Exists(target) || picture is null || picture.IsEmpty() || picture.SavePng(target) != Error.Ok)
                throw new IOException($"Could not write a new title capture at {target}.");
            pendingCapture = null;
        }

        // Without capture requests, advance several fixed steps per Godot frame.
        for (int budget = 256; budget > 0; budget--)
        {
            if (frame == steps.Length)
            {
                if (saves != 3) throw new InvalidOperationException($"Cycle {cycle + 1} completed {saves} saves, expected 3.");
                if (++cycle == PresentationRoute.Cycles)
                {
                    WriteTrace();
                    return true;
                }
                route = StartCycle();
                View.ShowRoute(route);
            }
            Tick(delta);
            if (pendingCapture is not null) return false;
        }
        return false;
    }

    private void Tick(TimeSpan delta)
    {
        var (key, expected) = steps[frame];
        string before = route.Label();
        var input = edges.Next(PresentationRoute.Held(key), false, true);
        route.Tick(input, delta);
        string state = route.Label();
        if (expected != PresentationRoute.AnyState)
        {
            if (state != expected)
                throw new InvalidOperationException($"Cycle {cycle + 1}, frame {frame}, {key}: expected {expected}, got {state}.");
            asserted++;
            VerifyProfile(before, state);
            if (captureRoot is not null && cycle == 0 && captures.Add(state))
            {
                Directory.CreateDirectory(captureRoot);
                pendingCapture = state.Replace('/', '-').Replace(':', '-');
                settleFrames = 3;
                View.QueueRedraw();
            }
        }
        trace.Add(new(cycle + 1, frame, input.Pressed.ToString(), input.Held.ToString(), expected, state, saves));
        frame++;
    }

    private void VerifyProfile(string before, string after)
    {
        var flow = route.Flow;
        if (before != after && flow.Step == TitleFlowStep.SystemSaveDone)
        {
            if (!profiles.TryLoad() || profiles.Current is not { Options: { } savedOptions } saved ||
                saved.Name != flow.Name || saved.Level != flow.Level || !savedOptions.Values.SequenceEqual(flow.SessionOptions.Values))
                throw new InvalidOperationException($"Cycle {cycle + 1}, frame {frame}: the saved profile differs from the session.");
            lastSave = saved;
            saves++;
        }
        if (profiles.Present != (lastSave is not null))
            throw new InvalidOperationException($"Cycle {cycle + 1}, frame {frame}: unexpected profile existence.");
        if (before.StartsWith("StorageCheck/", StringComparison.Ordinal) && flow.Step == TitleFlowStep.MainMenu &&
            (lastSave is not { Options: { } loadedOptions } loaded || flow.Name != loaded.Name || flow.Level != loaded.Level ||
             !flow.SessionOptions.Values.SequenceEqual(loadedOptions.Values)))
            throw new InvalidOperationException($"Cycle {cycle + 1}, frame {frame}: Continue did not restore the profile.");
    }

    private void WriteTrace()
    {
        if (tracePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
            using var output = new FileStream(tracePath, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(output, new
            {
                cycles = PresentationRoute.Cycles, stepsPerCycle = steps.Length, frames = trace.Count,
                asserted, captures = captures.Count, input = trace
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        }
        GD.Print($"PRESENTATION_SMOKE cycles={cycle} steps={steps.Length} asserted={asserted} saves-per-cycle={saves} captures={captures.Count}");
    }

    public void Dispose()
    {
        foreach (var folder in scratch.EnumerateDirectories())
        {
            File.Delete(Path.Combine(folder.FullName, "profile.json"));
            folder.Delete();
        }
        scratch.Delete();
    }
}
