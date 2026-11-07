using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Normal Monster family tests with authored statistics.</summary>
public sealed class NormalMonsterFamilyTests
{
    // Authored levels for selected family IDs; every other family member is an authored Level 4.
    private const uint Donor = 4012, Filler = 4030, L5 = 3804, L6 = 3805, L7 = 3850, L8 = 4028;
    private static readonly Dictionary<uint, uint> Levels = new() { [Filler] = 1, [L5] = 5, [L6] = 6, [L7] = 7, [L8] = 8 };

    internal static CardCatalog FamilyCatalog(Func<CardDefinition, CardDefinition>? edit = null, bool includeTokens = false)
    {
        edit ??= c => c;
        var monsters = TfspNormalMonsters.Ids.Select(id => new CardDefinition(id, $"Family probe {id}",
            "Synthetic test description", new(1000 + (int)(id % 7) * 100, 1000, Levels.GetValueOrDefault(id, 4u)),
            new(0, 0, 0, 0, 0, 0, 0), Kind: CardKind.Monster, Effects: CardEffectBinding.None));
        var spells = PlayablePreview.Cards.Where(c => c.Spell is not null).Select(c => new CardDefinition(c.CardId,
            $"Spell probe {c.CardId}", "", new(0, 0, 0),
            Kind: TfspCardMetadata.Reviewed[c.CardId].Kind, Effects: TfspCardMetadata.Reviewed[c.CardId].Effects));
        var tokens = includeTokens
            ? TfspTokens.Ids.Select(id => new CardDefinition(id, $"Token probe {id}", "", new(0, 0, 1)))
            : [];
        // Contiguous 1..N source rows, as the inventory requires of an imported catalog.
        return new(new(null, "authored-normal-monster-family"), monsters.Concat(spells).Select(edit).Concat(tokens)
            .Select((c, i) => c with { SourceRow = new(i + 1, c.GameCardId, 0, 0) }));
    }

    private static DuelEngine Engine(IEnumerable<uint> first, CardCatalog? catalog = null, bool withSpells = false) =>
        new(catalog ?? FamilyCatalog(), withSpells ? NormalMonsterFamily.RulesWith(PlayablePreview.Rules) : NormalMonsterFamily.Rules,
            first, Enumerable.Repeat(Filler, 40),
            effectDefinitions: withSpells ? PlayablePreview.Definitions : null);

    private static MonsterBinding Bound(DuelEngine e, int zone)
    {
        var m = e.State.GetPlayer(PlayerId.One).MonsterZones[zone]!;
        return new(PlayerId.One, zone, m.Card, m.PlacementId);
    }

    private static CardInstance InHand(DuelEngine e, uint id) => e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);

    /// <summary>
    /// Summons one donor per own turn into zones 0..count-1, then returns on the next own Main Phase 1.
    /// </summary>
    private static DuelEngine WithDonors(uint high, int count, CardCatalog? catalog = null, bool withSpells = false, IEnumerable<uint>? tail = null)
    {
        uint[] deck = [.. Enumerable.Repeat(Donor, count), high, high, .. tail ?? [], .. Enumerable.Repeat(Filler, 40)];
        var e = Engine(deck.Take(40), catalog, withSpells);
        EnterMain(e);
        for (int zone = 0; zone < count; zone++)
        {
            if (zone > 0) { Next(e); Next(e); }
            Ok(e, new NormalSummon(PlayerId.One, InHand(e, Donor).InstanceId, zone));
        }
        Next(e); Next(e);
        return e;
    }

    [Fact]
    public void RegistryIsSortedDistinctAndDisjointFromTokensAndPreviewSpells()
    {
        var ids = NormalMonsterFamily.CardIds;
        Assert.Equal(665, ids.Count);
        Assert.Equal(ids.Order(), ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Empty(ids.Intersect(TfspTokens.Ids));
        Assert.Equal(95, TfspTokens.Ids.Distinct().Count());
        Assert.Empty(ids.Intersect(PlayablePreview.Cards.Where(c => c.Spell is not null).Select(c => c.CardId)));
        Assert.Subset(ids.ToHashSet(), PlayablePreview.Cards.Where(c => c.Spell is null).Select(c => c.CardId).ToHashSet());
        Assert.Subset(ids.ToHashSet(), TfspNormalMonsters.ArchfiendMembers.ToHashSet());
        Assert.Equal(665 + 6, ReviewedCardMetadata.Reviewed.Count);
    }

    [Fact]
    public void FrozenConverterBaselineIsThePreviewSubsetOfTheReviewedOverlay()
    {
        // TfspCardMetadata (Cards) is the frozen converter baseline; ReviewedCardMetadata (Effects) is the overlay.
        var baseline = TfspCardMetadata.Reviewed;
        Assert.Equal(PlayablePreview.Cards.Select(c => c.CardId).Order(), baseline.Keys.Order());
        Assert.All(baseline, e => Assert.Same(e.Value, ReviewedCardMetadata.Reviewed[e.Key]));
        var reviewed = new CardCatalog(new(TfspCardMetadata.CardInfoSha256, "overlay-probe"),
            [new(L7, "Probe", "", new(1000, 1000, 7))]);
        Assert.Equal(CardEffectState.Unknown, TfspCardMetadata.Apply(reviewed)[L7].Effects!.State);
        Assert.Equal(CardEffectState.None, ReviewedCardMetadata.ApplyReviewed(reviewed)[L7].Effects!.State);
        Assert.Equal(CardEffectState.None, ReviewedCardMetadata.Overlay(reviewed)[L7].Effects!.State);
        var other = new CardCatalog(new(new string('a', 64), "other-build"), [new(L7, "Probe", "", new(1000, 1000, 7))]);
        Assert.Same(other, ReviewedCardMetadata.Overlay(other));
        Assert.Throws<InvalidDataException>(() => TfspCardMetadata.Apply(other));
        Assert.Throws<InvalidDataException>(() => ReviewedCardMetadata.ApplyReviewed(other));
    }

    public static IEnumerable<object[]> FamilyIds() => TfspNormalMonsters.Ids.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(FamilyIds))]
    public void EachFamilyCardHasReviewedEffectFreeMetadataAndFamilyRules(uint id)
    {
        var metadata = ReviewedCardMetadata.Reviewed[id];
        Assert.Equal(CardKind.Monster, metadata.Kind);
        Assert.Equal(CardEffectState.None, metadata.Effects.State);
        Assert.Empty(metadata.Effects.References);
        Assert.Same(NormalMonsterFamily.Rule, NormalMonsterFamily.Rules[id]);
        Assert.Equal(TfspNormalMonsters.ArchfiendMembers.Contains(id), NormalMonsterFamily.Exception(id) is not null);
    }

    [Fact]
    public void CatalogValidationFailsClosedOutsideTheReviewedFamilyContract()
    {
        NormalMonsterFamily.ValidateCatalog(FamilyCatalog());
        Assert.Throws<InvalidDataException>(() => NormalMonsterFamily.ValidateCatalog(new(new(new string('f', 64), "unreviewed"), FamilyCatalog().Cards)));
        Assert.Throws<InvalidDataException>(() => NormalMonsterFamily.ValidateCatalog(new(FamilyCatalog().Source, FamilyCatalog().Cards.Where(c => c.GameCardId != L7))));
        CardDefinition Edit(CardDefinition c, Func<CardDefinition, CardDefinition> change) => c.GameCardId == L6 ? change(c) : c;
        Func<CardDefinition, CardDefinition>[] invalid =
        [
            c => c with { Effects = CardEffectBinding.Unknown },
            c => c with { Kind = CardKind.Unknown },
            c => c with { Codes = new(0, 0, 1, 1, 0, 0, 0) },
            c => c with { Statistics = new(null, 1000, 6) },
            c => c with { Statistics = new(1000, null, 6) },
            c => c with { Statistics = new(1000, 1000, 0) },
            c => c with { Statistics = new(1000, 1000, 9) }
        ];
        foreach (var change in invalid)
            Assert.Throws<InvalidDataException>(() => NormalMonsterFamily.ValidateCatalog(FamilyCatalog(c => Edit(c, change))));
    }

    [Fact]
    public void LowLevelSummonsDirectlyAndHighLevelRequiresTheTributeCommand()
    {
        var e = Engine([Donor, L5, L7, .. Enumerable.Repeat(Filler, 37)]);
        EnterMain(e);
        Reject(e, new NormalSummon(PlayerId.One, InHand(e, L5).InstanceId, 0), DuelError.TributeSummonNotSupported);
        Reject(e, new SetMonster(PlayerId.One, InHand(e, L7).InstanceId, 0), DuelError.TributeSummonNotSupported);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, L5).InstanceId, 0, []), DuelError.InvalidTributeSelection);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, Donor).InstanceId, 0, []), DuelError.UnsupportedMonsterLevel);
        Ok(e, new NormalSummon(PlayerId.One, InHand(e, Donor).InstanceId, 0));
    }

    [Theory]
    [InlineData(L5, 1, false)] [InlineData(L5, 1, true)]
    [InlineData(L6, 1, false)] [InlineData(L6, 1, true)]
    [InlineData(L7, 2, false)] [InlineData(L7, 2, true)]
    [InlineData(L8, 2, false)] [InlineData(L8, 2, true)]
    public void HighLevelFamilyMonstersTributeSummonOrSetWithFamilyDonors(uint high, int count, bool set)
    {
        var e = WithDonors(high, count);
        var donors = Enumerable.Range(0, count).Select(z => Bound(e, z)).ToArray();
        if (count == 2)
            Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, high).InstanceId, 0, donors.Take(1), set), DuelError.InvalidTributeSelection);
        else
            Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, high).InstanceId, 0, [], set), DuelError.InvalidTributeSelection);
        var card = InHand(e, high);
        var result = Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, card.InstanceId, 0, donors, set));
        var player = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(donors.Select(d => d.Card), result.Events.OfType<MonsterTributed>().Select(t => t.Monster.Card));
        Assert.Equal(donors.Select(d => d.Card), player.Graveyard);
        Assert.Equal(card, player.MonsterZones[0]!.Card);
        Assert.Equal(set ? BattlePosition.FaceDownDefense : BattlePosition.FaceUpAttack, player.MonsterZones[0]!.Position);
        Assert.True(e.State.NormalSummonUsed);
    }

    [Fact]
    public void TributeSummonedMonsterAttacksForItsPrintedAttackAndCanItselfBeTributed()
    {
        var catalog = FamilyCatalog();
        var e = WithDonors(L5, 1, catalog);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, L5).InstanceId, 0, [Bound(e, 0)]));
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        var attack = Ok(e, new DeclareAttack(PlayerId.One, 0));
        int atk = catalog[L5].Statistics.Attack!.Value;
        Assert.Equal(atk, Assert.Single(attack.Events.OfType<BattleDamageDealt>()).Damage);
        Assert.Equal(8000 - atk, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Next(e); Next(e);
        var l5 = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card;
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, L5).InstanceId, 1, [Bound(e, 0)]));
        Assert.Contains(l5, e.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void FamilyMonstersAndPreviewBurnSpellsInteractInOneEngine()
    {
        // Ookazi (4352) resolves with a Tribute-Summoned family monster on the field; neither contract disturbs the other.
        var e = WithDonors(L6, 1, withSpells: true, tail: [4352]);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, InHand(e, L6).InstanceId, 0, [Bound(e, 0)]));
        Ok(e, new ActivateSpell(PlayerId.One, InHand(e, 4352).InstanceId, 0));
        var events = Resolve(e);
        Assert.Equal(800, Assert.Single(events.OfType<EffectDamageDealt>()).Damage);
        Assert.Equal(L6, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal(7200, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void RuleMergeReplacesNarrowerMonsterRulesAndRejectsCategoryConflicts()
    {
        var merged = NormalMonsterFamily.RulesWith(PlayablePreview.Rules);
        Assert.Same(NormalMonsterFamily.Rule, merged[Donor]);
        Assert.Equal(PlayablePreview.Rules[4352], merged[4352]);
        Assert.Equal(665 + 6, merged.Count);
        Assert.Throws<ArgumentException>(() => NormalMonsterFamily.RulesWith(
            new Dictionary<uint, DuelCardRules> { [Donor] = new(CardCategory.Spell, AllowsSpellTrapSet: true) }));
    }

    [Fact]
    public void ManifestMarksWholeFamilySupportedAndExcludesTokensAsEntries()
    {
        var catalog = FamilyCatalog(includeTokens: true);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(CardSupportInventory.Build(catalog, catalog.Count + 1),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var full = json.RootElement.GetProperty("pspFidelity10");
        var cards = full.GetProperty("cards").EnumerateArray().ToArray();
        Assert.Equal(665 + 6, cards.Length);
        Assert.Equal(95, full.GetProperty("excludedTokens").GetArrayLength());
        Assert.DoesNotContain(cards, c => TfspTokens.Ids.Contains(c.GetProperty("gameCardId").GetUInt32()));
        var family = cards.Where(c => c.GetProperty("behaviorFamily").GetString() == NormalMonsterFamily.FamilyId).ToArray();
        Assert.Equal(665, family.Length);
        Assert.All(family, c =>
        {
            Assert.Equal("supported", c.GetProperty("status").GetString());
            Assert.Equal(NormalMonsterFamily.BatchId, c.GetProperty("scope").GetString());
            Assert.False(c.GetProperty("pspVerified").GetBoolean());
        });
        Assert.Contains("Archfiend", family.Single(c => c.GetProperty("gameCardId").GetUInt32() == 4789).GetProperty("exceptions").GetString());
        Assert.Contains("two eligible Tributes", family.Single(c => c.GetProperty("gameCardId").GetUInt32() == L8).GetProperty("conditions").GetString());
        // The preview manifest keeps its narrower 14-card contract.
        Assert.Equal(14, json.RootElement.GetProperty("playablePreview").GetProperty("cards").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("unclassifiedCardCount").GetInt32());
    }
}
