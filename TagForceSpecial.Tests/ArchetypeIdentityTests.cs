using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects.Archetypes;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

public sealed class ArchetypeIdentityTests
{
    private const uint Subject = 1, Copy = 2, Change = 3, Down = 4, Destroy = 5, Control = 6,
        Negate = 7, Return = 8, Up = 9, ChangeAgain = 10, FlipBoth = 11, Other = 12;
    private static readonly SourceCardId Source = new("synthetic:1");
    private static readonly ArchetypeId Original = new("synthetic:original"), Child = new("synthetic:child"),
        Parent = new("synthetic:parent"), Permanent = new("synthetic:permanent");
    private static ArchetypeEvidence Evidence => new("synthetic identity mechanics", new string('a', 64), "Synthetic authoring, not a PSP observation.");
    private static ArchetypeIdentityRule Rule(ArchetypeIdentityTreatment treatment, string id = "synthetic:rule") =>
        new(id, Source, treatment, ArchetypeReviewStatus.Reviewed, Evidence);
    private static ArchetypeIdentityRule ConditionalName => Rule(ArchetypeIdentityTreatment.ConditionalName) with {
        Name = "Changed identity", NameArchetypes = [Child],
        Condition = new([CardIdentityLocation.MonsterZone, CardIdentityLocation.Graveyard]) };
    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Subject, 40 - top.Length)];
    private static SourceCardId Id(uint id) => new("synthetic:" + id);
    private static CardInstance Hand(DuelEngine e, uint id, PlayerId p = PlayerId.One) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);
    private static ArchetypePackDocument Pack(params ArchetypeIdentityRule[] rules) => new(
        new(1, new("synthetic:identity"), "1", ArchetypePackKind.Synthetic, [ArchetypeContentProfile.TfsClassic, ArchetypeContentProfile.Expanded], [], Evidence),
        new(1, new[] { Original, Child, Parent, Permanent }.Select(a => new ArchetypeDefinition(a, a.Value, [], [], ArchetypeReviewStatus.Reviewed, Evidence)).ToArray(),
            [new(Child, Parent, ArchetypeReviewStatus.Reviewed, Evidence)]),
        new(1, [new(Source, [Original], ArchetypeReviewStatus.Reviewed, Evidence),
            new(Id(Copy), [Child], ArchetypeReviewStatus.Reviewed, Evidence)]), new(1, rules), new(1, []));
    private static readonly uint[] CardIds = [Subject, Copy, Change, Down, Destroy, Control, Negate, Return, Up, ChangeAgain, FlipBoth, Other];
    private static ArchetypeCardFacts[] Facts => CardIds.Select(id => new ArchetypeCardFacts(Id(id),
        id is Subject or Copy or Other ? CardKind.Monster : CardKind.Spell)).ToArray();
    private static ArchetypeContentSnapshot Snapshot(params ArchetypeIdentityRule[] rules) =>
        ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.Expanded, [Pack(rules)], Facts);
    private static ArchetypeIdentityResolver Resolver(params ArchetypeIdentityRule[] rules) =>
        new(Snapshot(rules), CardIds.ToDictionary(id => id, Id));

    private static DuelEngine Engine(uint[]? first = null, uint[]? second = null, bool timed = true)
    {
        var catalog = new CardCatalog(new(null, "synthetic-effective-identity"), CardIds.Select(id =>
            new CardDefinition(id, "Printed " + id, "", new(1000, 1000, id is Subject or Copy or Other ? 4u : 0u),
                Kind: id is Subject or Copy or Other ? CardKind.Monster : CardKind.Spell)));
        var rules = CardIds.ToDictionary(id => id, id => id is Subject or Copy or Other
            ? new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true)
            : new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true));
        FieldTarget Target(FieldCardFilter filter = FieldCardFilter.FaceUp, TargetSide side = TargetSide.Either) => new(FieldCardKind.Monster, side, filter);
        EffectDefinition Spell(uint id, EffectStep step, FieldCardFilter filter = FieldCardFilter.FaceUp, TargetSide side = TargetSide.Either) =>
            new("synthetic:effect-" + id, id, id == Destroy ? EffectKind.QuickPlaySpell : EffectKind.NormalSpell, [step], fieldTargets: [Target(filter, side)]);
        return new(catalog, rules, first ?? Deck(Subject, Change, Down, Up, Destroy, Negate), second ?? Deck(Subject),
            effectDefinitions: [Spell(Change, new ChangeTargetsNameStep(Copy, timed)), Spell(ChangeAgain, new ChangeTargetsNameStep(Other, timed)),
                Spell(Down, new FlipTargetsFaceDownStep()), Spell(Up, new FlipTargetsFaceUpStep(), FieldCardFilter.FaceDown),
                Spell(Destroy, new DestroyTargetsStep(), FieldCardFilter.Any), Spell(Return, new ReturnTargetsToHandStep(), FieldCardFilter.Any),
                Spell(Control, new TakeControlStep(), side: TargetSide.Opponent),
                new("synthetic:negation", Negate, EffectKind.ContinuousSpell, [], negation: LastingNegation.FaceUpMonsters),
                new("synthetic:flip-both", FlipBoth, EffectKind.NormalSpell, [new FlipTargetsFaceDownStep(), new FlipTargetsFaceUpStep()],
                    fieldTargets: [Target(FieldCardFilter.Any)])]);
    }
    private static List<DuelEvent> Drive(DuelEngine e, CardInstance target)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 60; i++)
        {
            DuelCommand? command = e.State.PendingDecision is { } d
                ? d.FieldTargetOptions?.FirstOrDefault(t => t.Card == target) is { } option
                    ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, option.Player, option.Location, option.Zone, target.InstanceId)
                    : throw new InvalidOperationException("Expected target is unavailable.")
                : e.State.PendingChain is not { } c ? null
                : c.NextLink == 0 ? new PassChainResponse(c.Responder, c.DecisionId)
                : new ResolveNextChainLink(e.State.ActivePlayer, c.DecisionId);
            if (command is null) return events;
            events.AddRange(Ok(e, command).Events);
        }
        throw new InvalidOperationException("Chain did not finish.");
    }
    private static List<DuelEvent> Cast(DuelEngine e, uint spell, CardInstance target)
    {
        int zone = Enumerable.Range(0, 5).First(z => e.State.GetPlayer(e.State.ActivePlayer).SpellTrapFields[z] is null);
        Ok(e, new ActivateSpell(e.State.ActivePlayer, Hand(e, spell, e.State.ActivePlayer).InstanceId, zone));
        return Drive(e, target);
    }
    private static CardInstance Summon(DuelEngine e, PlayerId p = PlayerId.One)
    {
        var card = Hand(e, Subject, p); Ok(e, new NormalSummon(p, card.InstanceId, 0)); return card;
    }

    [Fact]
    public void ConditionalNameUsesRealHandFieldGraveyardStateWithoutMutatingStaticMembership()
    {
        var snapshot = Snapshot(ConditionalName); string fingerprint = snapshot.Fingerprint;
        var resolver = new ArchetypeIdentityResolver(snapshot, CardIds.ToDictionary(id => id, Id));
        var e = Engine(); EnterMain(e); var card = Hand(e, Subject);
        Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
        Summon(e);
        var field = resolver.Resolve(e, card)!;
        Assert.Equal("Changed identity", field.Name); Assert.True(field.IsMember(Parent)); Assert.False(field.IsMember(Original));
        Cast(e, Destroy, card);
        Assert.Equal(CardIdentityLocation.Graveyard, e.GetCardIdentityState(card)!.Location);
        Assert.Equal("Changed identity", resolver.Resolve(e, card)!.Name);
        Assert.True(snapshot.IsMember(Source, Original)); Assert.False(snapshot.IsMember(Source, Parent));
        Assert.Equal(fingerprint, snapshot.Fingerprint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PermanentTreatmentSurvivesFaceDownAndNegationWhileConditionalTreatmentDoesNot(bool permanent)
    {
        var name = permanent ? Rule(ArchetypeIdentityTreatment.PermanentName) with { Name = "Always", NameArchetypes = [Child] } : ConditionalName;
        var extra = Rule(ArchetypeIdentityTreatment.PermanentArchetype, "synthetic:extra") with { Archetype = Permanent };
        var resolver = Resolver(name, extra); var e = Engine(Deck(Subject, Down, Up, Negate, Destroy)); EnterMain(e); var card = Summon(e);
        Cast(e, Down, card);
        Assert.Equal(permanent ? "Always" : "Printed 1", resolver.Resolve(e, card)!.Name);
        Assert.True(resolver.Resolve(e, card)!.IsMember(Permanent));
        Cast(e, Up, card); Cast(e, Negate, card);
        Assert.True(e.GetCardIdentityState(card)!.EffectsNegated);
        Assert.Equal(permanent ? "Always" : "Printed 1", resolver.Resolve(e, card)!.Name);
        Cast(e, Destroy, card);
        Assert.Equal(permanent ? "Always" : "Changed identity", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void ConditionalMembershipRechecksControllerAfterControlChanges()
    {
        var rule = Rule(ArchetypeIdentityTreatment.ConditionalArchetype) with {
            Archetype = Child, Condition = new([CardIdentityLocation.MonsterZone], Controller: PlayerId.One) };
        var resolver = Resolver(rule); var e = Engine(Deck(Control), Deck(Subject)); EnterMain(e); Next(e);
        var card = Summon(e, PlayerId.Two); Assert.False(resolver.Resolve(e, card)!.IsMember(Parent)); Next(e);
        Cast(e, Control, card);
        Assert.Equal(PlayerId.Two, card.Owner); Assert.Equal(PlayerId.One, e.GetCardIdentityState(card)!.Controller);
        Assert.True(resolver.Resolve(e, card)!.IsMember(Parent));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvedNamesExpireByTurnOrRemainUntilThePlacementEnds(bool timed)
    {
        var extra = Rule(ArchetypeIdentityTreatment.PermanentArchetype) with { Archetype = Permanent };
        var resolver = Resolver(extra); var e = Engine(Deck(Subject, Change, Return), timed: timed); EnterMain(e); var card = Summon(e);
        Assert.Single(Cast(e, Change, card).OfType<CardNameChanged>());
        Assert.Equal("Printed 2", resolver.Resolve(e, card)!.Name);
        Assert.True(resolver.Resolve(e, card)!.IsMember(Parent)); Assert.True(resolver.Resolve(e, card)!.IsMember(Permanent));
        Assert.False(resolver.Resolve(e, card)!.IsMember(Original)); Next(e); Next(e);
        Assert.Equal(timed ? "Printed 1" : "Printed 2", resolver.Resolve(e, card)!.Name);
        long placement = e.GetCardIdentityState(card)!.PlacementId;
        Cast(e, Return, card); Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
        Summon(e); Assert.NotEqual(placement, e.GetCardIdentityState(card)!.PlacementId);
        Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void ResolvedNameIsNotUndoneByTargetNegationButFaceDownPermanentlyClearsIt()
    {
        var resolver = Resolver(); var e = Engine(Deck(Subject, Change, Negate, Down, Up), timed: false); EnterMain(e); var card = Summon(e);
        Cast(e, Change, card); Cast(e, Negate, card);
        Assert.Equal("Printed 2", resolver.Resolve(e, card)!.Name);
        Cast(e, Down, card); Cast(e, Up, card);
        Assert.Null(e.GetCardIdentityState(card)!.ChangedNameCardId);
        Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void LatestResolvedNameWinsAndCarriesAcrossControlChanges()
    {
        var resolver = Resolver(); var e = Engine(Deck(Subject, Change, ChangeAgain), Deck(Control), timed: false);
        EnterMain(e); var card = Summon(e); Cast(e, Change, card); Cast(e, ChangeAgain, card);
        Assert.Equal("Printed 12", resolver.Resolve(e, card)!.Name); Assert.Empty(resolver.Resolve(e, card)!.Archetypes);
        Next(e); Cast(e, Control, card);
        Assert.Equal(PlayerId.Two, e.GetCardIdentityState(card)!.Controller);
        Assert.Equal("Printed 12", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void RejectedActivationDoesNotCreateNameChangesOrAlterState()
    {
        var e = Engine(Deck(Change)); EnterMain(e); var before = e.State;
        var result = e.Execute(new ActivateSpell(PlayerId.One, Hand(e, Change).InstanceId, 0));
        Assert.False(result.Succeeded); Assert.Same(before, e.State); Assert.Empty(result.Events);
        Assert.All(e.State.GetPlayer(PlayerId.One).Hand, c => Assert.Null(e.GetCardIdentityState(c)!.ChangedNameCardId));
    }

    [Fact]
    public void UnmappedAndAbsentInstancesDoNotResolveAndMappingsAreCopied()
    {
        var e = Engine(); var card = Hand(e, Subject);
        var map = new Dictionary<uint, SourceCardId> { [Subject] = Source };
        var resolver = new ArchetypeIdentityResolver(Snapshot(), map); map.Clear();
        Assert.NotNull(resolver.Resolve(e, card));
        Assert.Null(resolver.Resolve(e, card with { InstanceId = -1 }));
        Assert.Null(new ArchetypeIdentityResolver(Snapshot(), new Dictionary<uint, SourceCardId>()).Resolve(e, card));
        Assert.Throws<InvalidDataException>(() => new ArchetypeIdentityResolver(Snapshot(), new Dictionary<uint, SourceCardId> { [1] = Source, [2] = Source }));
    }

    [Fact]
    public void ConditionalRulesRoundTripAndDeepFreezeTheirLists()
    {
        CardIdentityLocation[] locations = [CardIdentityLocation.MonsterZone]; ArchetypeId[] groups = [Child];
        var rule = ConditionalName with { Condition = new(locations), NameArchetypes = groups };
        var document = new ArchetypeIdentityRulesDocument(1, [rule]);
        var parsed = ArchetypePackJson.ReadDocument<ArchetypeIdentityRulesDocument>(ArchetypePackJson.WriteDocument(document));
        Assert.Equal(groups, parsed.Rules[0].NameArchetypes);
        var snapshot = Snapshot(rule); string fingerprint = snapshot.Fingerprint;
        locations[0] = CardIdentityLocation.Hand; groups[0] = Original;
        var resolver = new ArchetypeIdentityResolver(snapshot, CardIds.ToDictionary(id => id, Id));
        var e = Engine(); EnterMain(e); var card = Summon(e);
        Assert.True(resolver.Resolve(e, card)!.IsMember(Child)); Assert.Equal(fingerprint, snapshot.Fingerprint);
    }

    [Fact]
    public void OverlappingConflictingNamesFailRatherThanDependingOnPackOrder()
    {
        var conflicting = ConditionalName with { Id = "synthetic:conflict", Name = "Conflicting" };
        var resolver = Resolver(ConditionalName, conflicting); var e = Engine(); EnterMain(e); var card = Summon(e);
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(e, card));
        Cast(e, Change, card);
        Assert.Equal("Printed 2", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void ConditionalRulesRequireReviewedKnownExplicitScopesAndNameMemberships()
    {
        var bad = new[] { ConditionalName with { Condition = null }, ConditionalName with { NameArchetypes = null },
            ConditionalName with { Condition = new([]) }, ConditionalName with { Condition = new([(CardIdentityLocation)999]) },
            ConditionalName with { Condition = new([CardIdentityLocation.Hand], Controller: (PlayerId)999) },
            ConditionalName with { NameArchetypes = [new("synthetic:unknown")] }, ConditionalName with { Review = ArchetypeReviewStatus.Candidate },
            Rule(ArchetypeIdentityTreatment.PermanentName) with { Name = "Name", Condition = new([CardIdentityLocation.Hand]) } };
        foreach (var rule in bad) Assert.Throws<InvalidDataException>(() => Snapshot(rule));
    }

    [Fact]
    public void ConditionalHandMembershipDoesNotLeakToDeckOrFieldAndPermanentNamesNeedNoNameParsing()
    {
        var handOnly = Rule(ArchetypeIdentityTreatment.ConditionalArchetype) with { Archetype = Child, Condition = new([CardIdentityLocation.Hand]) };
        var name = Rule(ArchetypeIdentityTreatment.PermanentName, "synthetic:name") with { Name = "synthetic:parent" };
        var resolver = Resolver(handOnly, name); var e = Engine();
        var card = Hand(e, Subject); var deckCard = e.State.GetPlayer(PlayerId.One).Deck.First(c => c.GameCardId == Subject);
        Assert.True(resolver.Resolve(e, card)!.IsMember(Parent)); Assert.False(resolver.Resolve(e, deckCard)!.IsMember(Parent));
        EnterMain(e); Summon(e);
        Assert.Equal("synthetic:parent", resolver.Resolve(e, card)!.Name);
        Assert.False(resolver.Resolve(e, card)!.IsMember(Parent)); Assert.True(resolver.Resolve(e, card)!.IsMember(Original));
    }

    [Fact]
    public void FaceDownThenFaceUpWithinOneResolvedEffectDoesNotRestoreTheOldName()
    {
        var resolver = Resolver(); var e = Engine(Deck(Subject, Change, FlipBoth), timed: false);
        EnterMain(e); var card = Summon(e); Cast(e, Change, card); long placement = e.GetCardIdentityState(card)!.PlacementId;
        Cast(e, FlipBoth, card);
        Assert.True(e.GetCardIdentityState(card)!.FaceUp); Assert.Equal(placement, e.GetCardIdentityState(card)!.PlacementId);
        Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void NameChangeDoesNotAffectASetTargetAndDestroyingTheTargetEndsIt()
    {
        var resolver = Resolver(); var e = Engine(); EnterMain(e); var card = Summon(e);
        Cast(e, Down, card);
        var before = e.State;
        var rejected = e.Execute(new ActivateSpell(PlayerId.One, Hand(e, Change).InstanceId, 0));
        Assert.False(rejected.Succeeded); Assert.Same(before, e.State); Assert.Empty(rejected.Events);
        Cast(e, Up, card); Cast(e, Change, card); Cast(e, Destroy, card);
        Assert.Null(e.GetCardIdentityState(card)!.ChangedNameCardId); Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
    }

    [Fact]
    public void CopyingANameUsesPermanentNameProjectionWithoutCopyingUnrelatedConditionalEffects()
    {
        var permanent = Rule(ArchetypeIdentityTreatment.PermanentName) with { CardId = Id(Copy), Name = "Permanent target name", NameArchetypes = [Parent] };
        var conditional = ConditionalName with { Id = "synthetic:conditional-target", CardId = Id(Copy), Condition = new([CardIdentityLocation.Deck]), Name = "Deck only" };
        var resolver = Resolver(permanent, conditional); var e = Engine(); EnterMain(e); var card = Summon(e); Cast(e, Change, card);
        Assert.Equal("Permanent target name", resolver.Resolve(e, card)!.Name);
        Assert.True(resolver.Resolve(e, card)!.IsMember(Parent)); Assert.False(resolver.Resolve(e, card)!.IsMember(Child));
    }

    [Fact]
    public void ConflictingPermanentNameMembershipAndUnknownRuntimeNameMappingAreRejected()
    {
        var first = Rule(ArchetypeIdentityTreatment.PermanentName) with { Name = "Same name", NameArchetypes = [Parent] };
        Assert.Throws<InvalidDataException>(() => Snapshot(first, first with { Id = "synthetic:second", NameArchetypes = [Child] }));
        var e = Engine(); EnterMain(e); var card = Summon(e); Cast(e, Change, card);
        var resolver = new ArchetypeIdentityResolver(Snapshot(), new Dictionary<uint, SourceCardId> { [Subject] = Source });
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(e, card));
    }

    [Fact]
    public void ATargetRemovedByAChainResponseNeverReceivesThePendingNameChange()
    {
        var resolver = Resolver(); var e = Engine(Deck(Subject, Change), Deck(Destroy));
        EnterMain(e); var card = Summon(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Destroy, PlayerId.Two).InstanceId, 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Change).InstanceId, 0));
        var decision = e.State.PendingDecision!;
        var target = Assert.Single(decision.FieldTargetOptions!, t => t.Card == card);
        Ok(e, new ChooseDecisionFieldTarget(decision.Player, decision.DecisionId, target.Player, target.Location, target.Zone, card.InstanceId));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = Drive(e, card);
        Assert.DoesNotContain(events, ev => ev is CardNameChanged);
        Assert.Equal(CardIdentityLocation.Graveyard, e.GetCardIdentityState(card)!.Location);
        Assert.Equal("Printed 1", resolver.Resolve(e, card)!.Name);
    }
}
