using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

public sealed class ChainCatalogIdentityTests
{
    [Fact]
    public void DefinitionIdentitiesAreNotRestrictedToThePspPackedIdRange()
    {
        const uint custom = uint.MaxValue;
        var definition = new EffectDefinition("custom-effect", custom, EffectKind.QuickPlaySpell, [new DestroyTargetStep()],
            target: new SpellTrapOnFieldTarget());
        var response = ResponseProbe.Negate("custom-response", custom, ResponseNegation.Effect,
            ChainSpellSpeed.Two, [custom]);
        var tokens = new SummonTokensStep([custom, custom - 1, custom - 2, custom - 3], BattlePosition.FaceUpDefense);
        var summon = new SpecialSummonStep(SpecialSummonSources.Deck, [custom]);

        Assert.Equal(custom, definition.CardId);
        Assert.Equal(custom, response.CardId);
        Assert.Equal(custom, Assert.Single(Assert.IsType<DiscardCost>(response.Cost).Filter!.CardIds));
        Assert.Equal(custom, tokens.TokenCardIds[0]);
        Assert.Equal(custom, Assert.Single(summon.EligibleCardIds));
    }

    [Fact]
    public void EngineStillRejectsReferencesMissingFromItsCatalog()
    {
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-identity-boundary"),
            [new(1, "Synthetic Spell", "", new(0, 0, 0), new(0, 0, 0, 0, 0, 0, 0), new(1, 1, 0, 0))]);
        var rules = new Dictionary<uint, DuelCardRules> { [1] = new(CardCategory.Spell, AllowsSpellTrapSet: true) };
        var deck = Enumerable.Repeat(1u, 40).ToArray();
        var unknownSource = new EffectDefinition("unknown-source", uint.MaxValue, EffectKind.NormalSpell, [new DealDamageStep(1)]);
        var unknownCost = ResponseProbe.Negate("unknown-cost", 1, ResponseNegation.Effect,
            ChainSpellSpeed.Two, [uint.MaxValue]);
        var unknownTokens = new EffectDefinition("unknown-tokens", 1, EffectKind.QuickPlaySpell,
            [new SummonTokensStep([uint.MaxValue, uint.MaxValue - 1, uint.MaxValue - 2, uint.MaxValue - 3], BattlePosition.FaceUpDefense)]);

        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, effectDefinitions: [unknownSource]));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, effectDefinitions: [unknownCost]));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, effectDefinitions: [unknownTokens]));
    }
}
