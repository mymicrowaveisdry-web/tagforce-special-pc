using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Pendulum Effects: what a card does while it is an active scale in a Pendulum Zone.</summary>
public sealed class PendulumEffectTests
{
    private const uint W = 1041, Qli = 11354, Lasting = 1043, Scout = 11353, Cannon = 11211, SupplySquad = 11147, Order = 1044, Seal = 1045;

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        CardCatalog catalog = new(new(null, "synthetic-pendulum-effects"),
            TemplateCardFamily.Family.Cards.Where(c => c.CardId != Qli).Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 5), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Qli, "Qliphort Carrier", "", new(1800, 1000, 4), new(1, MonsterTypes.Machine, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Lasting, "Lasting Scale", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Order, "Spell Negator", "", new(0, 0, 1)))
                .Append(new CardDefinition(Seal, "Spell Seal", "", new(0, 0, 1))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = rules[Qli] = rules[Lasting] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsPendulumSummon: true);
        rules[Order] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        rules[Seal] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        EffectDefinition order = new("test-spell-negation", Order, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells);
        EffectDefinition seal = new("test-spell-seal", Seal, EffectKind.NormalSpell, [new ApplyRuleLockStep(new RuleLock(LockedAction.ActivateSpell, TargetSide.Own))]);
        EffectDefinition lasting = new("test-pendulum-lasting", Lasting, EffectKind.ContinuousSpell, [],
            lastingModifiers: [new LastingStatModifier(TargetSide.Own, 300, 0)], origin: EffectOrigin.PendulumZone);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, lasting, order, seal], shuffleSeed: 9, ruleLocks: TemplateCardFamily.Family.RuleLocks,
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules,
            experimentalPendulumCards: [.. TemplateCardFamily.Family.PendulumCards, new PendulumCardProfile(Lasting, 1, 1)]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;
    private static int Attack(DuelEngine e, PlayerId owner, int zone) =>
        e.State.EventEffects.Statistics.Single(s => s.Monster.Player == owner && s.Monster.Zone == zone).Attack!.Value;

    /// <summary>Answers choices with the first option (or <paramref name="target"/> for trigger targets) and resolves every chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e, Func<FieldCardTarget, bool>? target = null)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingCommitment is { } c
                ? c.ZoneOptions.FirstOrDefault(o => target?.Invoke(o) ?? true) is { } zone
                    ? new ChooseTriggerZoneTarget(c.Player, c.DecisionId, zone.Player, zone.Card.InstanceId) : null
                : e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingChain is not { } chain ? null
                : chain.DefinitionCardChoice is { } pick ? new ChooseDefinitionCard(pick.Player, pick.DecisionId, pick.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    private static void Scale(DuelEngine e, PlayerId player, uint id, int zone)
    {
        Ok(e, new ActivatePendulumScale(player, Hand(e, player, id), zone));
        Drive(e);
    }

    [Fact]
    public void AScaleAppliesItsStatChangeOnlyOnceItsActivationResolvesAndCannotBeUsedAsASpell()
    {
        var e = Engine(Deck(Lasting, W), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Lasting), 0), DuelError.UnsupportedSpellEffect);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0));
        Ok(e, new ActivatePendulumScale(PlayerId.One, Hand(e, PlayerId.One, Lasting), 0));
        Assert.Equal(1500, Attack(e, PlayerId.One, 0));
        Drive(e);
        Assert.Equal(1800, Attack(e, PlayerId.One, 0));
    }

    [Fact]
    public void AScaleLockLimitsPendulumSummonsToItsArchetype()
    {
        var e = Engine(Deck(Scout, Cannon, W, Qli), Deck());
        EnterMain(e);
        Scale(e, PlayerId.One, Scout, 0);
        Scale(e, PlayerId.One, Cannon, 1);
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [new(Hand(e, PlayerId.One, W), DecisionCardSource.Hand, 0)]), DuelError.LockedByEffect);
        Ok(e, new PendulumSummon(PlayerId.One, e.State.Revision, [new(Hand(e, PlayerId.One, Qli), DecisionCardSource.Hand, 0)]));
    }

    [Fact]
    public void AnActivatedPendulumEffectSearchesOncePerTurn()
    {
        var e = Engine(Deck(Scout, W, W, W, W, Qli), Deck());
        EnterMain(e);
        Scale(e, PlayerId.One, Scout, 0);
        Ok(e, new ActivatePendulumEffect(PlayerId.One, 0));
        var events = Drive(e);
        Assert.Equal(Qli, events.OfType<CardSearched>().Single().Card.GameCardId);
        Assert.Equal(7200, P(e, PlayerId.One).LifePoints);
        Reject(e, new ActivatePendulumEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void AnEndPhaseScaleTriggerNeedsTheScaleActivatedThisTurn()
    {
        var e = Engine(Deck(Cannon), Deck(SupplySquad));
        EnterMain(e); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, SupplySquad), 0)); Drive(e); Next(e);
        Scale(e, PlayerId.One, Cannon, 0);
        Phase(e, DuelPhase.End);
        Drive(e, t => t.Player == PlayerId.Two);
        Assert.All(P(e, PlayerId.Two).SpellTrapFields, Assert.Null);

        var late = Engine(Deck(Cannon), Deck(SupplySquad));
        EnterMain(late); Scale(late, PlayerId.One, Cannon, 0);
        Phase(late, DuelPhase.End);
        var offered = late.State.EventEffects.PendingTriggers!;
        Ok(late, new ChooseTriggerOrder(offered.Player, offered.DecisionId, []));
        Ok(late, new EndTurn(PlayerId.One)); EnterMain(late);
        Ok(late, new ActivateSpell(PlayerId.Two, Hand(late, PlayerId.Two, SupplySquad), 0)); Drive(late);
        Phase(late, DuelPhase.End);
        Assert.Null(late.State.EventEffects.PendingTriggers);
        Assert.NotNull(P(late, PlayerId.Two).SpellTrapFields.Single(f => f is not null));
    }

    [Fact]
    public void SpellNegationStopsAnActivatedPendulumEffectButTheScaleStays()
    {
        var e = Engine(Deck(Scout, Order, W, W, W, W, Qli), Deck());
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Order), 0));
        Scale(e, PlayerId.One, Scout, 0);
        Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e);
        Ok(e, new ActivatePendulumEffect(PlayerId.One, 0));
        var events = Drive(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.SourceEffectsNegated });
        Assert.Empty(events.OfType<CardSearched>());
        Assert.NotNull(P(e, PlayerId.One).PendulumZones[0]);
    }

    [Fact]
    public void PlacingAScaleIsASpellActivationThatSpellLocksStop()
    {
        var e = Engine(Deck(Seal, Scout), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Seal), 0)); Drive(e);
        Reject(e, new ActivatePendulumScale(PlayerId.One, Hand(e, PlayerId.One, Scout), 0), DuelError.LockedByEffect);
    }
}
