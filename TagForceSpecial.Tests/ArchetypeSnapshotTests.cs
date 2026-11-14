using TagForceSpecial.Cards;
using TagForceSpecial.Effects.Archetypes;

namespace TagForceSpecial.Tests;

public sealed class ArchetypeSnapshotTests
{
    private static readonly ArchetypeId Existing = new("official:existing"), New = new("synthetic:new"), Other = new("official:other");
    private static readonly SourceCardId Original = new("tfs:1"), Additional = new("synthetic:4294967295");
    private static readonly ArchetypePackId BaseId = new("synthetic:base");
    private static ArchetypeEvidence Evidence => new("synthetic content fixture", new string('a', 64), "Explicit authoring data, not a native gameplay observation.");
    private static ArchetypeDefinition Definition(ArchetypeId id, string name) => new(id, name, [], [], ArchetypeReviewStatus.Reviewed, Evidence);
    private static ArchetypePackDocument Pack(string id, ArchetypeDefinition[] definitions,
        ArchetypeCardMembership[]? memberships = null) => new(
        new(1, new(id), "1", ArchetypePackKind.Synthetic, [ArchetypeContentProfile.TfsClassic, ArchetypeContentProfile.Expanded], [], Evidence),
        new(1, definitions, []), new(1, memberships ?? []), new(1, []), new(1, []));
    private static ArchetypeCardMembership Member(SourceCardId card, params ArchetypeId[] ids) => new(card, ids, ArchetypeReviewStatus.Reviewed, Evidence);
    private static ArchetypePackDocument Base => Pack(BaseId.Value,
        [Definition(Existing, "Existing"), Definition(Other, "Other")], [Member(Original, Existing)]);
    private static ArchetypePackDocument Expansion => Pack("synthetic:expansion", [Definition(New, "Entirely New")],
        [Member(Additional, Existing, New), Member(Original, New)]) with
        { Manifest = new(1, new("synthetic:expansion"), "1", ArchetypePackKind.Expansion,
            [ArchetypeContentProfile.Expanded], [new(BaseId, "1")], Evidence) };
    private static ArchetypeCardFacts[] Facts => [new(Original, CardKind.Monster, Level: 4), new(Additional, CardKind.Monster, Rank: 4)];
    private static ArchetypeContentSnapshot Compile(params ArchetypePackDocument[] packs) =>
        ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, packs, Facts);

    [Fact]
    public void NewCardsJoinExistingAndEntirelyNewGroupsThroughDataWithConsistentReverseLookups()
    {
        var snapshot = Compile(Expansion, Base);
        IArchetypeRegistry registry = snapshot; IArchetypeMembership membership = snapshot;
        Assert.Equal(3, registry.GetAll().Count);
        Assert.Equal("Entirely New", registry.Get(New)!.Name);
        Assert.Equal(new[] { Existing, New }, membership.GetArchetypes(Additional));
        Assert.True(membership.IsMember(Original, New));
        foreach (var card in Facts)
            foreach (var definition in registry.GetAll())
                Assert.Equal(membership.GetArchetypes(card.CardId).Contains(definition.Id), membership.GetMembers(definition.Id).Contains(card.CardId));
        Assert.Equal(BaseId, snapshot.Packs[0].Manifest.PackId);
        Assert.Empty(snapshot.GetMembers(Other));
        Assert.Empty(snapshot.GetArchetypes(new("synthetic:unknown")));
        Assert.Empty(snapshot.GetMembers(new("synthetic:unknown")));
        Assert.False(snapshot.IsMember(default, default));
        Assert.Null(snapshot.Get(default)); Assert.Null(snapshot.GetCard(default));
    }

    [Fact]
    public void ClassicSnapshotAndOriginalPackStayUnchangedWhenExpandedSnapshotIsCompiled()
    {
        var original = Base;
        var classic = ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.TfsClassic, [original], Facts);
        string fingerprint = classic.Fingerprint;
        var expanded = Compile(original, Expansion);
        Assert.True(expanded.IsMember(Additional, Existing));
        Assert.False(classic.IsMember(Additional, Existing)); Assert.False(classic.IsMember(Original, New));
        Assert.Equal(fingerprint, classic.Fingerprint);
        Assert.Single(original.Memberships.Memberships);
        Assert.Throws<InvalidDataException>(() => ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.TfsClassic, [original, Expansion], Facts));
    }

    [Fact]
    public void ExplicitSubsetClosureIsTransitiveAndNamesExternalCodesAndIdentityDoNotInferMembership()
    {
        var root = new ArchetypeId("synthetic:root");
        var pack = Base with
        {
            Definitions = new(1, [Definition(Existing, "Existing"), Definition(New, "Existing Child"), Definition(root, "Root"),
                Definition(Other, "Other") with { ExternalIds = [new("example-setcode", "0x3008", Evidence)] }],
                [new(New, Existing, ArchetypeReviewStatus.Reviewed, Evidence), new(Existing, root, ArchetypeReviewStatus.Reviewed, Evidence)]),
            Memberships = new(1, [Member(Additional, New), Member(Original, Other)]),
            IdentityRules = new(1, [new("synthetic:later-identity", Original, ArchetypeIdentityTreatment.PermanentArchetype,
                ArchetypeReviewStatus.Reviewed, Evidence, Archetype: New)])
        };
        var snapshot = Compile(pack);
        Assert.Equal(new[] { New }, snapshot.GetDirectArchetypes(Additional));
        Assert.True(snapshot.IsMember(Additional, Existing)); Assert.True(snapshot.IsMember(Additional, root));
        Assert.False(snapshot.IsMember(Original, Existing)); Assert.False(snapshot.IsMember(Original, New));
        var noEdges = Compile(pack with { Definitions = pack.Definitions with { Relationships = [] } });
        Assert.False(noEdges.IsMember(Additional, Existing));
    }

    [Fact]
    public void MoreThanNativeSlotCountAndFullWidthSourceIdsNeedNoRegistryEnumChanges()
    {
        var definitions = Enumerable.Range(0, 512).Select(i => Definition(new($"synthetic:group-{i}"), $"Group {i}")).ToArray();
        var snapshot = Compile(Pack("synthetic:many", definitions, [Member(Additional, definitions.Select(d => d.Id).ToArray())]));
        Assert.Equal(512, snapshot.GetArchetypes(Additional).Count);
        Assert.True(snapshot.IsMember(Additional, definitions[^1].Id));
        Assert.Equal(new[] { Additional }, snapshot.GetMembers(definitions[^1].Id));
    }

    [Fact]
    public void KindLevelRankAndTokensAreIndependentFiltersAndDoNotChangeMembership()
    {
        SourceCardId spell = new("synthetic:spell"), trap = new("synthetic:trap"), token = new("synthetic:token"), unknown = new("synthetic:unknown-kind");
        ArchetypeCardFacts[] facts = [.. Facts, new(spell, CardKind.Spell), new(trap, CardKind.Trap),
            new(token, CardKind.Monster, Level: 1, IsToken: true), new(unknown, CardKind.Unknown)];
        var pack = Base with { Memberships = new(1, facts.Select(c => Member(c.CardId, Existing)).ToArray()) };
        var snapshot = ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, [pack], facts);
        Assert.Equal(new[] { Original }, snapshot.GetMembers(Existing, new(Kind: CardKind.Monster, MaxLevel: 4, IsToken: false)));
        Assert.Equal(new[] { Additional }, snapshot.GetMembers(Existing, new(MaxRank: 4)));
        Assert.Equal(new[] { spell }, snapshot.GetMembers(Existing, new(Kind: CardKind.Spell)));
        Assert.Equal(new[] { trap }, snapshot.GetMembers(Existing, new(Kind: CardKind.Trap)));
        Assert.Equal(new[] { token }, snapshot.GetMembers(Existing, new(IsToken: true)));
        Assert.Empty(snapshot.GetMembers(Existing, new(MinLevel: 1, MinRank: 1)));
        Assert.Equal(6, snapshot.GetMembers(Existing).Count);
        Assert.Throws<ArgumentException>(() => snapshot.GetMembers(Existing, new(MinLevel: 5, MaxLevel: 4)));
        Assert.Throws<ArgumentException>(() => snapshot.GetMembers(Existing, new(MinRank: 5, MaxRank: 4)));
    }

    [Fact]
    public void SnapshotDeepFreezesInputCollectionsAndExposesOnlyReadOnlyViews()
    {
        string[] aliases = ["Legacy"];
        ArchetypeExternalIdentifier[] external = [new("example", "opaque", Evidence)];
        ArchetypeDefinition[] definitions = [Definition(Existing, "Existing") with { Aliases = aliases, ExternalIds = external }];
        ArchetypeId[] ids = [Existing];
        ArchetypeCardMembership[] memberships = [Member(Original, ids)];
        ArchetypeContentProfile[] profiles = [ArchetypeContentProfile.Expanded];
        NativeArchetypeMapping[] mappings = [new(9, Existing, ArchetypeReviewStatus.Reviewed, Evidence)];
        var pack = Pack("synthetic:mutable", definitions, memberships);
        pack = pack with { Manifest = pack.Manifest with { Profiles = profiles }, NativeMappings = new(1, mappings) };
        ArchetypePackDocument[] packs = [pack]; var facts = Facts;
        var snapshot = ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, packs, facts);
        string fingerprint = snapshot.Fingerprint;
        aliases[0] = "Edited"; external[0] = new("example", "changed", Evidence);
        definitions[0] = definitions[0] with { Name = "Changed" }; ids[0] = Other;
        memberships[0] = Member(Original, Existing); profiles[0] = ArchetypeContentProfile.TfsClassic;
        mappings[0] = new(9, null, ArchetypeReviewStatus.Reviewed, Evidence); facts[0] = new(Original, CardKind.Spell);
        packs[0] = Base;
        Assert.Equal("Existing", snapshot.Get(Existing)!.Name);
        Assert.Equal("Legacy", Assert.Single(snapshot.Get(Existing)!.Aliases));
        Assert.Equal("opaque", Assert.Single(snapshot.Get(Existing)!.ExternalIds).Value);
        Assert.True(snapshot.IsMember(Original, Existing));
        Assert.Equal(4u, snapshot.GetCard(Original)!.Level);
        Assert.Equal(Existing, snapshot.Packs[0].NativeMappings.Mappings[0].Archetype);
        Assert.Equal(fingerprint, snapshot.Fingerprint);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Get(Existing)!.Aliases)[0] = "Other");
        Assert.Throws<NotSupportedException>(() => ((IList<SourceCardId>)snapshot.GetMembers(Existing))[0] = Additional);
        Assert.Throws<NotSupportedException>(() => ((IList<ArchetypeId>)snapshot.GetArchetypes(Original))[0] = Other);
        Assert.Throws<NotSupportedException>(() => ((IList<ArchetypePackDocument>)snapshot.Packs)[0] = Base);
        Assert.Throws<NotSupportedException>(() => ((IList<ArchetypeCardFacts>)snapshot.Cards)[0] = facts[0]);
    }

    [Fact]
    public void FingerprintAndOrderingAreStableAcrossInputOrderAndChangeWithProfileOrContent()
    {
        var first = Compile(Base, Expansion);
        var reordered = Expansion with { Memberships = new(1, Expansion.Memberships.Memberships.Reverse()
            .Select(m => m with { Archetypes = m.Archetypes.Reverse().ToArray() }).ToArray()) };
        var second = ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, [reordered,
            Base with { Definitions = Base.Definitions with { Definitions = Base.Definitions.Definitions.Reverse().ToArray() },
                Manifest = Base.Manifest with { Profiles = Base.Manifest.Profiles.Reverse().ToArray() } }], Facts.Reverse());
        Assert.Equal(first.Fingerprint, second.Fingerprint); Assert.Equal(64, first.Fingerprint.Length);
        Assert.Equal(first.GetMembers(Existing), second.GetMembers(Existing));
        Assert.Equal(first.Packs.Select(p => p.Manifest.PackId), second.Packs.Select(p => p.Manifest.PackId));
        Assert.NotEqual(Compile(Base).Fingerprint, ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.TfsClassic, [Base], Facts).Fingerprint);
        Assert.NotEqual(Compile(Base).Fingerprint, Compile(Base with { Manifest = Base.Manifest with { Version = "2" } }).Fingerprint);
        Assert.NotEqual(Compile(Base).Fingerprint, ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, [Base],
            [new(Original, CardKind.Monster, Level: 5), Facts[1]]).Fingerprint);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("alias")]
    [InlineData("external")]
    [InlineData("rule")]
    [InlineData("name-treatment")]
    public void IndividuallyValidPacksCannotIntroduceGlobalConflicts(string conflict)
    {
        var first = Pack("synthetic:a", [Definition(Existing, "Existing")]);
        var second = Pack("synthetic:b", [Definition(Other, "Other")]);
        if (conflict == "id") second = second with { Definitions = new(1, [Definition(Existing, "Different")], []) };
        if (conflict == "alias") second = second with { Definitions = new(1, [Definition(Other, "Other") with { Aliases = ["EXISTING"] }], []) };
        if (conflict == "external")
        {
            var external = new ArchetypeExternalIdentifier("example", "opaque", Evidence);
            first = first with { Definitions = new(1, [Definition(Existing, "Existing") with { ExternalIds = [external] }], []) };
            second = second with { Definitions = new(1, [Definition(Other, "Other") with { ExternalIds = [external] }], []) };
        }
        if (conflict is "rule" or "name-treatment")
        {
            first = first with { IdentityRules = new(1, [new("synthetic:rule", Original, ArchetypeIdentityTreatment.PermanentName, ArchetypeReviewStatus.Reviewed, Evidence, Name: "A")]) };
            second = second with { IdentityRules = new(1, [new(conflict == "rule" ? "synthetic:rule" : "synthetic:other-rule",
                Original, ArchetypeIdentityTreatment.PermanentName, ArchetypeReviewStatus.Reviewed, Evidence, Name: "B")]) };
        }
        Assert.Throws<InvalidDataException>(() => Compile(first, second));
    }

    [Fact]
    public void CombinedSubsetCyclesAreRejectedEvenWhenEachContributingPackIsLocallyAcyclic()
    {
        ArchetypePackDocument Relation(string id, ArchetypeId child, ArchetypeId parent) => Pack(id, []) with
        {
            Manifest = Pack(id, []).Manifest with { Dependencies = [new(BaseId, "1")] },
            Definitions = new(1, [], [new(child, parent, ArchetypeReviewStatus.Reviewed, Evidence)])
        };
        Assert.Throws<InvalidDataException>(() => Compile(Base, Relation("synthetic:one", Existing, Other), Relation("synthetic:two", Other, Existing)));
    }

    [Fact]
    public void MissingWrongVersionDuplicateAndCyclicPackDependenciesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Compile(Expansion));
        Assert.Throws<InvalidDataException>(() => Compile(Base with { Manifest = Base.Manifest with { Version = "2" } }, Expansion));
        Assert.Throws<InvalidDataException>(() => Compile(Base, Base));
        var a = Pack("synthetic:a", []); var b = Pack("synthetic:b", []);
        a = a with { Manifest = a.Manifest with { Dependencies = [new(b.Manifest.PackId, "1")] } };
        b = b with { Manifest = b.Manifest with { Dependencies = [new(a.Manifest.PackId, "1")] } };
        Assert.Throws<InvalidDataException>(() => Compile(a, b));
    }

    [Fact]
    public void CandidatesAndUnknownReferencesCannotEnterAReviewedSnapshot()
    {
        Assert.Throws<InvalidDataException>(() => Compile(Base with { Definitions = Base.Definitions with
            { Definitions = [Definition(Existing, "Existing") with { Review = ArchetypeReviewStatus.Candidate }] } }));
        Assert.Throws<InvalidDataException>(() => Compile(Base with { Memberships = new(1, [Member(Original, New)]) }));
        Assert.Throws<InvalidDataException>(() => Compile(Base with { Memberships = new(1, [Member(new("synthetic:missing-card"), Existing)]) }));
    }

    [Theory]
    [InlineData("both-stats")]
    [InlineData("spell-level")]
    [InlineData("token-spell")]
    [InlineData("duplicate")]
    [InlineData("default-id")]
    [InlineData("null-entry")]
    public void InvalidCardFactsAreRejectedBeforeIndexesArePublished(string invalid)
    {
        var facts = Facts;
        facts[0] = invalid switch
        {
            "both-stats" => facts[0] with { Rank = 4 },
            "spell-level" => facts[0] with { Kind = CardKind.Spell },
            "token-spell" => new(Original, CardKind.Spell, IsToken: true),
            "duplicate" => facts[1],
            "default-id" => facts[0] with { CardId = default },
            _ => null!
        };
        Assert.Throws<InvalidDataException>(() => ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, [Base], facts));
    }
}
