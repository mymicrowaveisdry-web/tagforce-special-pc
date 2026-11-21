using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>Measured original prompt windows: colour runs, word wrap, menu and Chain window geometry, menu verbs.</summary>
public sealed class DuelPromptWindowTests
{
    /// <summary>Card-font widths measured at 0.6 for the captured labels; other text uses 6 per character.</summary>
    private static float Measure(string text, float scale) => text switch
    {
        "Show Details" => 76.95f, "Change Phase" => 82.95f, "Show Card List" => 88.8f, "Surrender" => 56.7f,
        _ => text.Length * 6f * scale / 0.6f
    };

    [Fact]
    public void ColourCodesSplitIntoRuns() =>
        Assert.Equal([(0, "Select "), (2, "phase"), (0, " to enter.")], DuelRichTextLayout.Runs("Select @2phase@0 to enter."));

    [Theory]
    [InlineData("Show Details", 106f)]
    [InlineData("Change Phase", 112f)]
    [InlineData("Show Card List", 118f)]
    [InlineData("Surrender", 86f)]
    public void MenuWidthIsWidestLabelPlusPadding(string label, float width)
    {
        var menu = new DuelFieldMenu(DuelPart.Prompt, [new MenuItem(label, new UnsupportedCommand(""))], 0, 409, 239);
        Assert.Equal(width, DuelMenuWindow.Rect(menu, [label], Measure).Width);
    }

    [Fact]
    public void MenuWindowsSitAboveTheAnchorAndStayRightOfThePanel()
    {
        var hand = new DuelFieldMenu(DuelPart.Prompt, [], 0, 409, 239);
        Assert.Equal((356f, 176f), (DuelMenuWindow.Rect(hand, ["Set", "Show Details"], Measure).X0, DuelMenuWindow.Rect(hand, ["Set", "Show Details"], Measure).Y0));
        var extra = new DuelFieldMenu(DuelPart.Prompt, [], 0, 158, 224);
        Assert.Equal(148f, DuelMenuWindow.Rect(extra, ["Show Card List"], Measure).X0);
        var deck = new DuelFieldMenu(DuelPart.Prompt, [], 0, 470, 222);
        Assert.Equal(394f, DuelMenuWindow.Rect(deck, ["Surrender"], Measure).X0);
    }

    [Fact]
    public void WrappedTextBreaksBeforeTheWordThatPassesTheWidth()
    {
        var text = new DuelRichText(DuelPart.Prompt, "s", 0, "X", 0, 0, 0.6f, 0.54f, new Dictionary<int, (int, int)> { [0] = (0, 64) }, 60, 11.72f);
        var lines = DuelRichTextLayout.Lines(text, "aaaa bbbb cccc", Measure);
        Assert.Equal(2, lines.Count);
        Assert.Equal(["aaaa ", "bbbb "], lines[0].Commands.Select(c => c.Text));
        Assert.Equal((0f, 11.72f), (lines[1].Commands[0].X, lines[1].Commands[0].Y));
    }

    [Fact]
    public void SetIconsDifferForMonstersAndSpellTraps()
    {
        var card = new CardView(1, 1, "", "", null, null, null, false);
        Assert.Equal(MenuVerb.SetMonster, DuelFieldCommands.Verb(new(0, "Set X [1]", 1, ActionKind.SetMonster), card));
        Assert.Equal(MenuVerb.SetSpellTrap, DuelFieldCommands.Verb(new(0, "Set X [1]", 1, ActionKind.SetSpellTrap), card));
        Assert.NotEqual(MenuVerbs.Icon(MenuVerb.SetMonster), MenuVerbs.Icon(MenuVerb.SetSpellTrap));
        Assert.Equal(MenuVerbs.TextOffset(MenuVerb.SetMonster), MenuVerbs.TextOffset(MenuVerb.SetSpellTrap));
    }

    [Fact]
    public void FaceDownMonsterTurningToAttackIsAFlipSummon()
    {
        var set = new CardView(1, 1, "", "", null, null, BattlePosition.FaceDownDefense, false, FaceDown: true);
        Assert.Equal(MenuVerb.FlipSummon, DuelFieldCommands.Verb(new(0, "Position 1", 1, ActionKind.Position, Position: BattlePosition.FaceUpAttack), set));
        var attack = set with { Position = BattlePosition.FaceUpAttack, FaceDown = false };
        Assert.Equal(MenuVerb.ToDefense, DuelFieldCommands.Verb(new(0, "Position 1", 1, ActionKind.Position, Position: BattlePosition.FaceUpDefense), attack));
    }
}
