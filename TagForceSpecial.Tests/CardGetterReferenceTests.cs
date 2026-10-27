using System.Text.Json;
using TagForceSpecial.Assets;

namespace TagForceSpecial.Tests;

public sealed class CardGetterReferenceTests
{
    // These values come from runtime.v0, not the capture script's calculated
    // "expected" fields. The packed words come from a separate asset listing.
    // Hosted tests compare the public projection; they do not replay PPSSPP.
    [Theory]
    [InlineData(4007u)]
    [InlineData(4041u)]
    [InlineData(4842u)]
    [InlineData(11213u)]
    [Trait("Category", "LocalReference")]
    public void PackedPropertyAttackMatchesRecordedOriginalGetter(uint cardId)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "card-attack.json")));
        var sample = fixture.RootElement.GetProperty("samples").EnumerateArray()
            .Single(row => row.GetProperty("cardId").GetUInt32() == cardId);
        uint word = sample.GetProperty("rawPropertyWord0").GetUInt32();
        int observed = sample.GetProperty("runtimeV0").GetInt32();

        Assert.Equal(observed, CardAttackGetter.FromPropertyWord(word));
        Assert.Equal(observed, CardPropertyDecoder.Decode(cardId, word, 0).Attack);
    }

    // Synthetic tests derived from the MIPS extraction, not measured PSP cases.
    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, 10)]
    [InlineData(510u, 5100)]
    [InlineData(511u, 0)]
    public void SyntheticEncodedBoundariesIgnoreAdjacentFields(uint encoded, int expected)
    {
        uint clean = encoded << 14;
        uint adjacentBitsSet = clean | 0xFF803FFFu;
        Assert.Equal(expected, CardAttackGetter.FromPropertyWord(clean));
        Assert.Equal(expected, CardAttackGetter.FromPropertyWord(adjacentBitsSet));
    }

    [Fact]
    public void SyntheticVariableAttackPreservesSeparateDomainRepresentation()
    {
        uint word = 511u << 14;
        Assert.Equal(0, CardAttackGetter.FromPropertyWord(word));
        Assert.Null(CardPropertyDecoder.Decode(4007, word, 0).Attack);
    }
}
