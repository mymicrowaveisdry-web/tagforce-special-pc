using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

public sealed class ArchetypeMigrationTests
{
    private const uint Subject = 1, Member = 2, Probe = 3, Filler = 4, SpellMember = 5, RankMember = 6;
    private static readonly ArchetypeId Group = new("official:elemental-hero");
    private static SourceCardId Source(uint id) => new("synthetic:" + id);
    private static ArchetypeEvidence Evidence => new("synthetic migration fixture", new string('a', 64), "Synthetic mechanics, not a PSP observation.");
    private static readonly uint[] Ids = [Subject, Member, Probe, Filler, SpellMember, RankMember];
    private static ArchetypeIdentityResolver Resolver(bool conditional = false, bool empty = false)
    {
        var facts = Ids.Select(id => new ArchetypeCardFacts(Source(id), id is Probe or SpellMember ? CardKind.Spell : CardKind.Monster,
            Level: id is Subject or Member or Filler ? 4 : null, Rank: id == RankMember ? 4 : null)).ToArray();
        var rules = conditional ? new[] { new ArchetypeIdentityRule("synthetic:conditional", Source(Subject), ArchetypeIdentityTreatment.ConditionalArchetype,
            ArchetypeReviewStatus.Reviewed, Evidence, Archetype: Group, Condition: new([CardIdentityLocation.MonsterZone])) } : [];
        var pack = new ArchetypePackDocument(new(1, new("synthetic:migration"), "1", ArchetypePackKind.Synthetic,
            [ArchetypeContentProfile.Expanded], [], Evidence), new(1, [new(Group, "Synthetic group", [], [], ArchetypeReviewStatus.Reviewed, Evidence)], []),
            new(1, empty ? [] : new[] { Member, SpellMember, RankMember }.Select(id => new ArchetypeCardMembership(Source(id), [Group], ArchetypeReviewStatus.Reviewed, Evidence)).ToArray()),
            new(1, rules), new(1, []));
        var snapshot = ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, [pack], facts);
        return new(snapshot, Ids.ToDictionary(id => id, Source));
    }
    private static DuelEngine Engine(EffectDefinition definition, uint[] first, ICardArchetypeResolver? selected = null, uint[]? second = null)
    {
        var catalog = new CardCatalog(new(null, "synthetic-migration"), Ids.Select(id => new CardDefinition(id,
            id == Filler ? "Elemental HERO Lookalike" : "Unrelated name " + id, "", new(1000, 1000, 4),
            new(1, 1, 1, id == RankMember ? 22u : id is Probe or SpellMember ? 13u : 1u, 0, 0, 0),
            Kind: id is Probe or SpellMember ? CardKind.Spell : CardKind.Monster)));
        var rules = Ids.ToDictionary(id => id, id => id is Probe or SpellMember ? new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true)
            : new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true));
        uint[] Deck(uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
        return new(catalog, rules, Deck(first), Deck(second ?? []), effectDefinitions: [definition], shuffleSeed: 7, archetypeResolver: selected);
    }
    private static CardInstance Hand(DuelEngine e, uint id) => e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
    private static void Cast(DuelEngine e) => Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Probe).InstanceId, 0));
    private static void Resolve(DuelEngine e, CardInstance? target = null)
    {
        for (int i = 0; i < 50; i++)
        {
            DuelCommand? command = e.State.PendingDecision is { FieldTargetOptions: { } options } d && target is not null
                ? options.First(t => t.Card == target) is { } t ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, t.Player, t.Location, t.Zone, target.InstanceId) : null
                : e.State.PendingChain is not { } c ? null
                : c.DefinitionCardChoice is { } choice ? new ChooseDefinitionCard(choice.Player, choice.DecisionId, choice.Options[0].InstanceId)
                : c.NextLink == 0 ? new PassChainResponse(c.Responder, c.DecisionId)
                : new ResolveNextChainLink(e.State.ActivePlayer, c.DecisionId);
            if (command is null) return;
            Ok(e, command);
        }
        throw new InvalidOperationException("Chain did not finish.");
    }

    public static IEnumerable<object[]> LegacyScopes()
    {
        // Hashes pin the reviewed pre-migration sets, with the explicit Contrast HERO correction applied.
        yield return ["elemental-hero", true, false, 66, "6ec046526c9c77aa96efa20305332ab13585e23d967f6581df0ab1c61696d78d"];
        yield return ["nordic", true, false, 14, "a8e394d22e3ceda8741ef2977f08db2a2c45e69d1241854683d0f4acfd4bf1c3"];
        yield return ["resonator", true, false, 8, "cdc49a58f05c60ca8f19159c039057236189f4f25d22f720de4cea6a25fdfaf1"];
        yield return ["hieratic", true, false, 12, "e4f7523e33e559541949bc3657b0d7c999fc0c8fda99ca7000aee4f9c611007d"];
        yield return ["ice-barrier", true, false, 27, "0435552e64c8671be1102eace70a326f55f22001fa4ea668cafed11bf18ccf12"];
        yield return ["dark-world", true, true, 10, "54d80cef88620053e035df161cc9c974bb338e21021fca4f1f2c3595036fb240"];
        yield return ["number", true, false, 101, "7a69d760cd10971f234dbbb1642dbecf0ac2b74d72b28bcc86b3a4616ffb9d35"];
        yield return ["number-c", true, false, 24, "37a54d6d0c735cea77070c46f9d10bd4276601b1dec38f8740d1fe7637a2590d"];
        yield return ["qli", false, false, 15, "85e81e146900166ded704150ccde3776ab0f94d064b137084282f42626ab30fc"];
        yield return ["dd", false, false, 16, "a59eb4e05dc8036b153122cc650513f1de0c2d129ea4a18ff1a0a94fc5d6e27e"];
        yield return ["covenant", false, false, 3, "3529394edde5c5b049fa230c9c71a2667d3c70ab1941dd2773b1ed2c43eb8f44"];
        yield return ["cxyz", true, false, 8, "1d0454be8e1b32818b2d0aa7ac97fee9ec51c11e0eabd76d2b79222f4d9f20e2"];
    }

    [Theory]
    [MemberData(nameof(LegacyScopes))]
    public void AllTwelveScopesMatchTheReviewedBaseline(string group, bool monster, bool lowLevel, int count, string sha256)
    {
        var members = ClassicArchetypes.GetMembers("official:" + group,
            new(Kind: monster ? CardKind.Monster : null, MaxLevel: lowLevel ? 4u : null));
        Assert.Equal(count, members.Count);
        string text = string.Join(",", members);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void ClassicIncludesPermanentContrastHeroTreatmentAndBroaderSpellTrapScopes()
    {
        var context = new CardIdentityState(new(1, 11310, PlayerId.One), "Contrast HERO Chaos", CardIdentityLocation.MonsterZone,
            PlayerId.One, true, true, 1, 6310, "A different name");
        Assert.True(ClassicArchetypes.Resolver.IsMember(context, "official:elemental-hero"));
        Assert.Contains(ClassicArchetypes.GetMembers("official:qli"), id => ClassicArchetypes.Snapshot.GetCard(new("tfs:" + id))!.Kind != CardKind.Monster);
        Assert.All(ClassicArchetypes.GetMembers("official:covenant"), id => Assert.NotEqual(CardKind.Monster, ClassicArchetypes.Snapshot.GetCard(new("tfs:" + id))!.Kind));
        Assert.Equal(59, ClassicArchetypes.Snapshot.GetAll().Count); Assert.Equal(7254, ClassicArchetypes.Snapshot.Cards.Count);
    }

    /// <summary>Reviewed native slots agree with each other where one archetype lies inside another, and unreviewed slots stay out.</summary>
    [Theory]
    [InlineData("official:destiny-hero", "official:hero")]
    [InlineData("official:elemental-hero", "official:hero")]
    [InlineData("official:gem-knight", "official:gem")]
    [InlineData("official:steelswarm", "official:lswarm")]
    public void ClassicSubArchetypesLieInsideTheirParentSlots(string child, string parent)
    {
        var members = ClassicArchetypes.GetMembers(child);
        Assert.NotEmpty(members);
        Assert.Empty(members.Except(ClassicArchetypes.GetMembers(parent)));
        Assert.Throws<ArgumentException>(() => ClassicArchetypes.Filter("official:worm"));
    }

    [Fact]
    public void SyntheticExpansionUsesUnchangedMigratedSearchAndDoesNotInferMembershipFromNames()
    {
        var migrated = TemplateCardFamily.Family.Definitions.Single(d => d.CardId == 6672).Steps.OfType<AddFromDeckToHandStep>().Single().Filter;
        var definition = new EffectDefinition("synthetic:search", Probe, EffectKind.NormalSpell, [new AddFromDeckToHandStep(migrated)]);
        var selected = Resolver(); string classic = ClassicArchetypes.Resolver.Fingerprint;
        var e = Engine(definition, [Probe, Filler, Filler, Filler, Filler, Member], selected);
        EnterMain(e); Cast(e); Resolve(e);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Member);
        Assert.Equal(classic, ClassicArchetypes.Resolver.Fingerprint);
        var noMember = Engine(definition, [Probe], selected); EnterMain(noMember);
        var before = noMember.State; var rejected = noMember.Execute(new ActivateSpell(PlayerId.One, Hand(noMember, Probe).InstanceId, 0));
        Assert.False(rejected.Succeeded); Assert.Same(before, noMember.State); Assert.Empty(rejected.Events);
    }

    [Fact]
    public void KindLevelAndRankRestrictionsStayIndependentOfSemanticMembership()
    {
        var resolver = Resolver();
        var filter = new AllOfFilter(new ArchetypeFilter(Group.Value, resolver), new MonsterLevelFilter(4));
        var definition = new EffectDefinition("synthetic:level-search", Probe, EffectKind.NormalSpell, [new AddFromDeckToHandStep(filter)]);
        var rejected = Engine(definition, [Probe, Filler, Filler, Filler, Filler, RankMember, SpellMember]); EnterMain(rejected);
        var result = rejected.Execute(new ActivateSpell(PlayerId.One, Hand(rejected, Probe).InstanceId, 0)); Assert.False(result.Succeeded);
        var allowed = Engine(definition, [Probe, Filler, Filler, Filler, Filler, Member]); EnterMain(allowed); Cast(allowed); Resolve(allowed);
        Assert.Contains(allowed.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Member);
    }

    [Fact]
    public void SemanticFilterReadsTheNameChangedEarlierInTheSameResolvingEffect()
    {
        var filter = new ArchetypeFilter(Group.Value, Resolver());
        var definition = new EffectDefinition("synthetic:name-then-destroy", Probe, EffectKind.NormalSpell,
            [new ChangeTargetsNameStep(Member), new DestroyAllStep(FieldCardKind.Monster, Cards: filter)],
            fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp)]);
        var e = Engine(definition, [Probe, Subject], second: [Member]); EnterMain(e); Next(e);
        var existing = e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == Member);
        Ok(e, new NormalSummon(PlayerId.Two, existing.InstanceId, 0)); Next(e); var subject = Hand(e, Subject);
        Ok(e, new NormalSummon(PlayerId.One, subject.InstanceId, 0)); Cast(e); Resolve(e, subject);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Contains(subject, e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Contains(existing, e.State.GetPlayer(PlayerId.Two).Graveyard);
    }

    [Fact]
    public void ConditionalMembershipStopsWhenAnEarlierStepFlipsTheMonsterFaceDown()
    {
        var resolver = Resolver(conditional: true);
        var definition = new EffectDefinition("synthetic:flip-then-destroy", Probe, EffectKind.NormalSpell,
            [new FlipTargetsFaceDownStep(), new DestroyAllStep(FieldCardKind.Monster, Cards: new ArchetypeFilter(Group.Value, resolver))],
            fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp)]);
        var e = Engine(definition, [Probe, Subject]); EnterMain(e); var subject = Hand(e, Subject);
        Ok(e, new NormalSummon(PlayerId.One, subject.InstanceId, 0)); Assert.True(resolver.Resolve(e, subject)!.IsMember(Group));
        Cast(e); Resolve(e, subject);
        Assert.Equal(BattlePosition.FaceDownDefense, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
        Assert.False(resolver.Resolve(e, subject)!.IsMember(Group));
    }

    [Fact]
    public void AuthoredDefaultContentRemainsPinnedWhenAnotherSelectionIsUsed()
    {
        var original = Resolver(); var empty = Resolver(empty: true);
        var definition = new EffectDefinition("synthetic:pinned-search", Probe, EffectKind.NormalSpell,
            [new AddFromDeckToHandStep(new ArchetypeFilter(Group.Value, original))]);
        Assert.NotEqual(original.Fingerprint, empty.Fingerprint);
        var changed = Engine(definition, [Probe, Filler, Filler, Filler, Filler, Member], empty); EnterMain(changed);
        Assert.False(changed.Execute(new ActivateSpell(PlayerId.One, Hand(changed, Probe).InstanceId, 0)).Succeeded);
        var retained = Engine(definition, [Probe, Filler, Filler, Filler, Filler, Member]); EnterMain(retained); Cast(retained); Resolve(retained);
        Assert.Contains(retained.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == Member);
        Assert.Throws<ArgumentException>(() => new ArchetypeFilter("synthetic:unknown", original));
    }
}
