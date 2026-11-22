using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Developer verification only: renders the measured field layer for the states of the original GE captures, so each PNG can be
/// compared pixel by pixel with the matching native screenshot. Never part of the player UI.</summary>
public static class DuelFieldReference
{
    /// <summary>Capture name, backdrop piece, phase and turn of each reference state; the player is the turn player in every capture.</summary>
    public static IReadOnlyList<(string Name, DuelFieldStyle Style, DuelPhase Phase, int Turn)> States { get; } =
    [
        ("aki-board", new("tf6", "RJ ANDA", "Aki Izayoi", "fw0_PLAYER_0", "fw3_aki01"), DuelPhase.Main1, 2),
        ("joey-board", new("default", "PPSSPP", "Joey Wheeler", "fw0_PLAYER_0", "fw1_JONOUCHI_00"), DuelPhase.Battle, 3),
        ("yuya-chain", new("tf4", "RJ ANDA", "Yuya Sakaki", "fw0_PLAYER_0", "fw5_YUYA_00"), DuelPhase.Main1, 1)
    ];

    public static void Capture(DuelFieldVisuals field, string root, Func<uint, DuelCardFacts?>? facts)
    {
        Directory.CreateDirectory(root);
        foreach (var (name, style, phase, turn) in States)
        {
            var view = name == "aki-board" ? AkiBoard() : EmptyView(phase, turn);
            var list = DuelScene.Compose(new(view, 0, -1, "", 0, style, facts), field.Profile);
            field.Render(list.FieldDraws);
            using var output = new FileStream(Path.Combine(root, name + ".field.png"), FileMode.CreateNew, FileAccess.Write);
            field.Layer.SaveAsPng(output, 480, 272);
        }
    }

    /// <summary>The aki-board capture: Gusto Gulldo and a set card on the player's centre slots, four cards in hand with Caam raised by the
    /// cursor, Lonefire Blossom and a set card on Aki's centre slots, three cards in her hand; Decks 36 and 37, Extra Decks 15.</summary>
    public static DuelView AkiBoard()
    {
        static CardView Face(int instance, uint id, BattlePosition? position = null) => new(instance, id, "", "", null, null, position, false);
        static CardView Hidden(BattlePosition? position = null) => new(null, null, "Hidden card", "", null, null, position, true);
        var self = new PlayerView(PlayerId.One, 8000, 36, 15,
            [Face(1, 9167), Face(2, 9167), Face(3, 5290), Face(4, 9168)],
            [null, null, Face(5, 9164, BattlePosition.FaceUpAttack), null, null],
            [null, null, new CardView(6, 9167, "", "", null, null, null, false, FaceDown: true), null, null], new CardView?[2], [], []);
        var opponent = new PlayerView(PlayerId.Two, 8000, 37, 15, [Hidden(), Hidden(), Hidden()],
            [null, null, Face(7, 7568, BattlePosition.FaceUpAttack), null, null],
            [null, null, Hidden(), null, null], new CardView?[2], [], []);
        return new(0, PlayerId.One, PlayerId.One, 2, DuelPhase.Main1, self, opponent, "", [new ChoiceView(0, "", 4)], null);
    }

    /// <summary>The left panel for the inspected cards of the native captures: Krebons (joey-board) and Contact with Gusto (aki-board), focused
    /// in hand, and a hidden opponent hand card (back).</summary>
    public static void CapturePanels(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, Func<uint, DuelCardFacts?>? facts)
    {
        foreach (var (name, id) in new[] { ("panel-krebons", 7716u), ("panel-contact", 9183u), ("panel-hidden", 0u) })
        {
            var view = AkiBoard();
            var hand = view.Self.Hand.ToArray();
            if (id != 0) hand[3] = new CardView(4, id, "", "", null, null, null, false);
            view = view with { Self = view.Self with { Hand = hand }, Choices = [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(1, "End turn", null, ActionKind.EndTurn)] };
            var input = new DuelFieldInput();
            input.Handle(view, default, TimeSpan.FromSeconds(1 / 59.94));
            if (id == 0) input.Handle(view, new(InputAction.Down, InputAction.Down), TimeSpan.FromSeconds(1 / 59.94));
            Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, name + ".png"));
        }
    }

    /// <summary>The attack-target capture (ge-aki-attack): Battle Phase, Gusto Gulldo (500/500) attacking Lonefire Blossom (500/1400) through
    /// ○ → Attack, with the trail at update 37 (second lap, counter 45 = the capture's alpha phase 6).</summary>
    public static void CaptureAttack(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, CardCatalog catalog) => CaptureAttack(device, renderer, field, root, catalog, 2, 2, "attack-target.png");

    internal static void CaptureAttack(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, CardCatalog catalog, int from, int to, string filename)
    {
        var board = AkiBoard();
        var self = new CardView?[5]; self[from] = Describe(board.Self.Monsters[2]!) with { Attack = 500, Defense = 500 };
        var opponent = new CardView?[5]; opponent[to] = Describe(board.Opponent.Monsters[2]!) with { Attack = 500, Defense = 1400 };
        var view = board with
        {
            Phase = DuelPhase.Battle, Self = board.Self with { Monsters = self }, Opponent = board.Opponent with { Monsters = opponent },
            Choices = [new(0, "Attack Lonefire Blossom", 5, ActionKind.Attack, TargetZone: to), new(1, "End turn", null, ActionKind.EndTurn)]
        };
        var input = new DuelFieldInput();
        var frame = TimeSpan.FromSeconds(1 / 59.94);
        input.Handle(view, default, frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        input.Handle(view, default, TimeSpan.FromSeconds(37.5 * AttackTrail.VblanksPerUpdate / 59.94) - frame);
        Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, id => DuelCardFacts.From(catalog[id]), input), field.Profile),
            Path.Combine(root, filename));

        CardView Describe(CardView card)
        {
            var definition = catalog[card.CardId!.Value];
            return card with { Name = definition.Name, Description = definition.Description };
        }
    }

    /// <summary>The card-list capture (order32-gallery g-open / h04): the own Extra Deck list opened through ○ on the Extra Deck and ○ on Show
    /// Card List, 15 cards led by the five the native list shows first; then Close focused.</summary>
    public static void CaptureCardList(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, CardCatalog catalog, Func<uint, DuelCardFacts?>? facts)
    {
        string[] first = ["Ultimate Axon Kicker", "Stardust Dragon", "Daigusto Eguls", "Black Rose Dragon", "Daigusto Sphreez"];
        var known = first.Select(n => catalog.Cards.First(c => c.Name == n)).ToList();
        var extra = Enumerable.Range(0, 15).Select(i => known[i % known.Count])
            .Select((c, i) => new CardView(100 + i, c.GameCardId, c.Name, c.Description, c.Statistics.Attack, c.Statistics.Defense, null, false)).ToArray();
        var board = AkiBoard();
        var view = board with
        {
            Self = board.Self with { ExtraDeckCount = extra.Length, ExtraDeck = extra },
            Choices = [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(1, "End turn", null, ActionKind.EndTurn)]
        };
        var input = new DuelFieldInput();
        var frame = TimeSpan.FromSeconds(1 / 59.94);
        input.Handle(view, default, frame);
        input.Focus.FocusOn(DuelFieldRows.Build(view).SelectMany(r => r).First(o => o.Own && o.Kind == FieldObjectKind.Extra));
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "cardlist-open.png"));
        input.Handle(view, new(InputAction.Down, InputAction.Down), frame);
        Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "cardlist-close.png"));
    }

    /// <summary>The decision screens of Gusto Gulldo's Deck summon (order32-tribute-3 v-01/v-02/v-04): the card list in selection mode led by
    /// Gusto Squirro (opened, then marked with OK focused) and the battle position screen for it.</summary>
    public static void CaptureDecisionScreens(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, CardCatalog catalog, Func<uint, DuelCardFacts?>? facts)
    {
        var squirro = catalog.Cards.First(c => c.Name == "Gusto Squirro");
        var others = catalog.Cards.Where(c => c.Name.StartsWith("Gusto ", StringComparison.Ordinal) && c.GameCardId != squirro.GameCardId).Take(4);
        CardView Public(CardDefinition c, int instance) => new(instance, c.GameCardId, c.Name, c.Description, c.Statistics.Attack, c.Statistics.Defense, null, false);
        var options = new[] { squirro }.Concat(others).Select((c, i) => new ChoiceView(i, "Choose " + c.Name, 200 + i, ActionKind.ChooseCard) { Subject = Public(c, 200 + i), SubjectSource = DecisionCardSource.Deck }).ToArray();
        var board = AkiBoard();
        var view = board with { Revision = 10, Choices = options };
        var input = new DuelFieldInput();
        var frame = TimeSpan.FromSeconds(1 / 59.94);
        input.Handle(view, default, frame);
        Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "select-open.png"));
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "select-marked.png"));
        var subject = Public(squirro, 200);
        var position = board with
        {
            Revision = 11,
            Choices = [new(0, "FaceUpAttack", null, ActionKind.ChoosePosition, Position: BattlePosition.FaceUpAttack) { Subject = subject },
                new(1, "FaceUpDefense", null, ActionKind.ChoosePosition, Position: BattlePosition.FaceUpDefense) { Subject = subject }]
        };
        input.Handle(position, default, frame);
        Save(device, renderer, field, DuelScene.Compose(new(position, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "position-atk.png"));

        // Tribute Summon of Winda (hand) over Gusto Squirro in zone 3, compared with order32-win-5 s2 (prompt) and s3 (selection).
        var monsters = board.Self.Monsters.ToArray();
        monsters[2] = new CardView(5, squirro.GameCardId, "", "", null, null, BattlePosition.FaceUpAttack, false);
        var tribute = board with
        {
            Revision = 12, Self = board.Self with { Monsters = monsters },
            Choices = [new(0, "Tribute Summon Winda - use zone 3", 1, ActionKind.Summon, TargetZone: 2004, Zone: 2),
                new(1, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(2, "End turn", null, ActionKind.EndTurn)]
        };
        input = new DuelFieldInput();
        input.Handle(tribute, default, frame);
        input.Focus.FocusOn(input.Focus.Row(DuelFieldRows.OwnBottom).First(slot => slot.Card?.InstanceId == 1));
        input.Handle(tribute, new(InputAction.Confirm, InputAction.Confirm), frame);
        input.Handle(tribute, new(InputAction.Confirm, InputAction.Confirm), frame);
        Save(device, renderer, field, DuelScene.Compose(new(tribute, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "tribute-prompt.png"));
        input.Handle(tribute, new(InputAction.Confirm, InputAction.Confirm), frame);
        Save(device, renderer, field, DuelScene.Compose(new(tribute, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "tribute-select.png"));

        // Turn 3's Draw prompt (order34-closure draw-prompt): the Deck's one-row Draw menu.
        var draw = board with { Revision = 13, Turn = 3, Phase = DuelPhase.Draw, Choices = [new(0, "Draw / begin turn", null, ActionKind.Draw)] };
        input = new DuelFieldInput();
        input.Handle(draw, default, frame);
        Save(device, renderer, field, DuelScene.Compose(new(draw, 0, -1, "", 0, States[0].Style with { Backdrop = "starfield" }, facts, input), field.Profile),
            Path.Combine(root, "draw-prompt.png"));
    }

    /// <summary>Show Details of Caam (aki-board, focused in hand) through ○ → Show Details, compared with ge-aki-detail.png (counters
    /// differ: the capture shows the owner's save).</summary>
    public static void CaptureDetail(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, Func<uint, DuelCardFacts?>? facts, Func<CardView, App.Screens.Album.CardAlbumDetail?> detail)
    {
        var view = AkiBoard() with { Choices = [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(1, "End turn", null, ActionKind.EndTurn)] };
        var input = new DuelFieldInput { DetailFactory = detail };
        var frame = TimeSpan.FromSeconds(1 / 59.94);
        input.Handle(view, default, frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), frame);
        Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile), Path.Combine(root, "detail-caam.png"));
    }

    internal static void CaptureDecisions(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, Func<uint, DuelCardFacts?>? facts)
    {
        var view = AkiBoard() with
        {
            Choices = [.. Enumerable.Range(0, 5).Select(zone => new ChoiceView(zone, $"Target opposing Spell/Trap zone {zone + 1}",
                Kind: ActionKind.ChooseTarget, TargetZone: zone)), new(5, "Target opposing Pendulum scale", Kind: ActionKind.ChooseTarget),
                new(6, "Finish choosing", Kind: ActionKind.FinishChoice)]
        };
        var input = new DuelFieldInput();
        var frame = TimeSpan.FromSeconds(1 / 60.0);
        input.Handle(view, default, frame);
        input.Handle(view, new(InputAction.Up, InputAction.Up), frame);
        Capture("decision-list");
        input.Handle(view, new(InputAction.Back, InputAction.Back), frame);
        Capture("decision-required");
        input.Rejected("That choice has expired");
        Capture("action-rejected");
        view = view with { Choices = [], Outcome = new(PlayerId.One, PlayerId.Two, DuelEndReason.LifePointsDepleted),
            Opponent = view.Opponent with { LifePoints = 0 } };
        Capture("outcome-win");
        view = view with { Outcome = new(PlayerId.Two, PlayerId.One, DuelEndReason.LifePointsDepleted) };
        Capture("outcome-loss");
        var loss = DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile);
        var surrendered = new DuelDrawList();
        foreach (var draw in loss.Draws)
            surrendered.Add(draw is DuelOutcomeReason ? new DuelOutcomeReason(DuelPart.Effects, 402182) : draw);
        Save(device, renderer, field, surrendered, Path.Combine(root, "outcome-surrender.png"));

        void Capture(string name) => Save(device, renderer, field, DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile),
            Path.Combine(root, name + ".png"));
    }

    internal static void Save(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field, DuelDrawList list, string path)
    {
        field.Render(list.FieldDraws);
        using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 480, 272);
        device.SetRenderTarget(target);
        device.Clear(Microsoft.Xna.Framework.Color.Black);
        renderer.Begin(); renderer.Layer(field.Layer); list.Replay(renderer, field); renderer.End();
        device.SetRenderTarget(null);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        target.SaveAsPng(output, 480, 272);
    }

    /// <summary>Scripted presses on the aki-board state, each rendered as the full composite (field layer and interim windows).</summary>
    public static void CaptureInputWalk(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field,
        string root, Func<uint, DuelCardFacts?>? facts)
    {
        var view = AkiBoard() with
        {
            Choices = [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(1, "End turn", null, ActionKind.EndTurn),
                new(2, "Set Spell Shattering Arrow [1]", 3, ActionKind.SetSpellTrap, Zone: 0)]
        };
        var input = new DuelFieldInput();
        var frame = TimeSpan.FromSeconds(1 / 59.94);
        input.Handle(view, default, frame);
        (string Name, InputAction Press)[] steps = [("0-start", InputAction.None), ("1-left", InputAction.Left), ("2-left", InputAction.Left),
            ("3-menu", InputAction.Confirm), ("4-close", InputAction.Back), ("5-up", InputAction.Up), ("6-up", InputAction.Up),
            ("7-phase", InputAction.Back), ("8-phase-right", InputAction.Right), ("9-close", InputAction.Back), ("10-global", InputAction.Menu),
            ("11-chain", InputAction.None)];
        // The Chain prompt of the input capture: Scapegoat activated, a set Mystical Space Typhoon (4909) as the only response.
        var chain = view with
        {
            Revision = 99,
            Self = view.Self with { SpellTraps = [null, null, new CardView(6, 4909, "", "", null, null, null, false, FaceDown: true), null, null] },
            ChainTop = new CardView(50, 4818, "Scapegoat", "", null, null, null, false),
            Choices = [new(0, "Respond zone 3", null, ActionKind.ChainResponse, Zone: 2), new(1, "Pass response", null, ActionKind.ChainPass)]
        };
        using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 480, 272);
        foreach (var (name, press) in steps)
        {
            if (name == "11-chain") { input.Handle(chain, default, frame); view = chain; }
            else if (press != InputAction.None) input.Handle(view, new(press, press), frame);
            var list = DuelScene.Compose(new(view, 0, -1, "", 0, States[0].Style, facts, input), field.Profile);
            field.Render(list.FieldDraws);
            device.SetRenderTarget(target);
            device.Clear(Microsoft.Xna.Framework.Color.Black);
            renderer.Begin(); renderer.Layer(field.Layer); list.Replay(renderer, field); renderer.End();
            device.SetRenderTarget(null);
            using var output = new FileStream(Path.Combine(root, "walk-" + name + ".png"), FileMode.CreateNew, FileAccess.Write);
            target.SaveAsPng(output, 480, 272);
        }
    }

    public static DuelView EmptyView(DuelPhase phase, int turn) => new(0, PlayerId.One, PlayerId.One, turn, phase,
        Empty(PlayerId.One), Empty(PlayerId.Two), "", [], null);

    private static PlayerView Empty(PlayerId id) => new(id, 8000, 0, 0, [], new CardView?[5], new CardView?[5], new CardView?[2], [], []);
}
