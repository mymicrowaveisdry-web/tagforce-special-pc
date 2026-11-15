namespace TagForceSpecial.Core;

/// <summary>Read-only effect/source lookup used to construct action candidates; contains no commands or duel state.</summary>
internal sealed class ActionEffectCatalog(IReadOnlyDictionary<uint, EffectDefinition[]> effectsByCard, IReadOnlyDictionary<uint, DuelCardRules> rules)
{
    internal int EffectTotal(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects) ? effects.Length : 1;

    internal IEnumerable<int> ZoneEffectIndexes(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects)
        ? Enumerable.Range(0, effects.Length).Where(i => effects[i].Origin is EffectOrigin.Hand or EffectOrigin.Graveyard) : [];

    /// <summary>The Quick Effects of this card used while it is face-up.</summary>
    internal IEnumerable<int> FaceUpEffectIndexes(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects)
        ? Enumerable.Range(0, effects.Length).Where(i => effects[i].IsFaceUpEffect && effects[i].Origin == EffectOrigin.Field) : [];

    internal IEnumerable<int> PendulumEffectIndexes(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects)
        ? Enumerable.Range(0, effects.Length).Where(i => effects[i].Origin == EffectOrigin.PendulumZone && effects[i].IsFaceUpEffect) : [];

    internal bool IsToonCard(uint cardId) => rules.TryGetValue(cardId, out var rule) && rule.IsToon;
    internal int DetachCostFor(uint cardId, int effectIndex = 0) => effectsByCard.TryGetValue(cardId, out var effects) &&
        effectIndex >= 0 && effectIndex < effects.Length && effects[effectIndex] is { Cost: DetachCost detach } ? detach.Count : 0;

    internal IEnumerable<(CardInstance Card, int Index)> ZoneEffects(DuelPlayerState own) =>
        own.Hand.Concat(own.Graveyard).SelectMany(c => ZoneEffectIndexes(c.GameCardId).Select(i => (c, i)));

    internal IEnumerable<(int Zone, CardInstance Card, int Index)> FaceUpEffects(DuelPlayerState own) =>
        Enumerable.Range(0, DuelPlayerState.FieldZoneIndex + 1).Select(z => (Zone: z, Field: z == DuelPlayerState.FieldZoneIndex ? own.FieldSpell : own.SpellTrapFields[z]))
            .Where(e => e.Field is { IsFaceUp: true }).SelectMany(e => FaceUpEffectIndexes(e.Field!.Card.GameCardId).Select(i => (e.Zone, e.Field!.Card, i)));

    internal int EffectsOf(CardInstance? card) => card is null ? 1 : EffectTotal(card.GameCardId);

}
