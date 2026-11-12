using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

public sealed class ArchetypeIntegrationTests : IDisposable
{
    private const uint Original = 1, Probe = 3, Filler = 4, Added = uint.MaxValue;
    private static readonly ArchetypeId Parent = new("official:elemental-hero"), Child = new("synthetic:future");
    private static readonly ArchetypePackId BaseId = new("synthetic:base"), ExpansionId = new("synthetic:expansion");
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-archetype-integration-" + Guid.NewGuid().ToString("N"));
    private static ArchetypeEvidence Evidence => new("synthetic package-to-gameplay fixture", new string('c', 64), "Synthetic integration, not PSP runtime evidence.");
    private static SourceCardId Source(uint id) => new("synthetic:card-" + id);
    private static readonly uint[] Ids = [Original, Probe, Filler, Added];
    private static ArchetypeCatalogCard CatalogCard(uint id) => new(id,
        new(Source(id), id == Probe ? CardKind.Spell : CardKind.Monster, Level: id == Probe ? null : 4), ArchetypeReviewStatus.Reviewed, Evidence);
    private static ArchetypePackageDocument Base => new(new(
        new(1, BaseId, "1", ArchetypePackKind.Synthetic, [ArchetypeContentProfile.TfsClassic, ArchetypeContentProfile.Expanded], [], Evidence),
        new(1, [new(Parent, "Synthetic parent", [], [], ArchetypeReviewStatus.Reviewed, Evidence)], []),
        new(1, []), new(1, []), new(1, [])), new(1, [CatalogCard(Original), CatalogCard(Probe), CatalogCard(Filler)]), new(1, []), new(1, []));
    private static ArchetypePackageDocument Expansion(string version, bool member = true) => new(new(
        new(1, ExpansionId, version, ArchetypePackKind.Expansion, [ArchetypeContentProfile.Expanded], [new(BaseId, "1")], Evidence),
        new(1, [new(Child, "Synthetic child", [], [], ArchetypeReviewStatus.Reviewed, Evidence)],
            [new(Child, Parent, ArchetypeReviewStatus.Reviewed, Evidence)]),
        new(1, member ? [new(Source(Added), [Child], ArchetypeReviewStatus.Reviewed, Evidence)] : []), new(1, []), new(1, [])),
        new(1, [CatalogCard(Added)]), new(1, []), new(1, []));
    private ArchetypePackageReference Write(ArchetypePackageDocument package, string name) => ArchetypePackageStore.Write(Path.Combine(root, name), package,
        new(Ids.Select(Source).ToArray(), [new(BaseId, "1", [Parent])]));
    private static ArchetypeLoadedContent Load(ArchetypePackageReference original, ArchetypePackageReference expansion, bool classic = false) =>
        ArchetypeContentLoader.Load(new(1, classic ? ArchetypeContentProfile.TfsClassic : ArchetypeContentProfile.Expanded,
            [classic ? new(BaseId, "1") : new(ExpansionId, expansion.Version)], [expansion, original]));
    private static DuelEngine Engine(ArchetypeLoadedContent content)
    {
        var filter = TemplateCardFamily.Family.Definitions.Single(d => d.CardId == 6672).Steps.OfType<AddFromDeckToHandStep>().Single().Filter;
        var catalog = new CardCatalog(new(null, "synthetic-integrated-catalog"), Ids.Select(id =>
            new CardDefinition(id, "Unrelated printed name " + id, "", new(1000, 1000, 4), Kind: id == Probe ? CardKind.Spell : CardKind.Monster)));
        var rules = Ids.ToDictionary(id => id, id => id == Probe ? new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true)
            : new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true));
        uint[] first = [Probe, Filler, Filler, Filler, Filler, Added, .. Enumerable.Repeat(Filler, 34)];
        return new(catalog, rules, first, Enumerable.Repeat(Filler, 40), shuffleSeed: 19,
            effectDefinitions: [new("synthetic:integrated-search", Probe, EffectKind.NormalSpell, [new AddFromDeckToHandStep(filter)])],
            archetypeResolver: new ArchetypeIdentityResolver(content));
    }
    private static ActivateSpell Search(DuelEngine engine) => new(PlayerId.One,
        engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == Probe).InstanceId, 0);
    private static void FinishSearch(DuelEngine engine)
    {
        for (int i = 0; i < 30; i++)
        {
            if (engine.State.PendingChain is not { } chain) return;
            DuelCommand command = chain.DefinitionCardChoice is { } choice
                ? new ChooseDefinitionCard(choice.Player, choice.DecisionId, Assert.Single(choice.Options).InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(engine.State.ActivePlayer, chain.DecisionId);
            Ok(engine, command);
        }
        throw new InvalidOperationException("Search did not finish.");
    }

    [Fact]
    public void LoadedJsonExpansionAndSubsetRelationshipDriveUnchangedMigratedGameplay()
    {
        var original = Write(Base, "base"); var expansion = Write(Expansion("1"), "expanded");
        var classic = Load(original, expansion, classic: true); string identity = classic.Fingerprint;
        var expanded = Load(original, expansion); Assert.True(expanded.IsMember(Source(Added), Parent));
        var allowed = Engine(expanded); EnterMain(allowed); Ok(allowed, Search(allowed)); FinishSearch(allowed);
        Assert.Contains(allowed.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Added);
        var denied = Engine(classic); EnterMain(denied); var before = denied.State; var rejected = denied.Execute(Search(denied));
        Assert.False(rejected.Succeeded); Assert.Same(before, denied.State); Assert.Empty(rejected.Events);
        Assert.Equal(identity, classic.Fingerprint); Assert.Null(classic.GetSourceCard(Added));
    }

    [Fact]
    public void AnAlreadyActivatedSearchKeepsItsSelectionAcrossDiskTamperingAndNewPackVersions()
    {
        var original = Write(Base, "base"); var revision = Write(Expansion("1"), "v1"); var selected = Load(original, revision);
        var retained = Engine(selected); EnterMain(retained); Ok(retained, Search(retained));
        File.AppendAllText(Path.Combine(revision.Directory, "memberships.json"), " ");
        Assert.Throws<InvalidDataException>(() => Load(original, revision));
        var newer = Load(original, Write(Expansion("2", member: false), "v2"));
        Assert.NotEqual(selected.Fingerprint, newer.Fingerprint);
        var denied = Engine(newer); EnterMain(denied); Assert.False(denied.Execute(Search(denied)).Succeeded);
        FinishSearch(retained); Assert.Contains(retained.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Added);
    }

    [Fact]
    public void FileLoadedConditionalIdentityIsEvaluatedFromRealFieldAndHandContexts()
    {
        var rule = new ArchetypeIdentityRule("synthetic:conditional", Source(Original), ArchetypeIdentityTreatment.ConditionalArchetype,
            ArchetypeReviewStatus.Reviewed, Evidence, Archetype: Parent, Condition: new([CardIdentityLocation.MonsterZone]));
        var authoring = Base with { Pack = Base.Pack with { IdentityRules = new(1, [rule]) } };
        var original = Write(authoring, "conditional"); var content = ArchetypeContentLoader.Load(new(1, ArchetypeContentProfile.TfsClassic, [new(BaseId, "1")], [original]));
        var resolver = new ArchetypeIdentityResolver(content);
        var catalog = new CardCatalog(new(null, "synthetic-conditional-catalog"), [new(Original, "Unrelated name", "", new(1000, 1000, 4))]);
        var engine = new DuelEngine(catalog, new Dictionary<uint, DuelCardRules> { [Original] = new(CardCategory.Monster, AllowsNormalSummon: true) },
            Enumerable.Repeat(Original, 40), Enumerable.Repeat(Original, 40));
        EnterMain(engine); var card = engine.State.GetPlayer(PlayerId.One).Hand[0];
        Assert.False(resolver.Resolve(engine, card)!.IsMember(Parent));
        Ok(engine, new NormalSummon(PlayerId.One, card.InstanceId, 0)); Assert.True(resolver.Resolve(engine, card)!.IsMember(Parent));
        Assert.False(content.Snapshot.IsMember(Source(Original), Parent));
    }

    public void Dispose()
    {
        string target = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath());
        if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("tfsp-archetype-integration-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
