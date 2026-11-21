using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>One presented frame. During a Normal Summon: the card's pose (<paramref name="SummonWorld"/>, null once landed), the hand as it
/// was (<paramref name="HandBefore"/>) moving to its new layout by <paramref name="HandProgress"/>, the menu still shown after ○
/// (<paramref name="ClosingMenu"/>), the hidden pointer, the landing glow frame and whether the card has left the hand (<paramref name="Flying"/>:
/// no cursor, no hand Levels).</summary>
public readonly record struct DuelFieldFrame(bool BeforeDeal, CardView? SummonedCard = null, Matrix? SummonWorld = null,
    IReadOnlyList<CardView>? HandBefore = null, float HandProgress = 0, CardMenuModal? ClosingMenu = null, bool PointerHidden = false,
    int? GlowFrame = null, int GlowZone = 0, bool Flying = false);

/// <summary>Tracks the PC opening and Normal Summon presentation using viewer-authorized state. Measured Normal Summon (matched walk,
/// order33-walk, vblanks at 59.94 Hz after ○ on Summon; the original updates every 2 vblanks): the menu stays open 26 vblanks, the pointer is
/// hidden from 32 to 88, the card waits raised in the hand until 48, is at the mid pose (t = 0.5128 of position and tilt) from 50 and lands
/// at 52 while the other hand cards close the gap in the same two steps; the landing glow plays 12 game frames from 52.</summary>
public sealed class DuelFieldPresentation(TimeSpan entered, bool enabled)
{
    public static readonly TimeSpan OpeningHold = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan OutcomeHold = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(2.5);
    public const double VblankHz = 59.94;
    public const int MenuClose = 26, PointerHide = 32, Flight = 48, MidPose = 50, Landed = 52, GlowFrames = 12, PointerBack = 88;
    public const float MidProgress = 0.5128f;

    private sealed record Summon(long Revision, CardView Card, IReadOnlyList<CardView> Hand, int Zone, Matrix From, Matrix To, TimeSpan Started,
        CardMenuModal? Menu);
    private Summon? summon;
    private TimeSpan? outcomeAt;
    private TimeSpan noticeAt;
    private string lastNotice = "";
    private long lastNoticeRevision = -1;

    public bool BeforeDeal(DuelView view, TimeSpan now) => enabled && view.Revision == 0 && now - entered < OpeningHold;

    public bool ResultReady(TimeSpan now) => !enabled || now - (outcomeAt ??= now) >= OutcomeHold;

    /// <summary>Cosmetic input lock (UI policy): the original takes no input until the pointer returns after a Normal Summon.</summary>
    public bool Busy(DuelView view, TimeSpan now) => Vblanks(view, now) is < PointerBack;

    public string Notice(long revision, string text, TimeSpan now)
    {
        if (revision != lastNoticeRevision || text != lastNotice)
        {
            lastNoticeRevision = revision;
            lastNotice = text;
            noticeAt = now;
        }
        return now - noticeAt < NoticeHold ? text : "";
    }

    public static DuelView BeforeOpeningDeal(DuelView view) => view with { Self = DeckBeforeDeal(view.Self), Opponent = DeckBeforeDeal(view.Opponent) };

    private static PlayerView DeckBeforeDeal(PlayerView side) => side with { DeckCount = side.DeckCount + side.Hand.Count, Hand = [] };

    public void Accept(DuelView before, DuelView after, int choiceId, TimeSpan now, CardMenuModal? menu = null)
    {
        if (!enabled || after.Revision <= before.Revision || before.Choices.FirstOrDefault(c => c.Id == choiceId) is not
            { Kind: ActionKind.Summon, FocusInstanceId: { } id, Zone: { } zone } choice || zone < 0 || zone >= after.Self.Monsters.Count)
            return;

        int handIndex = Array.FindIndex(before.Self.Hand.ToArray(), card => card.InstanceId == id);
        if (handIndex < 0 || DuelFieldCommands.Verb(choice, before.Self.Hand[handIndex]) != MenuVerb.Summon ||
            after.Self.Monsters[zone] is not { InstanceId: { } landedId, Position: BattlePosition.FaceUpAttack } landed || landedId != id ||
            !CardSpriteRenderer.ShowsFace(landed, out _))
            return;

        summon = new(after.Revision, landed, before.Self.Hand, zone, DuelLayoutProfile.HandWorld(handIndex, before.Self.Hand.Count, focused: true),
            DuelLayoutProfile.SlotWorld(true, false, zone, landed.Position), now, menu?.Target.Card?.InstanceId == id ? menu : null);
    }

    /// <summary>Vblanks since ○ on Summon while the summon is being presented; null otherwise.</summary>
    private double? Vblanks(DuelView view, TimeSpan now) =>
        summon is { } s && view.Revision == s.Revision && (now - s.Started).TotalSeconds * VblankHz is var v && v < PointerBack ? v : null;

    public DuelFieldFrame Frame(DuelView view, TimeSpan now)
    {
        if (BeforeDeal(view, now)) return new(BeforeDeal: true);
        if (Vblanks(view, now) is not { } v || summon is not { } s) return default;
        var menu = v < MenuClose ? s.Menu : null;
        bool pointerHidden = v >= PointerHide;
        int? glow = v >= Landed && v < Landed + 2 * GlowFrames ? (int)((v - Landed) / 2) : null;
        if (v >= Landed) return new(false, s.Card, null, null, 1, menu, pointerHidden, glow, s.Zone);
        float t = v < MidPose ? 0 : MidProgress;
        var rotation = Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(s.From), Quaternion.CreateFromRotationMatrix(s.To), t);
        var world = Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(Vector3.Lerp(s.From.Translation, s.To.Translation, t));
        // Before the flight the card is still drawn raised at its hand place, among the hand as it was.
        return new(false, s.Card, world, s.Hand, v < Flight ? 0 : t, menu, pointerHidden, null, s.Zone, Flying: v >= Flight);
    }
}
