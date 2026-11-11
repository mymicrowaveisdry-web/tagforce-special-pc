using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Changed Types, Attributes and Pendulum Scales, and locks on a declared card name.</summary>
public sealed class IdentityChangeTests
{
    private const uint W = 1081, DarkWipe = 1082, HighScale = 1083, RockDark = 1084,
        DnaSurgery = 4951, Virus = 5660, GenexChanger = 8551, PsiBlocker = 9283, PendulumShift = 11585, Cannon = 11211;

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        CardCatalog catalog = new(new(null, "synthetic-identity-changes"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(MonsterAttributes.Light, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(MonsterAttributes.Light, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(HighScale, "High Scale", "", new(1000, 1000, 4), new(MonsterAttributes.Light, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(RockDark, "Rock Dark", "", new(1000, 1000, 4), new(MonsterAttributes.Dark, MonsterTypes.Rock, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(DarkWipe, "Dark Wipe", "", new(0, 0, 1))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = rules[HighScale] = rules[RockDark] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsPendulumSummon: true);
        rules[DarkWipe] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions,
                new("test-dark-wipe", DarkWipe, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster, Cards: new MonsterAttributeFilter(MonsterAttributes.Dark))])],
            shuffleSeed: 17, ruleLocks: TemplateCardFamily.Family.RuleLocks, battleAbilities: TemplateCardFamily.Family.BattleAbilities,
            destructionReplacements: TemplateCardFamily.Family.DestructionReplacements, graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects,
            damageRules: TemplateCardFamily.Family.DamageRules,
            experimentalPendulumCards: [.. TemplateCardFamily.Family.PendulumCards, new PendulumCardProfile(HighScale, 8, 8)]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;

    /// <summary>Declares <paramref name="value"/>, answers other choices with their first option and resolves every chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e, int value = 0, Func<FieldCardTarget, bool>? pick = null)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingDecision is { FieldTargetOptions: { Count: > 0 } options } d && options.FirstOrDefault(o => pick?.Invoke(o) ?? true) is { } target
                    ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId)
                : e.State.PendingDecision is { } card ? new ChooseDecisionCard(card.Player, card.DecisionId, card.CardOptions[0].InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.Declaration is { } declaration ? new DeclareValue(declaration.Player, declaration.DecisionId, value)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    [Fact]
    public void ALastingTypeChangeIsWhatTypeChecksSee()
    {
        var e = Engine(Deck(DnaSurgery, Virus, W), Deck(W));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, DnaSurgery), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e, (int)MonsterTypes.Rock);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Virus), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Drive(e, (int)MonsterTypes.Rock);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void AnAttributeChangeOnATargetLastsUntilTheEndOfTheTurn()
    {
        var e = Engine(Deck(GenexChanger, DarkWipe), Deck(W, DarkWipe));
        EnterMain(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, GenexChanger), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        var changed = Drive(e, (int)MonsterAttributes.Dark, t => t.Player == PlayerId.Two).OfType<TraitChanged>().Single();
        Assert.Equal((IdentityTrait.Attribute, (uint)MonsterAttributes.Dark), (changed.Trait, changed.Value));
        Next(e);
        // Back to LIGHT: with no DARK monster on the field, the wipe has nothing to destroy and cannot be activated.
        Reject(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, DarkWipe), 0), DuelError.DecisionUnavailable);

        var now = Engine(Deck(GenexChanger, DarkWipe), Deck(W));
        EnterMain(now); Next(now);
        Ok(now, new NormalSummon(PlayerId.Two, Hand(now, PlayerId.Two, W), 0)); Next(now);
        Ok(now, new NormalSummon(PlayerId.One, Hand(now, PlayerId.One, GenexChanger), 0));
        Ok(now, new ActivateMonsterEffect(PlayerId.One, 0)); Drive(now, (int)MonsterAttributes.Dark, t => t.Player == PlayerId.Two);
        Ok(now, new ActivateSpell(PlayerId.One, Hand(now, PlayerId.One, DarkWipe), 1)); Drive(now);
        Assert.Null(P(now, PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void ADeclaredNameCannotBeUsedUntilTheEndOfTheOpponentsNextTurn()
    {
        var e = Engine(Deck(PsiBlocker, W, W), Deck(W));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, PsiBlocker), 0)); Next(e); Next(e);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Drive(e, (int)W);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 1), DuelError.LockedByEffect);
        Next(e);
        Reject(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0), DuelError.LockedByEffect);
        Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 1));
    }

    [Fact]
    public void AChangedScaleNarrowsWhatCanBePendulumSummoned()
    {
        var open = Engine(Deck(Cannon, HighScale, W), Deck());
        EnterMain(open);
        foreach (var (id, zone) in new[] { (Cannon, 0), (HighScale, 1) })
        {
            Ok(open, new ActivatePendulumScale(PlayerId.One, Hand(open, PlayerId.One, id), zone)); Drive(open);
        }
        Ok(open, new PendulumSummon(PlayerId.One, open.State.Revision, [new(Hand(open, PlayerId.One, W), DecisionCardSource.Hand, 0)]));

        var e = Engine(Deck(Cannon, HighScale, PendulumShift, W), Deck());
        EnterMain(e);
        foreach (var (id, zone) in new[] { (Cannon, 0), (HighScale, 1) })
        {
            Ok(e, new ActivatePendulumScale(PlayerId.One, Hand(e, PlayerId.One, id), zone)); Drive(e);
        }
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, PendulumShift), 0));
        Assert.Single(Drive(e, 5, t => t.Card.GameCardId == Cannon).OfType<ScaleChanged>());
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [new(Hand(e, PlayerId.One, W), DecisionCardSource.Hand, 0)]), DuelError.UnsupportedMonsterLevel);
    }
}
