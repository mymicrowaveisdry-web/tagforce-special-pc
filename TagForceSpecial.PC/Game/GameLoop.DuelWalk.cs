using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.PC.App.Screens.DuelField;

namespace TagForceSpecial.PC.Game;

/// <summary>The matched Normal Summon walk (<c>--duel-walk</c>): the real window, router, duel input and controller, fed the scenario's key
/// presses on fixed 1/60 s frames, one update per drawn frame. Each frame's picture is saved and traced with the press, revision, phase,
/// open window, focus and viewer-authorized public state; <c>--duel-walk-static</c> repeats it with animations off for the trace comparison.</summary>
public sealed partial class GameLoop
{
    private static readonly TimeSpan WalkStep = TimeSpan.FromSeconds(1 / 60d);
    private readonly List<object> walkTrace = [];
    private int walkUpdatedFrame = -1;
    private Keys? walkKey;
    /// <summary>The full viewer-authorized view at each revision (the gate compares these between animation settings).</summary>
    private readonly SortedDictionary<long, System.Text.Json.Nodes.JsonNode?> walkRevisions = [];

    private void StartDuelWalk()
    {
        var cache = InstalledGame.FromCatalogDirectory(options.CachePath!);
        duel = SupportedDuelFactory.Create(cache, DuelWalkScenario.PlayerDeck(), CurrentSeed, Core.PlayerId.One,
            opponentDeck: DuelWalkScenario.OpponentDeck());
        // The native walk duels Aki (captured names and faces) on the starfield backdrop (its choice is not understood: an earlier Free Duel
        // against Aki showed tf6).
        duelStyle = DuelFieldReference.States[0].Style with { Backdrop = "starfield" };
        RecordRevision(duel.View);
        screen = ScreenId.Duel;
        Directory.CreateDirectory(Path.Combine(options.DuelWalkRoot!, "frames"));
    }

    /// <summary>One update per drawn frame; false when this frame was already updated.</summary>
    private bool DuelWalkUpdate(ref GameTime time)
    {
        if (walkUpdatedFrame == frames) return false;
        walkUpdatedFrame = frames;
        time = new(WalkStep * frames, WalkStep);
        return true;
    }

    private App.Input.InputFrame DuelWalkInput()
    {
        walkKey = DuelWalkScenario.Presses.FirstOrDefault(p => p.Frame == frames) is { Frame: > 0 } press ? press.Key : null;
        return input.Poll(walkKey is { } key ? new KeyboardState(key) : new KeyboardState(), new GamePadState(), true);
    }

    private void RecordRevision(DuelUI.DuelView view)
    {
        if (!walkRevisions.ContainsKey(view.Revision)) walkRevisions[view.Revision] = System.Text.Json.JsonSerializer.SerializeToNode(view);
    }

    private void CheckDuelWalkFrame()
    {
        if (duel is not { } controller) return;
        var view = controller.View;
        RecordRevision(view);
        var pixels = new byte[480 * 272 * 4];
        canvas.GetData(pixels);
        string root = options.DuelWalkRoot!;
        using (var output = new FileStream(Path.Combine(root, "frames", $"{frames:0000}.png"), FileMode.CreateNew, FileAccess.Write))
            canvas.SaveAsPng(output, 480, 272);
        var focus = duelInput?.Focus is { Ready: true } f ? f.Current : (FieldObject?)null;
        var summon = duelInput is null ? default : duelFieldPresentation.Frame(view, PresentationTime);
        walkTrace.Add(new
        {
            frame = frames, pressed = walkKey?.ToString(), revision = view.Revision, turn = view.Turn, phase = view.Phase.ToString(),
            active = view.ActivePlayer.ToString(), modal = duelInput?.Modal?.GetType().Name,
            focus = focus is { } at ? new { row = at.Row, kind = at.Kind.ToString(), own = at.Own, index = at.Index, card = at.Card?.CardId } : null,
            choices = view.Choices.Select(c => c.Kind?.ToString()).Distinct().ToArray(),
            beforeDeal = summon.BeforeDeal, moving = summon.SummonWorld is not null,
            self = new { lp = view.Self.LifePoints, deck = view.Self.DeckCount, extra = view.Self.ExtraDeckCount, hand = view.Self.Hand.Select(c => c.CardId),
                monsters = view.Self.Monsters.Select(c => c?.CardId) },
            opponent = new { lp = view.Opponent.LifePoints, deck = view.Opponent.DeckCount, extra = view.Opponent.ExtraDeckCount,
                hand = view.Opponent.Hand.Count, hidden = view.Opponent.Hand.All(c => c.Hidden && c.CardId is null) },
            rgbaSha256 = Convert.ToHexStringLower(SHA256.HashData(pixels))
        });
        if (frames + 1 < DuelWalkScenario.Frames) return;
        using (var output = new FileStream(Path.Combine(root, "trace.json"), FileMode.CreateNew, FileAccess.Write))
            System.Text.Json.JsonSerializer.Serialize(output, new { scenario = "matched-normal-summon", animations = !options.DuelWalkStatic,
                seed = CurrentSeed, stepSeconds = WalkStep.TotalSeconds, presses = DuelWalkScenario.Presses.Select(p => new { p.Frame, key = p.Key.ToString() }),
                revisions = walkRevisions.Values, frames = walkTrace }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"DUEL_WALK frames={frames + 1} revision={view.Revision} hand={view.Self.Hand.Count} animations={!options.DuelWalkStatic}");
        Exit();
    }
}
