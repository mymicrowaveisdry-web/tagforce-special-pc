using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>The attack target state against the original: rel_DuelDraw's trail routines and the measured attack-target GE frames
/// (particle translations, alpha bytes, cursor, sword and pointer placements).</summary>
public sealed class AttackTargetTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 59.94);

    private static int Measured(int particle, int phase) => 45 + 10 * (((particle + 2) / 3 + phase) % 22);

    [Fact]
    public void AlphaTableMatchesTheCapturedWave()
    {
        // Update 37 = second lap, counter 45: the reference frame's phase 6; each further update lowers the phase by one.
        foreach (var (updates, phase) in new[] { (37, 6), (38, 5), (41, 2), (43, 0), (44, 21) })
        {
            var alpha = AttackTrail.Alpha(updates);
            Assert.Equal(Enumerable.Range(0, 60).Select(i => Measured(i, phase)), alpha.Take(60).Select(a => (int)a));
            Assert.Equal((Measured(60, phase), Measured(63, phase)), (alpha[60], alpha[63]));
        }
    }

    [Fact]
    public void FirstLapGrowsFromZero()
    {
        var alpha = AttackTrail.Alpha(2);
        Assert.Equal([0, 245, 245, 245, 255, 255, 255, 0], alpha.Take(8).Select(a => (int)a));
        Assert.Equal(AttackTrail.Alpha(37), AttackTrail.Alpha(37 + 22 * 5));
    }

    [Theory]
    [InlineData(1, 37.788f, 117.555f)]
    [InlineData(29, 140.992f, 13.986f)]
    [InlineData(46, 103.887f, -75.549f)]
    [InlineData(59, 30.078f, -115.004f)]
    public void ParticlesFollowTheCapturedCurve(int index, float y, float z)
    {
        var p = AttackTrail.Point(new(0, 30, 117.730469f), new(0, 0, -117.730469f), index);
        // GE matrices carry 24-bit floats (16-bit mantissa): about 0.004 at a magnitude of 140.
        Assert.Equal(0, p.X, 0.005f);
        Assert.Equal(y, p.Y, 0.005f);
        Assert.Equal(z, p.Z, 0.005f);
    }

    [Fact]
    public void TheTrailUpdatesEveryTwoVblanks() =>
        Assert.Equal((0, 1, 1, 22), (AttackTrail.Updates(TimeSpan.FromSeconds(1.9 / 59.94)), AttackTrail.Updates(TimeSpan.FromSeconds(2.1 / 59.94)),
            AttackTrail.Updates(TimeSpan.FromSeconds(3.9 / 59.94)), AttackTrail.Updates(TimeSpan.FromSeconds(44.1 / 59.94))));

    private static CardView Monster(int instance, int atk, int def) => new(instance, 100, "", "", atk, def, BattlePosition.FaceUpAttack, false);

    private static DuelView Battle(params ChoiceView[] choices) => new(1, PlayerId.One, PlayerId.One, 2, DuelPhase.Battle,
        new(PlayerId.One, 8000, 36, 15, [], [null, null, Monster(5, 500, 500), null, null], new CardView?[5], new CardView?[2], [], []),
        new(PlayerId.Two, 8000, 37, 15, [], [null, null, Monster(7, 500, 1400), null, null], new CardView?[5], new CardView?[2], [], []),
        "", choices, null);

    private static DuelFieldInput Aiming(DuelView view)
    {
        var input = new DuelFieldInput();
        input.Handle(view, default, Frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame); // ○ on the attacker
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame); // Attack
        return input;
    }

    [Fact]
    public void OneMonsterTargetStillOpensTheTargetState()
    {
        var view = Battle(new ChoiceView(0, "Attack", 5, ActionKind.Attack, TargetZone: 2), new(1, "End turn", null, ActionKind.EndTurn));
        var input = Aiming(view);
        Assert.IsType<TargetModal>(input.Modal);
        var fallback = DuelScene.Compose(new(view, 0, -1, "", 0, Input: input));
        Assert.Contains(fallback.Draws.OfType<DuelText>(), draw => draw.Text == "Choose target");
        Assert.Equal(new DuelSubmit(1, 0), input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame));
    }

    [Fact]
    public void ALoneDirectAttackSubmitsAtOnce()
    {
        var view = Battle(new ChoiceView(0, "Direct attack", 5, ActionKind.Attack), new(1, "End turn", null, ActionKind.EndTurn));
        var input = new DuelFieldInput();
        input.Handle(view, default, Frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame);
        Assert.Equal(new DuelSubmit(1, 0), input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame));
    }

    [Fact]
    public void TheInspectorFollowsTheTargetAndWithholdsFaceDownDetails()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        var view = Battle(new ChoiceView(0, "Attack", 5, ActionKind.Attack, TargetZone: 2),
            new(1, "Attack", 5, ActionKind.Attack, TargetZone: 3), new(2, "End turn", null, ActionKind.EndTurn));
        var target = view.Opponent.Monsters[2]! with { CardId = 101, Name = "Visible target", Description = "Public effect" };
        var concealed = target with { InstanceId = 8, CardId = 102, Name = "Concealed name", Description = "Concealed effect", Defense = 9999,
            Position = BattlePosition.FaceDownDefense, FaceDown = true };
        view = view with { Opponent = view.Opponent with { Monsters = [null, null, target, concealed, null] } };
        var input = Aiming(view);
        IReadOnlyList<DuelDraw> Draws() => DuelScene.Compose(new(view, 0, -1, "", 0, Input: input), field).Draws;

        // The original two-card panel (aki-attack 210–289): target above, attacker below; no standard panel meanwhile.
        var visibleDraws = Draws();
        var both = Assert.Single(visibleDraws.OfType<DuelAttackPanel>());
        Assert.Equal((101u, 100u), (both.TargetId!.Value, both.AttackerId!.Value));
        Assert.Empty(visibleDraws.OfType<DuelCardPanel>());

        // A face-down target contributes no identity (back tint, no text).
        input.Handle(view, new(InputAction.Right, InputAction.Right), Frame);
        var concealed2 = Assert.Single(Draws().OfType<DuelAttackPanel>());
        Assert.Equal(((uint?)null, CardInspector.BackTint, 100u), (concealed2.TargetId, concealed2.TargetTint, concealed2.AttackerId!.Value));

        input.Handle(view, new(InputAction.Back, InputAction.Back), Frame);
        var closedDraws = Draws();
        Assert.Empty(closedDraws.OfType<DuelAttackPanel>());
        Assert.Single(closedDraws.OfType<DuelCardPanel>());
    }

    [Fact]
    public void TheSceneReplaysTheCapturedPlacements()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        var view = Battle(new ChoiceView(0, "Attack", 5, ActionKind.Attack, TargetZone: 2), new(1, "End turn", null, ActionKind.EndTurn));
        var input = Aiming(view);
        var draws = DuelScene.Compose(new(view, 0, -1, "", 0, null, null, input), field).Draws.OfType<DuelFieldDraw>().ToList();
        var cursor = draws.Single(d => d.Piece == "attack.cursor").Placement.World!.Value;
        Assert.Equal((-60f, 60f, -60f), (MathF.Round(cursor.M11, 3), MathF.Round(cursor.M22, 3), MathF.Round(cursor.M33, 3)));
        Assert.Equal(new Vector3(0, 1, -117.730469f), cursor.Translation);
        Assert.All(draws.Where(d => d.Piece == "attack.sword"), d => Assert.Equal(0, Vector3.Distance(new(0.32f, 30f, 98.51f), d.Placement.World!.Value.Translation), 2));
        Assert.Equal(60, draws.Count(d => d.Piece == "attack.particle"));
        Assert.Single(draws, d => d.Piece == "attack.head" && d.Placement.World!.Value.Translation == new Vector3(0, 17.5f, -117.730469f));
        Assert.NotEqual(Vector2.Zero, draws.Single(d => d.Piece == "focus.pointer").Placement.UvOffset);
        // Battle Phase readouts 500 / 500 and 500 / 1400 (13 digits) on top of the pile counters 36, 15, 37, 15; no Level without card facts.
        Assert.Equal(13 + 8, draws.Count(d => d.Piece == "digit.small"));
    }

    [Fact]
    public void ReadoutsAreBattlePhaseOnly()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        int Digits(DuelView view) => DuelScene.Compose(new(view, 0, -1, "", 0), field).Draws.OfType<DuelFieldDraw>().Count(d => d.Piece == "digit.small");
        Assert.Equal(13, Digits(Battle()) - Digits(Battle() with { Phase = DuelPhase.Main1 }));
    }
}
