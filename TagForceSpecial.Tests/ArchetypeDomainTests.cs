using System.Text.Json;
using TagForceSpecial.Effects.Archetypes;

namespace TagForceSpecial.Tests;

public sealed class ArchetypeDomainTests
{
    private static readonly ArchetypeId Parent = new("synthetic:constellation");
    private static readonly ArchetypeId Child = new("synthetic:little-stars");
    private static readonly SourceCardId Card = new("synthetic:future-card");
    private static ArchetypeEvidence Evidence => new("synthetic authoring fixture", new string('a', 64), "Explicit synthetic declaration, not a native observation.");
    private static ArchetypeDefinition Definition(ArchetypeId id, string name) => new(id, name, [], [], ArchetypeReviewStatus.Reviewed, Evidence);
    private static ArchetypeValidationContext Context => new([Card], []);
    private static ArchetypePackDocument Pack => new(
        new(1, new("synthetic:future-pack"), "1.0.0", ArchetypePackKind.Synthetic, [ArchetypeContentProfile.Expanded], [], Evidence),
        new(1, [Definition(Parent, "Constellation"), Definition(Child, "Little Stars")],
            [new(Child, Parent, ArchetypeReviewStatus.Reviewed, Evidence)]),
        new(1, [new(Card, [Parent, Child], ArchetypeReviewStatus.Reviewed, Evidence)]),
        new(1, []), new(1, []));

    private static ArchetypePackDocument RoundTrip(ArchetypePackDocument pack, ArchetypeValidationContext? context = null,
        ArchetypeValidationMode mode = ArchetypeValidationMode.ReviewedContent) => ArchetypePackJson.Read(
            ArchetypePackJson.WriteDocument(pack.Manifest), ArchetypePackJson.WriteDocument(pack.Definitions),
            ArchetypePackJson.WriteDocument(pack.Memberships), ArchetypePackJson.WriteDocument(pack.IdentityRules),
            ArchetypePackJson.WriteDocument(pack.NativeMappings), context ?? Context, mode);

    [Fact]
    public void EntirelyNewArchetypesAndMultipleMembershipsValidateFromSeparateJsonDocuments()
    {
        var loaded = RoundTrip(Pack);
        Assert.Equal(Parent, loaded.Definitions.Definitions[0].Id);
        Assert.Equal(new[] { Parent, Child }, loaded.Memberships.Memberships[0].Archetypes);
        Assert.Equal(Card, loaded.Memberships.Memberships[0].CardId);
        Assert.Empty(loaded.NativeMappings.Mappings);
        Assert.Equal(ArchetypePackJson.WriteDocument(Pack.Definitions), ArchetypePackJson.WriteDocument(loaded.Definitions));
    }

    [Fact]
    public void DomainHasNoNativeGroupCountOrCardIdWidthLimit()
    {
        var ids = Enumerable.Range(0, 300).Select(i => new ArchetypeId($"synthetic:group-{i}")).ToArray();
        var card = new SourceCardId($"tfs:{uint.MaxValue}");
        var pack = Pack with
        {
            Definitions = new(1, ids.Select(id => Definition(id, id.Value)).ToArray(), []),
            Memberships = new(1, [new(card, ids, ArchetypeReviewStatus.Reviewed, Evidence)])
        };
        Assert.Empty(ArchetypePackValidator.Validate(pack, new([card], [])));
        Assert.Equal(card, RoundTrip(pack, new([card], [])).Memberships.Memberships[0].CardId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hero")]
    [InlineData("HERO:elemental")]
    [InlineData("official:Elemental HERO")]
    [InlineData("official:")]
    [InlineData(":hero")]
    [InlineData("official:hero:extra")]
    [InlineData("official:hero\n")]
    public void MalformedKeysAreRejectedBeforeTheyBecomeDomainIdentifiers(string value)
    {
        Assert.Throws<ArgumentException>(() => new ArchetypeId(value));
        Assert.Throws<ArgumentException>(() => new ArchetypePackId(value));
        Assert.Throws<ArgumentException>(() => new SourceCardId(value));
    }

    [Fact]
    public void UninitializedIdentifiersAreRejectedEvenWhenConstructedWithoutJson()
    {
        var pack = Pack with { Definitions = new(1, [Definition(default, "Invalid")], []) };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "invalid-id");
        Assert.Throws<JsonException>(() => ArchetypePackJson.WriteDocument(pack.Definitions));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"definitions\":[],\"relationships\":[],\"typo\":true}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2,\"definitions\":[],\"relationships\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"definitions\":[]}")]
    [InlineData("null")]
    public void JsonRejectsUnknownDuplicateMissingAndNullDocumentFields(string json) =>
        Assert.Throws<JsonException>(() => ArchetypePackJson.ReadDocument<ArchetypeDefinitionsDocument>(json));

    [Fact]
    public void JsonRejectsNumericAndUnknownEnumsAndNestedUnknownFields()
    {
        string json = ArchetypePackJson.WriteDocument(Pack.Definitions);
        Assert.Throws<JsonException>(() => ArchetypePackJson.ReadDocument<ArchetypeDefinitionsDocument>(json.Replace("\"reviewed\"", "1")));
        Assert.Throws<JsonException>(() => ArchetypePackJson.ReadDocument<ArchetypeDefinitionsDocument>(json.Replace("\"reviewed\"", "\"approved\"")));
        Assert.Throws<JsonException>(() => ArchetypePackJson.ReadDocument<ArchetypeDefinitionsDocument>(json.Replace("\"reason\":", "\"typo\": 1, \"reason\":")));
    }

    [Fact]
    public void CandidatesAreRetainedOnlyInDiagnosticMode()
    {
        var pack = Pack with { Definitions = Pack.Definitions with
            { Definitions = [Definition(Parent, "Constellation") with { Review = ArchetypeReviewStatus.Candidate }, Definition(Child, "Little Stars")] } };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "unreviewed");
        Assert.Throws<InvalidDataException>(() => RoundTrip(pack));
        Assert.Empty(ArchetypePackValidator.Validate(pack, Context, ArchetypeValidationMode.Diagnostic));
        Assert.Equal(ArchetypeReviewStatus.Candidate, RoundTrip(pack, mode: ArchetypeValidationMode.Diagnostic).Definitions.Definitions[0].Review);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("alias")]
    [InlineData("membership")]
    [InlineData("relationship")]
    [InlineData("native")]
    public void ConflictingDeclarationsAreReported(string kind)
    {
        var pack = Pack;
        pack = kind switch
        {
            "definition" => pack with { Definitions = pack.Definitions with { Definitions = [Definition(Parent, "A"), Definition(Parent, "B")] } },
            "alias" => pack with { Definitions = pack.Definitions with { Definitions = [Definition(Parent, "Constellation") with { Aliases = ["Little Stars"] }, Definition(Child, "Little Stars")] } },
            "membership" => pack with { Memberships = new(1, [pack.Memberships.Memberships[0], pack.Memberships.Memberships[0]]) },
            "relationship" => pack with { Definitions = pack.Definitions with { Relationships = [pack.Definitions.Relationships[0], pack.Definitions.Relationships[0]] } },
            _ => pack with { NativeMappings = new(1, [new(9, Parent, ArchetypeReviewStatus.Reviewed, Evidence), new(9, Child, ArchetypeReviewStatus.Reviewed, Evidence)]) }
        };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code.StartsWith("duplicate-", StringComparison.Ordinal));
    }

    [Fact]
    public void SubsetCyclesAndSelfMembershipAreRejectedWithoutApplyingImplicitSetcodeSemantics()
    {
        var pack = Pack with { Definitions = Pack.Definitions with { Relationships =
            [new(Child, Parent, ArchetypeReviewStatus.Reviewed, Evidence), new(Parent, Child, ArchetypeReviewStatus.Reviewed, Evidence)] } };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "relationship-cycle");
        pack = pack with { Definitions = pack.Definitions with { Relationships = [new(Parent, Parent, ArchetypeReviewStatus.Reviewed, Evidence)] } };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "self-relationship");
    }

    [Fact]
    public void ReferencesRequireKnownCardsAndExactlyVersionedDeclaredDependencies()
    {
        var dependency = new ArchetypePackId("synthetic:base");
        var pack = Pack with
        {
            Manifest = Pack.Manifest with { Dependencies = [new(dependency, "2.0.0")] },
            Definitions = new(1, [Definition(Child, "Little Stars")], [])
        };
        var exports = new ArchetypeDependencyExports(dependency, "2.0.0", [Parent]);
        var context = new ArchetypeValidationContext([Card], [exports]);
        Assert.Empty(ArchetypePackValidator.Validate(pack, context));
        Assert.Contains(ArchetypePackValidator.Validate(pack, context with { Cards = [] }), i => i.Code == "unresolved-card");
        Assert.Contains(ArchetypePackValidator.Validate(pack, context with { Dependencies = [exports with { Version = "1.0.0" }] }), i => i.Code == "unresolved-dependency");
        Assert.Contains(ArchetypePackValidator.Validate(pack with { Manifest = pack.Manifest with { Dependencies = [] } }, context), i => i.Code == "unresolved-archetype");
    }

    [Fact]
    public void ExpansionCannotTargetClassicOrReplaceNativeMappings()
    {
        var pack = Pack with
        {
            Manifest = Pack.Manifest with { Kind = ArchetypePackKind.Expansion, Profiles = [ArchetypeContentProfile.TfsClassic] },
            NativeMappings = new(1, [new(9, Parent, ArchetypeReviewStatus.Reviewed, Evidence)])
        };
        var issues = ArchetypePackValidator.Validate(pack, Context);
        Assert.Contains(issues, i => i.Code == "classic-expansion");
        Assert.Contains(issues, i => i.Code == "expansion-native-mapping");
    }

    [Fact]
    public void NameAndArchetypeTreatmentRemainSeparateAndDoNotChangeStaticMembership()
    {
        var pack = Pack with { IdentityRules = new(1,
            [new("synthetic:name-rule", Card, ArchetypeIdentityTreatment.PermanentName, ArchetypeReviewStatus.Reviewed, Evidence, Name: "Other Name"),
             new("synthetic:group-rule", Card, ArchetypeIdentityTreatment.PermanentArchetype, ArchetypeReviewStatus.Reviewed, Evidence, Archetype: Parent)]) };
        var loaded = RoundTrip(pack);
        Assert.Equal(2, loaded.IdentityRules.Rules.Count);
        Assert.Equal(2, Assert.Single(loaded.Memberships.Memberships).Archetypes.Count);
        pack = pack with { IdentityRules = new(1, [pack.IdentityRules.Rules[0] with { Archetype = Parent }]) };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "invalid-treatment");
    }

    [Fact]
    public void UnknownNativeLabelsCanBePreservedWithoutInventingASemanticIdentifier()
    {
        var pack = Pack with { NativeMappings = new(1, [new(204, null, ArchetypeReviewStatus.Reviewed, Evidence)]) };
        Assert.Null(Assert.Single(RoundTrip(pack).NativeMappings.Mappings).Archetype);
    }

    [Fact]
    public void ExternalIdentifiersAreOpaqueMappingsWithProvenanceAndCannotConflict()
    {
        var external = new ArchetypeExternalIdentifier("synthetic-external", "0x3008", Evidence);
        var pack = Pack with { Definitions = Pack.Definitions with { Definitions =
            [Definition(Parent, "Constellation") with { ExternalIds = [external] }, Definition(Child, "Little Stars")] } };
        Assert.Equal(external, Assert.Single(RoundTrip(pack).Definitions.Definitions[0].ExternalIds));
        pack = pack with { Definitions = pack.Definitions with { Definitions =
            [pack.Definitions.Definitions[0], Definition(Child, "Little Stars") with { ExternalIds = [external] }] } };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "duplicate-external-id");
    }

    [Fact]
    public void SourceNamespacesPreventAccidentalCardIdCollisionsAndNamesCannotContradict()
    {
        var native = new SourceCardId("tfs:1");
        var additional = new SourceCardId("synthetic:1");
        var pack = Pack with { Memberships = new(1,
            [new(native, [Parent], ArchetypeReviewStatus.Reviewed, Evidence),
             new(additional, [Child], ArchetypeReviewStatus.Reviewed, Evidence)]) };
        Assert.Empty(ArchetypePackValidator.Validate(pack, new([native, additional], [])));
        pack = Pack with { IdentityRules = new(1,
            [new("synthetic:name-a", Card, ArchetypeIdentityTreatment.PermanentName, ArchetypeReviewStatus.Reviewed, Evidence, Name: "A"),
             new("synthetic:name-b", Card, ArchetypeIdentityTreatment.PermanentName, ArchetypeReviewStatus.Reviewed, Evidence, Name: "B")]) };
        Assert.Contains(ArchetypePackValidator.Validate(pack, Context), i => i.Code == "conflicting-name-treatment");
    }

    [Fact]
    public void SchemaProvenanceAndNullEntriesFailAsStructuredDiagnostics()
    {
        var pack = Pack with
        {
            Manifest = Pack.Manifest with { SchemaVersion = 2, Evidence = Evidence with { Sha256 = "not-a-hash" } },
            Definitions = new(1, [null!], null!),
            Memberships = new(1, null!), IdentityRules = new(1, [null!]), NativeMappings = new(1, [null!])
        };
        var issues = ArchetypePackValidator.Validate(pack, Context);
        Assert.Contains(issues, i => i.Code == "schema-version");
        Assert.Contains(issues, i => i.Code == "invalid-hash");
        Assert.Contains(issues, i => i.Code == "null-entry");
        Assert.Contains(issues, i => i.Code == "missing-list");
        Assert.Equal(issues, issues.OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal));
    }
}
