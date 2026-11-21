using Microsoft.Xna.Framework;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Public facts the field prints on a face-up card: whether it is a monster, its Level and whether it is a Tuner.</summary>
public sealed record DuelCardFacts(bool Monster, int Level, bool Tuner, bool Trap = false, int Rank = 0)
{
    /// <summary>From the decoded codes: raw kinds 13/14 are Spells/Traps; Tuner getter kinds are the ones Core treats as Tuners. Xyz
    /// monsters (raw kinds 22/23) report no Level: no capture shows what the field prints for their Rank. Their Rank is kept for the card list,
    /// which shows it with the rank star (measured).</summary>
    public static DuelCardFacts From(CardDefinition card) => new(card.Codes is { RawKind: not (13 or 14) } codes,
        card.Codes?.RawKind is 22 or 23 ? 0 : (int)card.Statistics.Level, card.Codes?.GetterKind is 15 or 16 or 19 or 30 or 32, card.Codes?.RawKind == 14,
        card.Codes?.RawKind is 22 or 23 ? (int)card.Statistics.Level : 0);
}

/// <summary>Where a card was drawn: its world matrix and whether it is in the player's hand.</summary>
public sealed record DuelCardPlacement(CardView Card, Matrix World, bool Hand);

/// <summary>Draws one card slot: empty, a hidden back, or a public face. A hidden card never contributes its name or text.</summary>
public static class CardSpriteRenderer
{
    public const string FaceKeyPrefix = "card.face:";

    /// <summary>Texture key of a public card's 64 × 64 cardm face.</summary>
    public static string FaceKey(uint cardId) => FaceKeyPrefix + cardId;

    /// <summary>Original card quad at <paramref name="world"/>: the back for hidden or face-down cards (no identity is used), else the face.</summary>
    public static void DrawOriginal(DuelDrawList list, CardView card, Matrix world)
    {
        if (ShowsFace(card, out uint id)) list.Field(DuelPart.Cards, "card.face", 0, new(World: world, Texture: FaceKey(id)));
        else list.Field(DuelPart.Cards, "card.back", 0, new(World: world));
    }

    /// <summary>Original order (GE captures): the player's field, the player's hand, the opponent's field, the opponent's hand.
    /// <paramref name="focused"/> is the hand card raised by the cursor. Returns where each card was drawn.</summary>
    public static IReadOnlyList<DuelCardPlacement> DrawFieldOriginal(DuelDrawList list, PC.DuelUI.DuelView view, int? focused) =>
        DrawFieldOriginal(list, view, focused, default);

    internal static IReadOnlyList<DuelCardPlacement> DrawFieldOriginal(DuelDrawList list, PC.DuelUI.DuelView view, int? focused, DuelFieldFrame presentation)
    {
        var placed = new List<DuelCardPlacement>();
        void Place(CardView card, Matrix world, bool hand)
        {
            DrawOriginal(list, card, world);
            placed.Add(new(card, world, hand));
        }
        foreach (var (player, owner) in new[] { (true, view.Self), (false, view.Opponent) })
        {
            for (int i = 0; i < owner.Monsters.Count; i++)
                if (owner.Monsters[i] is { } card)
                    Place(card, player && presentation.SummonedCard?.InstanceId == card.InstanceId && presentation.SummonWorld is { } world
                        ? world : DuelLayoutProfile.SlotWorld(player, false, i, card.Position), false);
            for (int i = 0; i < owner.SpellTraps.Count; i++)
                if (owner.SpellTraps[i] is { } card) Place(card, DuelLayoutProfile.SlotWorld(player, true, i, null), false);
            for (int i = 0; i < owner.Pendulum.Count; i++)
                if (owner.Pendulum[i] is { } card) Place(card, DuelLayoutProfile.PendulumWorld(player, i), false);
            if (owner.FieldSpell is { } fieldSpell) Place(fieldSpell, DuelLayoutProfile.FieldSpellWorld(player), false);
            for (int i = 0; i < owner.Hand.Count; i++)
            {
                var card = owner.Hand[i];
                Place(card, player ? DuelLayoutProfile.HandWorld(i, owner.Hand.Count, card.InstanceId is { } id && id == focused)
                    : DuelLayoutProfile.OpponentHandWorld(i, owner.Hand.Count), player);
            }
        }
        return placed;
    }

    /// <summary>Level numerals of face-up monsters on the field and in the player's hand, after the cursor (GE order).</summary>
    public static void DrawLevels(DuelDrawList list, DuelFieldProfile field, IEnumerable<DuelCardPlacement> placed, Func<uint, DuelCardFacts?>? facts)
    {
        foreach (var p in placed)
        {
            bool fieldMonster = !p.Hand && p.Card.Position is not null;
            if (p.Hand || fieldMonster) DrawLevel(list, field, p.Card, p.World, p.Hand ? DuelLayoutProfile.HandLevelAnchor : DuelLayoutProfile.FieldLevelAnchor, facts);
        }
    }

    public static bool ShowsFace(CardView card, out uint cardId)
    {
        cardId = card.CardId ?? 0;
        return !card.Hidden && !card.FaceDown && card.CardId is not null;
    }

    /// <summary>The Level numeral at a card-local anchor: yellow for Tuners, white otherwise; face-up monsters only.</summary>
    public static void DrawLevel(DuelDrawList list, DuelFieldProfile field, CardView card, Matrix world, Vector3 anchor, Func<uint, DuelCardFacts?>? facts)
    {
        if (!ShowsFace(card, out uint id) || facts?.Invoke(id) is not { Monster: true, Level: > 0 } fact) return;
        FieldCounters.Draw(list, DuelPart.Cards, field, fact.Level, Vector3.Transform(anchor, world), fact.Tuner ? FieldCounters.Tuner : FieldCounters.White);
    }

    public static void Draw(DuelDrawList list, CardView? card, Rectangle rect)
    {
        list.Fill(DuelPart.Cards, rect, card is null ? new(13, 27, 39) : card.Hidden ? new(80, 46, 37) : new(91, 82, 63), SceneLayer.Cards);
        list.Border(DuelPart.Cards, rect, card is null ? new(46, 65, 78) : new(177, 147, 100), SceneLayer.Cards);
        if (card is null) return;
        if (card.Hidden)
        {
            list.Text(DuelPart.Cards, "?", rect.X + rect.Width / 2 - 3, rect.Y + 8, DuelPalette.Gold);
            return;
        }
        int columns = Math.Max(1, (rect.Width - 4) / 6);
        list.Wrapped(DuelPart.Cards, card.Name, rect.X + 2, rect.Y + 3, columns, 2);
        if (card.Position == BattlePosition.FaceDownDefense) list.Text(DuelPart.Cards, "SET", rect.X + 2, rect.Bottom - 8, DuelPalette.Gold);
    }
}
