namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    /// <summary>Effective level solely for the supported hand Normal Summon/Set procedures.</summary>
    public uint? GetHandSummonLevel(PlayerId player, int instanceId)
    {
        var card = State.GetPlayer(player).Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        return card is not null && rules.TryGetValue(card.GameCardId, out var rule) && rule.Category == CardCategory.Monster
            ? ComputeHandSummonLevel(State, player, card) : null;
    }

    private uint ComputeHandSummonLevel(DuelState state, PlayerId player, CardInstance card)
    {
        uint raw = catalog[card.GameCardId].Statistics.Level;
        if (raw == 0) return 0; // A level-less card is not made eligible by a modifier.
        long level = raw;
        foreach (var (binding, monster) in FieldMonsters(state))
            if (monster.Position != BattlePosition.FaceDownDefense && !monster.BattleDestroyed &&
                !state.EventEffects.NegatedMonsters.Contains(binding))
                foreach (var profile in summonLevelModifiers)
                    if (profile.SourceCardId == monster.Card.GameCardId && profile.TargetCardIds.Contains(card.GameCardId) &&
                        (profile.Relation == EffectRelation.Any ||
                         profile.Relation == EffectRelation.Controller && binding.Player == player ||
                         profile.Relation == EffectRelation.Opponent && binding.Player != player))
                        level += profile.LevelDelta;
        return (uint)Math.Clamp(level, 1L, uint.MaxValue);
    }
}
