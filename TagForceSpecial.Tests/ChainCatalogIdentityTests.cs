using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

public sealed class ChainCatalogIdentityTests
{
    [Fact]
    public void ProfileIdentitiesAreNotRestrictedToThePspPackedIdRange()
    {
        const uint custom = uint.MaxValue;
        var effect = new ChainEffectProfile("custom-effect", custom, ChainOperation.DestroyBoundSpellTrap);
        var response = new ChainResponseProfile("custom-response", custom, ChainOperation.NegateEffect,
            ChainSpellSpeed.Two, [custom]);
        var tokens = new ChainEffectProfile("custom-tokens", custom, ChainOperation.SummonFourTokens,
            [custom, custom - 1, custom - 2, custom - 3]);
        var activation = new ActivationContract("custom-activation", custom, [custom]);
        var summon = new DeckSpecialSummonSpell("custom-summon", custom, [custom]);

        Assert.Equal(custom, effect.CardId);
        Assert.Equal(custom, response.CardId);
        Assert.Equal(custom, Assert.Single(response.DiscardCardIds));
        Assert.Equal(custom, tokens.TokenCardIds[0]);
        Assert.Equal(custom, activation.SpellCardId);
        Assert.Equal(custom, Assert.Single(activation.DiscardCardIds));
        Assert.Equal(custom, summon.SpellCardId);
        Assert.Equal(custom, Assert.Single(summon.EligibleMonsterIds));
    }

    [Fact]
    public void EngineStillRejectsProfileReferencesMissingFromItsCatalog()
    {
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-identity-boundary"),
            [new(1, "Synthetic Spell", "", new(0, 0, 0), new(0, 0, 0, 0, 0, 0, 0), new(1, 1, 0, 0))]);
        var rules = new Dictionary<uint, DuelCardRules> { [1] = new(CardCategory.Spell, AllowsSpellTrapSet: true) };
        var deck = Enumerable.Repeat(1u, 40).ToArray();
        var unknownSource = new ChainEffectProfile("unknown-source", uint.MaxValue, ChainOperation.DestroyBoundSpellTrap);
        var unknownCost = new ChainResponseProfile("unknown-cost", 1, ChainOperation.NegateEffect,
            ChainSpellSpeed.Two, [uint.MaxValue]);
        var unknownTokens = new ChainEffectProfile("unknown-tokens", 1, ChainOperation.SummonFourTokens,
            [uint.MaxValue, uint.MaxValue - 1, uint.MaxValue - 2, uint.MaxValue - 3]);

        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck,
            experimentalChainEffects: [unknownSource]));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck,
            experimentalChainResponses: [unknownCost]));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck,
            experimentalChainEffects: [unknownTokens]));
    }
}
