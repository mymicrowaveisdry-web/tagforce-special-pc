using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.Screens;

// No engine or DuelState reference is available to rendering.
internal static class DuelScreen
{
    private static readonly Color Gold = new(238, 196, 104);
    private static readonly Color Muted = new(143, 162, 187);

    internal static void Draw(SpriteRenderer r, DuelView view, int choice, int inspectedCard, string notice, double seconds)
    {
        r.Fill(new(0, 0, 480, 272), new(9, 15, 27), SceneLayer.Background);
        r.Fill(new(0, 0, 480, 28), new(24, 36, 55));
        r.Text("TAG FORCE / INITIAL DUEL", 8, 5, Gold);
        r.Text($"T{view.Turn} {view.Phase} / P{(int)view.ActivePlayer + 1}", 8, 16, Muted);
        r.Text($"YOU {view.Self.LifePoints}", 280, 5, new Color(104, 218, 214));
        r.Text($"CPU {view.Opponent.LifePoints}", 382, 5, new Color(238, 136, 123));
        r.Fill(new(6, 31, 332, 219), new(18, 36, 49));
        r.Border(new(6, 31, 332, 219), new(48, 78, 94), SceneLayer.Field);
        r.Fill(new(12, 127, 320, 1), new(48, 78, 94));
        var selected = view.Choices.Count > 0 ? view.Choices[Math.Clamp(choice, 0, view.Choices.Count - 1)] : null;
        int? focus = selected?.FocusInstanceId;
        var knownCards = view.Self.Hand.Concat(view.Self.Monsters.OfType<CardView>())
            .Concat(view.Self.SpellTraps.OfType<CardView>()).Concat(view.Self.Pendulum.OfType<CardView>())
            .Concat(view.Self.Graveyard).Concat(view.Self.FaceUpExtraDeck)
            .Concat(view.Opponent.Monsters.OfType<CardView>()).Concat(view.Opponent.SpellTraps.OfType<CardView>())
            .Concat(view.Opponent.Pendulum.OfType<CardView>()).Concat(view.Opponent.Graveyard)
            .Concat(view.Opponent.FaceUpExtraDeck).Concat(new[] { view.Self.FieldSpell, view.Opponent.FieldSpell }.OfType<CardView>())
            .Where(c => !c.Hidden).ToArray();
        var detail = inspectedCard >= 0 && knownCards.Length > 0 ? knownCards[inspectedCard % knownCards.Length]
            : knownCards.FirstOrDefault(c => c.InstanceId == focus);
        DrawHand(view.Opponent, 38);
        DrawRow(view.Opponent.SpellTraps, 63);
        DrawRow(view.Opponent.Monsters, 95);
        DrawRow(view.Self.Monsters, 134);
        DrawRow(view.Self.SpellTraps, 166);
        DrawHand(view.Self, 207);
        Pile(12, 96, "EX", view.Opponent.ExtraDeckCount + view.Opponent.FaceUpExtraDeck.Count);
        Pile(12, 134, "DK", view.Self.DeckCount);
        Pile(298, 96, "GY", view.Opponent.Graveyard.Count);
        Pile(298, 134, "GY", view.Self.Graveyard.Count);
        Pile(12, 63, "DK", view.Opponent.DeckCount);
        Pile(12, 166, "EX", view.Self.ExtraDeckCount + view.Self.FaceUpExtraDeck.Count);
        DrawCard(view.Opponent.Pendulum[0], new(12, 33, 30, 25));
        DrawCard(view.Opponent.Pendulum[1], new(298, 33, 30, 25));
        DrawCard(view.Self.Pendulum[0], new(12, 201, 30, 25));
        DrawCard(view.Self.Pendulum[1], new(298, 201, 30, 25));
        DrawCard(view.Opponent.FieldSpell, new(298, 63, 30, 25));
        DrawCard(view.Self.FieldSpell, new(298, 166, 30, 25));
        r.Text("P", 23, 229, Muted); r.Text("P", 309, 229, Muted);
        r.Fill(new(344, 31, 130, 219), new(22, 29, 44));
        r.Text("CARD DETAILS", 350, 37, Gold);
        if (detail is not null)
        {
            r.Wrapped(detail.Name, 350, 51, 20, 2);
            r.Text($"ATK {detail.Attack?.ToString() ?? "?"}", 350, 73, Muted);
            r.Text($"DEF {detail.Defense?.ToString() ?? "?"}", 350, 83, Muted);
            r.Wrapped(detail.Description, 350, 96, 20, 5, Muted);
        }
        else r.Wrapped("Select an action or press TAB to inspect visible cards.", 350, 53, 20, 5, Muted);
        r.Wrapped(view.Prompt, 350, 146, 20, 2, Gold);
        const int pageSize = 4;
        int start = choice / pageSize * pageSize;
        for (int i = start; i < Math.Min(start + pageSize, view.Choices.Count); i++)
        {
            int y = 168 + (i - start) * 17;
            if (i == choice) r.Fill(new(348, y - 2, 124, 17), new(49, 65, 79));
            r.Wrapped(view.Choices[i].Label, 350, y, 20, 2, i == choice ? Gold : Color.White);
        }
        r.Text(view.Choices.Count == 0 ? "WAIT" : $"{choice + 1}/{view.Choices.Count}  UP/DOWN", 350, 240, Muted);
        r.Text("ENTER/A SELECT  TAB/Y INSPECT  ESC/B MENU", 8, 256, Muted);
        r.Text(notice.Length > 77 ? notice[..77] : notice, 8, 265, Gold);
        if (view.Outcome is not null) r.Text(view.Prompt, 122, 118, Gold, 2);

        void DrawRow(IReadOnlyList<CardView?> cards, int y)
        {
            for (int i = 0; i < 5; i++) DrawCard(cards[i], new(47 + i * 49, y, 44, 28));
        }
        void DrawHand(PlayerView player, int y)
        {
            int step = Math.Min(36, 242 / Math.Max(1, player.Hand.Count));
            for (int i = 0; i < player.Hand.Count; i++) DrawCard(player.Hand[i], new(47 + i * step, y, step - 2, 23));
        }
        void DrawCard(CardView? card, Rectangle rect)
        {
            r.Fill(rect, card is null ? new(13, 27, 39) : card.Hidden ? new(80, 46, 37) : new(91, 82, 63), SceneLayer.Cards);
            r.Border(rect, card is null ? new(46, 65, 78) : new(177, 147, 100), SceneLayer.Cards);
            if (card is null) return;
            if (card.Hidden) r.Text("?", rect.X + rect.Width / 2 - 3, rect.Y + 8, Gold);
            else
            {
                int columns = Math.Max(1, (rect.Width - 4) / 6);
                r.Wrapped(card.Name, rect.X + 2, rect.Y + 3, columns, 2);
                if (card.Position == Core.BattlePosition.FaceDownDefense) r.Text("SET", rect.X + 2, rect.Bottom - 8, Gold);
            }
            if (detail?.InstanceId is { } id && card.InstanceId == id)
                r.Border(rect, seconds % 1 < .7 ? Color.Cyan : Gold);
        }
        void Pile(int x, int y, string label, int count)
        {
            r.Fill(new(x, y, 30, 28), new(32, 44, 58));
            r.Text(label, x + 3, y + 3, Muted); r.Text(count.ToString(), x + 3, y + 15);
        }
    }
}
