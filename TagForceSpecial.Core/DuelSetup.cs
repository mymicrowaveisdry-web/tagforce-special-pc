using System.Collections.Frozen;

namespace TagForceSpecial.Core;

/// <summary>Validates card classifications and constructs immutable initial players with bounded, stable instance allocation.</summary>
internal static class DuelSetup
{
    /// <summary>Standard starting Life Points; a duel may start with another positive amount (Free Duel's rule change).</summary>
    internal const int DefaultLifePoints = 8000, MaximumStartingLifePoints = 99_999;

    internal static FrozenDictionary<uint, DuelCardRules> Rules(IReadOnlyDictionary<uint, DuelCardRules> rules, IReadOnlySet<uint> knownIds)
    {
        foreach (var (id, profile) in rules)
            if (!knownIds.Contains(id) || profile is null || !Enum.IsDefined(profile.Category) ||
                (profile.AllowsNormalSummon || profile.AllowsBasicBattle) && profile.Category != CardCategory.Monster ||
                (profile.AllowsTributeSummon || profile.AllowsTributeForSummon || profile.IsToon) && profile.Category != CardCategory.Monster ||
                profile.IsToonWorld && profile.Category != CardCategory.Spell ||
                (profile.AllowsExtraDeck || profile.AllowsSummonMaterial || profile.AllowsPendulumSummon) && profile.Category != CardCategory.Monster ||
                profile.AllowsTributeSummon && !profile.AllowsNormalSummon ||
                profile.RequiresRegisteredTributeProcedure && !profile.AllowsTributeSummon ||
                profile.AllowsSpellTrapSet && profile.Category is not (CardCategory.Spell or CardCategory.Trap) ||
                (profile.CannotBeSpecialSummoned || profile.IsGemini) && profile.Category != CardCategory.Monster ||
                profile.IsToken && (profile.Category != CardCategory.Monster || profile.AllowsNormalSummon ||
                    profile.AllowsTributeSummon || profile.AllowsExtraDeck || profile.AllowsPendulumSummon))
                throw new ArgumentException("Invalid explicit card rules.", nameof(rules));
        return rules.ToFrozenDictionary();
    }

    internal static DuelState InitialState(IReadOnlySet<uint> knownIds, IReadOnlyDictionary<uint, DuelCardRules> rules,
        IEnumerable<uint> playerOneDeck, IEnumerable<uint> playerTwoDeck, PlayerId startingPlayer,
        IEnumerable<uint>? playerOneExtraDeck, IEnumerable<uint>? playerTwoExtraDeck, ulong? shuffleSeed, int startingLifePoints = DefaultLifePoints)
    {
        if (startingLifePoints is < 1 or > MaximumStartingLifePoints)
            throw new ArgumentOutOfRangeException(nameof(startingLifePoints), "Starting Life Points must be between 1 and 99,999.");
        bool IsTokenId(uint id) => rules.TryGetValue(id, out var rule) && rule.IsToken;
        int nextInstance = 1;
        DuelPlayerState CreatePlayer(PlayerId id, IEnumerable<uint> ids)
        {
            // Limit enumeration too: malformed/infinite sequences cannot allocate indefinitely.
            var deck = ids.Take(61).ToArray();
            if (deck.Length is < 40 or > 60 || deck.Any(cardId => !knownIds.Contains(cardId)))
                throw new ArgumentException("Each main deck needs 40–60 catalog IDs.");
            if (deck.Any(IsTokenId)) throw new ArgumentException("Tokens are created by effects and cannot be in a Deck.");
            var instances = deck.Select(cardId => new CardInstance(nextInstance++, cardId, id)).ToArray();
            return new(id, instances.Skip(5), instances.Take(5));
        }
        var first = CreatePlayer(PlayerId.One, playerOneDeck);
        var second = CreatePlayer(PlayerId.Two, playerTwoDeck);
        CardInstance[] CreateExtraDeck(PlayerId owner, IEnumerable<uint>? cards)
        {
            var ids = (cards ?? []).Take(16).ToArray();
            if (ids.Length > 15 || ids.Any(id => !rules.TryGetValue(id, out var rule) || !rule.AllowsExtraDeck))
                throw new ArgumentException("Each Extra Deck supports up to 15 explicitly eligible catalog monsters.");
            return ids.Select(id => new CardInstance(nextInstance++, id, owner)).ToArray();
        }
        var firstExtra = CreateExtraDeck(PlayerId.One, playerOneExtraDeck);
        var secondExtra = CreateExtraDeck(PlayerId.Two, playerTwoExtraDeck);
        return new([first.With(extraDeck: firstExtra, lifePoints: startingLifePoints), second.With(extraDeck: secondExtra, lifePoints: startingLifePoints)], startingPlayer,
            bookkeeping: new(nextInstance, shuffleSeed is { } seed ? DuelRandomState.FromSeed(seed) : null));
    }
    internal static FrozenDictionary<string, DecisionSequence> Decisions(IEnumerable<DecisionSequence>? decisionSequences, IReadOnlySet<uint> knownIds)
    {
        var sequences = (decisionSequences ?? []).ToArray();
        if (sequences.Any(s => s is null || s.Steps.Any(step => step.AllowedCardIds.Any(id => !knownIds.Contains(id)))))
            throw new ArgumentException("Decision sequences must reference catalog IDs.", nameof(decisionSequences));
        return sequences.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);
    }

}
