using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Fusion and Ritual effect-summon tests with explicit recipes.</summary>
public sealed class EffectSummonTests
{
    private const uint Low = 100_100, High = 100_101, Fusion = 100_102, Ritual = 100_103,
        FusionSpell = 100_104, RitualSpell = 100_105, Response = 100_106, Filler = 100_107;

    /// <summary>A null <paramref name="response"/> makes the Response card a targeted destruction Trap definition.</summary>
    private static DuelEngine Make(bool ritual = false, ResponseNegation? response = ResponseNegation.Effect,
        bool discard = false, bool triggers = false, bool facedown = true, bool duplicates = false,
        uint ritualLevel = 5)
    {
        uint[] ids = [Low, High, Fusion, Ritual, FusionSpell, RitualSpell, Response, Filler];
        var catalog = new CardCatalog(new(null, "synthetic-effect-summons"), ids.Select(id => new CardDefinition(id, $"{id}", "",
            new(1000, 1000, id == Low ? 2u : id == High ? 3u : id == Fusion ? 0u : id == Ritual ? 5u : 1u))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is FusionSpell or RitualSpell ? CardCategory.Spell : id == Response ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: id is Low or High or Filler, AllowsBasicBattle: id is Low or High or Fusion or Ritual or Filler,
            AllowsSpellTrapSet: id == Response, AllowsTributeForSummon: id is Low or High or Ritual,
            AllowsExtraDeck: id == Fusion, AllowsSummonMaterial: id is Low or High or Ritual));
        uint spell = ritual ? RitualSpell : FusionSpell;
        uint[] first = [Low, duplicates ? Low : High, spell, Ritual, Response, .. Enumerable.Repeat(Filler, 35)];
        uint[] second = [Response, Low, High, Filler, Filler, .. Enumerable.Repeat(Filler, 35)];
        TriggerEffectProfile[] effects = triggers ? [
            Gain("low-grave", Low, MonsterEventKind.SentToGraveyard, location: TriggerSourceLocation.GraveyardSelf),
            Gain("high-grave", High, MonsterEventKind.SentToGraveyard, location: TriggerSourceLocation.GraveyardSelf),
            Gain("ritual-summon", Ritual, MonsterEventKind.SpecialSummoned, gain: 500),
            Gain("fusion-summon", Fusion, MonsterEventKind.SpecialSummoned, gain: 500),
            Gain("ritual-grave-watcher", Ritual, MonsterEventKind.SentToGraveyard, EffectRelation.Controller, gain: 2000),
            Gain("fusion-grave-watcher", Fusion, MonsterEventKind.SentToGraveyard, EffectRelation.Controller, gain: 2000)] : [];
        var profile = ritual
            ? new SummonEffectProfile("ritual-effect", RitualSpell, "ritual-proper",
                ritualRecipes: [new(Ritual, ritualLevel, [Low, High, Ritual])], allowFaceDownFieldMaterials: facedown)
            : new SummonEffectProfile("fusion-effect", FusionSpell, "fusion-proper",
                fusionRecipes: [new(Fusion, [Low, duplicates ? Low : High])], allowFaceDownFieldMaterials: facedown);
        return new(catalog, rules, first, second, playerOneExtraDeck: [Fusion],
            experimentalSpecialSummonCards: [new(Fusion, SpecialSummonSources.Graveyard, "fusion-proper"), new(Ritual, SpecialSummonSources.Graveyard, "ritual-proper")],
            experimentalSpecialSummonProcedures: [new("revive", [Fusion, Ritual], DecisionCardSource.Graveyard)],
            experimentalSummonWindows: [new(Fusion), new(Ritual)], experimentalTriggerEffects: effects,
            effectDefinitions: response is { } negation ? [ResponseProbe.Negate("response", Response, negation, ChainSpellSpeed.Two, discard ? [High] : null)]
                : [new("response", Response, EffectKind.NormalTrap, [new DestroyTargetStep()],
                    cost: discard ? new DiscardCost() : null, target: new SpellTrapOnFieldTarget())],
            experimentalSummonEffects: [profile]);
    }

    private static void Activate(DuelEngine e, bool ritual = false)
    {
        var spell = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == (ritual ? RitualSpell : FusionSpell));
        Ok(e, new ActivateSpell(PlayerId.One, spell.InstanceId, 0));
    }

    private static PendingEffectSummon Request(DuelEngine e)
    {
        Pass(e); Pass(e); var result = Resolve(e);
        Assert.Single(result.Events.OfType<EffectSummonRequested>());
        Assert.Empty(result.Events.OfType<ChainLinkResolved>());
        return e.State.PendingChain!.SummonDecision!;
    }

    private static ChooseEffectSummon Selection(PendingEffectSummon pending, int zone = 0) =>
        new(pending.Player, pending.DecisionId, pending.Targets[0].InstanceId,
            pending.Materials.Where(m => m.Card.GameCardId is Low or High).Select(m => m.Card.InstanceId), zone);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MixedHandAndFacedownFieldMaterialsResolveAtomicallyThroughChainAndQualifyProperSummon(bool ritual)
    {
        var e = Make(ritual, triggers: true); EnterMain(e); Summon(e, Low, set: true);
        Activate(e, ritual);
        Assert.Null(e.State.PendingChain!.SummonDecision);
        var pending = Request(e);
        var old = e.State; var json = JsonSerializer.Serialize(old);
        Assert.NotNull(pending.Materials.Single(m => m.Card.GameCardId == Low).Field);
        Assert.Null(pending.Materials.Single(m => m.Card.GameCardId == High).Field);
        var result = Ok(e, Selection(pending));
        Assert.Equal(json, JsonSerializer.Serialize(old));
        var player = e.State.GetPlayer(PlayerId.One); var host = player.MonsterZones[0]!;
        Assert.Equal(ritual ? Ritual : Fusion, host.Card.GameCardId);
        Assert.Equal(3, player.Graveyard.Count); // two materials plus the resolving Spell
        Assert.Null(e.State.PendingSummon); Assert.Null(e.State.PendingChain);
        Assert.Empty(result.Events.OfType<MonsterDestroyed>());
        Assert.Empty(result.Events.OfType<CardDiscarded>());
        Assert.Empty(result.Events.OfType<ActivationCostPaid>());
        Assert.Equal(2, ritual ? result.Events.OfType<RitualMaterialTributed>().Count() : result.Events.OfType<FusionMaterialSentToGraveyard>().Count());
        Assert.True(e.State.NormalSummonUsed);
        var history = e.State.SummonHistory.Single(h => h.Card == host.Card);
        Assert.Equal(SummonMethod.SpecialEffect, history.Method);
        Assert.Equal(ritual ? "ritual-proper" : "fusion-proper", history.ProperProcedureId);
        Assert.Equal(result.Events.Count - 1, result.Events.ToList().FindIndex(x => x is TriggerOrderRequested));
        // The new host was not a field source when either hand or field materials went to the Graveyard.
        Assert.DoesNotContain(e.State.EventEffects.PendingTriggers!.Options, t => t.Profile.EffectId.EndsWith("grave-watcher", StringComparison.Ordinal));
        Drain(e); Assert.Equal(8700, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.False(e.State.ResearchSliceComplete);
    }

    [Theory]
    [InlineData(false, ResponseNegation.Activation)] [InlineData(false, ResponseNegation.Effect)]
    [InlineData(true, ResponseNegation.Activation)] [InlineData(true, ResponseNegation.Effect)]
    public void NegationNeverRequestsOrSpendsResolutionMaterials(bool ritual, ResponseNegation operation)
    {
        var e = Make(ritual, operation); EnterMain(e); Summon(e, Low);
        NextMain(e); Set(e, Response); NextMain(e); Activate(e, ritual);
        var before = e.State.GetPlayer(PlayerId.One); var low = before.MonsterZones[0]; var hand = before.Hand.ToArray();
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = Drain(e);
        Assert.Empty(events.OfType<EffectSummonRequested>());
        Assert.Empty(events.OfType<FusionMaterialSentToGraveyard>());
        Assert.Empty(events.OfType<RitualMaterialTributed>());
        Assert.Equal(low, e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Equal(hand, e.State.GetPlayer(PlayerId.One).Hand);
        Assert.Single(e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Null(e.State.PendingChain);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidAndStaleResolutionAnswersAreAtomicAndNoFreeSummonExists(bool ritual)
    {
        var e = Make(ritual); EnterMain(e);
        Reject(e, new ChooseEffectSummon(PlayerId.One, e.State.Revision, 1, [], 0), DuelError.NoPendingChain);
        Activate(e, ritual);
        Reject(e, new ChooseEffectSummon(PlayerId.One, e.State.Revision, 1, [], 0), DuelError.NoPendingDecision);
        var p = Request(e); var valid = Selection(p);
        Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId - 1, valid.TargetInstanceId, valid.MaterialInstanceIds, 0), DuelError.StaleDecision);
        Reject(e, new ChooseEffectSummon(PlayerId.Two, p.DecisionId, valid.TargetInstanceId, valid.MaterialInstanceIds, 0), DuelError.NotActivePlayer);
        Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId, int.MaxValue, valid.MaterialInstanceIds, 0), DuelError.InvalidSummonMaterials);
        Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId, valid.TargetInstanceId, [valid.MaterialInstanceIds[0], valid.MaterialInstanceIds[0]], 0), DuelError.InvalidSummonMaterials);
        Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId, valid.TargetInstanceId, [valid.MaterialInstanceIds[0]], 0), DuelError.InvalidSummonMaterials);
        Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId, valid.TargetInstanceId, valid.MaterialInstanceIds, 5), DuelError.InvalidMonsterZone);
        Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId, valid.TargetInstanceId, valid.MaterialInstanceIds, 0, BattlePosition.FaceDownDefense), DuelError.InvalidBattlePosition);
        if (ritual) Reject(e, new ChooseEffectSummon(PlayerId.One, p.DecisionId, valid.TargetInstanceId, [valid.TargetInstanceId], 0), DuelError.InvalidSummonMaterials);
        Ok(e, valid);
        Reject(e, valid, DuelError.NoPendingChain);
    }

    [Fact]
    public void DestroyingTheSourceSpellDoesNotNegateTheSummonEffect()
    {
        var e = Make(response: null); EnterMain(e); Summon(e, Low);
        NextMain(e); Set(e, Response); NextMain(e); Activate(e);
        var source = e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]!.Card;
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var target = e.State.PendingDecision!.TargetOptions.Single(t => t.Card == source);
        Ok(e, new ChooseDecisionTarget(PlayerId.Two, e.State.PendingDecision!.DecisionId, target.Player, target.Zone, source.InstanceId));
        Pass(e); Pass(e); Resolve(e);
        Assert.Null(e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]);
        Assert.Single(e.State.GetPlayer(PlayerId.One).Graveyard);
        Resolve(e); var pending = e.State.PendingChain!.SummonDecision!;
        var result = Ok(e, Selection(pending));
        Assert.Equal(Fusion, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.Empty(result.Events.OfType<SpellSentToGraveyard>());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AResponseDiscardingTheOnlyRequiredHandMaterialMakesResolutionFizzleAndComplete(bool ritual)
    {
        var e = Make(ritual, null, discard: true); EnterMain(e);
        Summon(e, Low); Set(e, Response, 1); NextMain(e); NextMain(e); Activate(e, ritual);
        var source = e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]!.Card;
        Pass(e); // The controller may respond after the opponent passes.
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 1));
        var cost = e.State.PendingDecision!.CardOptions.Single(c => c.GameCardId == High);
        Ok(e, new ChooseDecisionCard(PlayerId.One, e.State.PendingDecision!.DecisionId, cost.InstanceId));
        var target = e.State.PendingDecision!.TargetOptions.Single(t => t.Card == source);
        Ok(e, new ChooseDecisionTarget(PlayerId.One, e.State.PendingDecision!.DecisionId, target.Player, target.Zone, source.InstanceId));
        Pass(e); Pass(e); Resolve(e); var result = Resolve(e);
        Assert.Equal(ChainSkipReason.SummonUnavailable, Assert.Single(result.Events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Single(result.Events.OfType<ChainCompleted>());
        Assert.Empty(result.Events.OfType<EffectSummonRequested>());
        Assert.Empty(result.Events.OfType<FusionMaterialSentToGraveyard>());
        Assert.Empty(result.Events.OfType<RitualMaterialTributed>());
        Assert.Null(e.State.PendingChain);
        Assert.Equal(Low, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
        Assert.False(e.State.ResearchSliceComplete);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MaterialsFreeAZoneOnAFullFieldAndUnselectedOccupiedZonesReject(bool ritual)
    {
        var e = Make(ritual); EnterMain(e); Summon(e, Low);
        NextMain(e); NextMain(e); Summon(e, High, 1);
        for (int zone = 2; zone < 5; zone++) { NextMain(e); NextMain(e); Summon(e, Filler, zone); }
        Activate(e, ritual); var pending = Request(e); var selection = Selection(pending);
        Reject(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, selection.TargetInstanceId,
            selection.MaterialInstanceIds, 4), DuelError.MonsterZoneOccupied);
        Ok(e, selection);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[1]);
        Assert.All(e.State.GetPlayer(PlayerId.One).MonsterZones.Skip(2), m => Assert.Equal(Filler, m!.Card.GameCardId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ProperEffectSummonRemainsEligibleForRevivalAfterBattleDeparture(bool ritual)
    {
        var e = Make(ritual); EnterMain(e); Activate(e, ritual); Ok(e, Selection(Request(e)));
        var host = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card;
        NextMain(e); Summon(e, Low); Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Contains(host, e.State.GetPlayer(PlayerId.One).Graveyard);
        NextMain(e); Ok(e, new SpecialSummonByProcedure(PlayerId.One, e.State.Revision, "revive", host.InstanceId, 0));
        for (int pass = 0; pass < 2; pass++)
        {
            var pending = e.State.PendingSummon!;
            Ok(e, new PassSummonResponse(pending.Responder, pending.DecisionId));
        }
        Assert.Equal(host, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card);
        Assert.Equal(ritual ? "ritual-proper" : "fusion-proper", e.State.SummonHistory.Single(h => h.Card == host).ProperProcedureId);
    }

    [Fact]
    public void ExactMultiplicityIsAuthoredAndProfileCollectionsAreImmutable()
    {
        uint[] recipeIds = [Low, Low]; var recipe = new FusionSummonRecipe(Fusion, recipeIds); recipeIds[0] = High;
        Assert.Equal(new uint[] { Low, Low }, recipe.MaterialCardIds);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)recipe.MaterialCardIds).Clear());
        var e = Make(duplicates: true); EnterMain(e); Activate(e); var p = Request(e);
        Assert.Equal(2, p.Materials.Count); Assert.All(p.Materials, m => Assert.Equal(Low, m.Card.GameCardId));
        Ok(e, Selection(p)); Assert.Equal(Fusion, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Throws<ArgumentException>(() => Make(ritual: true, ritualLevel: 4));
        Assert.Throws<ArgumentException>(() => new SummonEffectProfile("bad", FusionSpell, "proper"));
        Assert.Throws<ArgumentException>(() => new RitualSummonRecipe(Ritual, 0, [Low]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitFaceDownExclusionAndMissingMaterialsFailActivationWithoutMovingTheSpell(bool ritual)
    {
        var e = Make(ritual, facedown: false); EnterMain(e); Summon(e, Low, set: true);
        var spell = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == (ritual ? RitualSpell : FusionSpell));
        Reject(e, new ActivateSpell(PlayerId.One, spell.InstanceId, 0), DuelError.DecisionUnavailable);
    }
}
