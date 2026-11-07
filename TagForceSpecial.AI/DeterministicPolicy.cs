using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.AI;

public sealed record PolicyChoice(long Revision, int Token, string Key, ActionKind Kind, int Score);

/// <summary>One-ply policy over authorized information only. No engine, catalog or hidden-state access.</summary>
public sealed class DeterministicPolicy(uint seed)
{
    public const string Version = "initial-opponent-v1";
    public const int MaximumActions = DuelActionSource.MaximumCandidates;
    public uint Seed { get; } = seed;

    public PolicyChoice? Choose(DuelObservation view)
    {
        if (view.Availability != ActionAvailability.Ready || view.Actions.Count is 0 or > MaximumActions) return null;
        LegalAction? best = null;
        int bestScore = int.MinValue;
        ulong bestTie = 0;
        foreach (var action in view.Actions)
        {
            int score = Score(view, action);
            if (score == int.MinValue) continue;
            ulong tie = Tie(view, action);
            if (best is null || score > bestScore || score == bestScore && tie > bestTie)
            {
                best = action; bestScore = score; bestTie = tie;
            }
        }
        return best is null ? null : new(view.Revision, best.Token, best.Key, best.Kind, bestScore);
    }

    private static int Score(DuelObservation v, LegalAction a)
    {
        var card = v.Self.Hand.Concat(v.Self.Monsters.OfType<CardView>())
            .Concat(v.Decision?.Cards ?? []).FirstOrDefault(c => c.InstanceId == a.CardInstanceId);
        int attack = Math.Clamp(card?.Attack ?? 0, 0, 10000);
        return a.Kind switch
        {
            ActionKind.Draw => 1000,
            ActionKind.ChooseCard => 600 + attack / 100,
            ActionKind.ChoosePosition => a.Position == BattlePosition.FaceUpAttack ? 650 : 600,
            ActionKind.ChooseZone or ActionKind.ChooseTarget => 600,
            ActionKind.ChainResponse or ActionKind.BattleResponse => 800,
            ActionKind.ChainPass or ActionKind.BattlePass or ActionKind.ResolveChain => 700,
            ActionKind.ReplayAttack => 150,
            ActionKind.CancelReplay => 0,
            ActionKind.AdvanceBattle => 30,
            ActionKind.ActivateSpell or ActionKind.ActivateSetSpell => 300,
            ActionKind.Summon => 200 + attack / 100,
            ActionKind.SetMonster => 40,
            ActionKind.SetSpellTrap => 5,
            ActionKind.Position => a.Position == BattlePosition.FaceUpAttack ? 100 : -20,
            ActionKind.Attack => AttackScore(v, a),
            ActionKind.Discard => 500 - attack / 100,
            ActionKind.Phase => a.Phase == DuelPhase.Battle ? 90 : a.Phase == DuelPhase.End ? 10 : 20,
            ActionKind.EndTurn => 20,
            _ => int.MinValue
        };
    }

    private static int AttackScore(DuelObservation v, LegalAction a)
    {
        if (a.Zone is not { } zone || v.Self.Monsters[zone] is not { Attack: { } attack }) return -10;
        if (a.TargetZone is not { } target) return 180;
        var defender = v.Opponent.Monsters[target];
        // Face-down cards use a fixed unknown-card priority.
        if (defender is null || defender.Hidden) return 110;
        int? strength = defender.Position == BattlePosition.FaceUpAttack ? defender.Attack : defender.Defense;
        if (strength is null) return 110;
        if (attack > strength) return 140 + Math.Min(20, (attack - strength.Value) / 100);
        if (attack == strength && defender.Position == BattlePosition.FaceUpAttack && attack > 0) return 120;
        return -10; // Prefer progression to a visibly losing or ineffective battle.
    }

    // Versioned FNV-1a + avalanche. Stateless; independent of process hash randomization and call count.
    private ulong Tie(DuelObservation v, LegalAction a)
    {
        ulong value = 14695981039346656037UL;
        foreach (char c in $"{Version}|{Seed}|{v.Revision}|{v.Player}|{a.Key}")
            value = unchecked((value ^ c) * 1099511628211UL);
        value ^= value >> 33; value = unchecked(value * 0xff51afd7ed558ccdUL);
        value ^= value >> 33; value = unchecked(value * 0xc4ceb9fe1a85ec53UL);
        return value ^ value >> 33;
    }
}
