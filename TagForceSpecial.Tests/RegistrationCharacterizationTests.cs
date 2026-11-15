using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using Xunit.Abstractions;

namespace TagForceSpecial.Tests;

/// <summary>Constructor validation precedence, diagnostic parameters and initial instance ordering.</summary>
public sealed class RegistrationCharacterizationTests(ITestOutputHelper output)
{
    private static DuelEngine Create(string mode)
    {
        var catalog = new CardCatalog(new(null, "synthetic-registration"),
            Enumerable.Range(1, 3).Select(id => new CardDefinition((uint)id, $"Synthetic {id}", "", new(1000, 1000, 4))));
        Dictionary<uint, DuelCardRules> rules = new()
        {
            [1] = new(CardCategory.Monster, AllowsNormalSummon: true),
            [2] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
            [3] = new(CardCategory.Monster, AllowsExtraDeck: true)
        };
        if (mode == "rules") rules[1] = null!;
        var duplicate = new RuleLockProfile("duplicate", 1, new(LockedAction.Attack));
        return new(catalog, rules, Enumerable.Repeat(1u, 40), Enumerable.Repeat(1u, 40),
            decisionSequences: mode == "decisionSequences" ? [null!] : null,
            experimentalSpecialSummonCards: mode == "special" ? [null!] : null,
            experimentalMaterialProcedures: mode == "material" ? [null!] : null,
            experimentalSummonLevelModifiers: mode == "summon" ? [null!] : null,
            effectDefinitions: mode == "effectDefinitions" ? [null!] : null,
            ruleLocks: mode == "ruleLocks" ? [null!] : mode == "duplicate" ? [duplicate, duplicate] : null,
            battleAbilities: mode == "battleAbilities" ? [null!] : null,
            destructionReplacements: mode == "destructionReplacements" ? [null!] : null,
            graveyardRedirects: mode == "graveyardRedirects" ? [null!] : null,
            damageRules: mode == "damageRules" ? [null!] : null,
            monsterStats: mode == "valid" ? null : [null!],
            playerOneExtraDeck: [3], playerTwoExtraDeck: [3], shuffleSeed: 37);
    }

    [Theory]
    [InlineData("rules", "rules")]
    [InlineData("decisionSequences", "decisionSequences")]
    [InlineData("special", "")]
    [InlineData("material", "")]
    [InlineData("summon", "")]
    [InlineData("effectDefinitions", "effectDefinitions")]
    [InlineData("ruleLocks", "ruleLocks")]
    [InlineData("duplicate", "ruleLocks")]
    [InlineData("battleAbilities", "battleAbilities")]
    [InlineData("destructionReplacements", "destructionReplacements")]
    [InlineData("graveyardRedirects", "graveyardRedirects")]
    [InlineData("damageRules", "graveyardRedirects")]
    [InlineData("monsterStats", "monsterStats")]
    public void EarlierInvalidRegistrationWinsBeforeTheLaterInvalidStatProfile(string mode, string expectedParameter)
    {
        var error = Assert.Throws<ArgumentException>(() => Create(mode));
        Assert.Equal(expectedParameter, error.ParamName ?? "");
        output.WriteLine("REGISTRATION_TRACE " + JsonSerializer.Serialize(new { mode, Type = error.GetType().Name, error.ParamName, error.Message }));
    }

    [Fact]
    public void InitialSetupKeepsMainThenExtraInstanceAllocationAndSeed()
    {
        var e = Create("valid");
        Assert.Equal(Enumerable.Range(1, 5), e.State.GetPlayer(PlayerId.One).Hand.Select(c => c.InstanceId));
        Assert.Equal(Enumerable.Range(41, 5), e.State.GetPlayer(PlayerId.Two).Hand.Select(c => c.InstanceId));
        Assert.Equal(81, Assert.Single(e.State.GetPlayer(PlayerId.One).ExtraDeck).InstanceId);
        Assert.Equal(82, Assert.Single(e.State.GetPlayer(PlayerId.Two).ExtraDeck).InstanceId);
        Assert.Equal(83, e.State.NextInstanceId);
        Assert.Equal(DuelRandomState.FromSeed(37), e.State.Random);
        output.WriteLine("REGISTRATION_TRACE " + JsonSerializer.Serialize(e.State));
    }
}
