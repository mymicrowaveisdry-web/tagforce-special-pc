using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal readonly record struct DuelFieldFrame(bool BeforeDeal, CardView? SummonedCard = null, Matrix? SummonWorld = null);

/// <summary>Tracks the PC opening and Normal Summon poses using viewer-authorized state.</summary>
internal sealed class DuelFieldPresentation(TimeSpan entered, bool enabled)
{
    internal static readonly TimeSpan OpeningHold = TimeSpan.FromMilliseconds(240);
    internal static readonly TimeSpan SummonTravel = TimeSpan.FromMilliseconds(360);
    internal static readonly TimeSpan SummonHold = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan OutcomeHold = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(2.5);

    private sealed record Summon(long Revision, CardView Card, Matrix From, Matrix To, TimeSpan Started);
    private Summon? summon;
    private TimeSpan? outcomeAt;
    private TimeSpan noticeAt;
    private string lastNotice = "";
    private long lastNoticeRevision = -1;

    public bool BeforeDeal(DuelView view, TimeSpan now) => enabled && view.Revision == 0 && now - entered < OpeningHold;

    public bool ResultReady(TimeSpan now) => !enabled || now - (outcomeAt ??= now) >= OutcomeHold;

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

    public void Accept(DuelView before, DuelView after, int choiceId, TimeSpan now)
    {
        if (!enabled || after.Revision <= before.Revision || before.Choices.FirstOrDefault(c => c.Id == choiceId) is not
            { Kind: ActionKind.Summon, FocusInstanceId: { } id, Zone: { } zone } choice || zone < 0 || zone >= after.Self.Monsters.Count)
            return;

        int handIndex = Array.FindIndex(before.Self.Hand.ToArray(), card => card.InstanceId == id);
        if (handIndex < 0 || DuelFieldCommands.Verb(choice, before.Self.Hand[handIndex]) != MenuVerb.Summon ||
            after.Self.Monsters[zone] is not { InstanceId: { } landedId, Position: BattlePosition.FaceUpAttack } landed || landedId != id ||
            !CardSpriteRenderer.ShowsFace(landed, out _))
            return;

        summon = new(after.Revision, landed, DuelLayoutProfile.HandWorld(handIndex, before.Self.Hand.Count, focused: true),
            DuelLayoutProfile.SlotWorld(true, false, zone, landed.Position), now);
    }

    public DuelFieldFrame Frame(DuelView view, TimeSpan now)
    {
        if (BeforeDeal(view, now)) return new(BeforeDeal: true);
        if (summon is not { } moving || view.Revision != moving.Revision || now - moving.Started >= SummonTravel + SummonHold)
            return default;

        float progress = (float)Math.Clamp((now - moving.Started) / SummonTravel, 0, 1);
        if (progress >= 1) return new(false, moving.Card);

        float eased = progress * progress * (3 - 2 * progress);
        var rotation = Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(moving.From), Quaternion.CreateFromRotationMatrix(moving.To), eased);
        var position = Vector3.Lerp(moving.From.Translation, moving.To.Translation, eased);
        position.Y += 42 * 4 * progress * (1 - progress);
        return new(false, moving.Card, Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(position));
    }
}
