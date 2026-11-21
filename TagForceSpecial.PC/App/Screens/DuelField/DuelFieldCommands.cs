using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>What one menu row does: submit a legal choice, pick an attack target, pick among several choices (PC list), show the card's
/// details, or report a command the PC does not support.</summary>
public abstract record MenuCommand;
public sealed record SubmitCommand(int ChoiceId) : MenuCommand;
public sealed record TargetCommand(IReadOnlyList<ChoiceView> Choices) : MenuCommand;
public sealed record ListCommand(IReadOnlyList<ChoiceView> Choices) : MenuCommand;
public sealed record DetailsCommand(CardView Card) : MenuCommand;
public sealed record UnsupportedCommand(string Notice) : MenuCommand;
/// <summary>One menu row: its label (the original string when <see cref="Verb"/> has one), what it does, and its verb.</summary>
public sealed record MenuItem(string Label, MenuCommand Command, MenuVerb Verb = MenuVerb.Other);

/// <summary>Command verbs with an original string in duel/basic_jj.ehp. Icons are the card_icon02 cells seen in captures.</summary>
public enum MenuVerb
{
    Summon, SpecialSummon, PendulumSummon, FlipSummon, SetMonster, SetSpellTrap, Activate, PendulumScale, Attack, ToAttack, ToDefense,
    ShowCardList, Surrender, ShowDetails, ChangePhase, Option, Help, Other
}

public static class MenuVerbs
{
    /// <summary>String offsets in duel/basic_jj.ehp (UTF-16, zero-terminated).</summary>
    public static int? TextOffset(MenuVerb verb) => verb switch
    {
        MenuVerb.Summon => 401574, MenuVerb.SpecialSummon => 401620, MenuVerb.PendulumSummon => 401588, MenuVerb.FlipSummon => 401288,
        MenuVerb.SetMonster or MenuVerb.SetSpellTrap => 401354, MenuVerb.Activate => 400808, MenuVerb.PendulumScale => 400826,
        MenuVerb.Attack => 400864, MenuVerb.ToAttack => 400878, MenuVerb.ToDefense => 400924, MenuVerb.ShowCardList => 401472,
        MenuVerb.Surrender => 401642, MenuVerb.ShowDetails => 401502, MenuVerb.ChangePhase => 401262, MenuVerb.Option => 401104,
        MenuVerb.Help => 401088, _ => null
    };

    /// <summary>card_icon02 cell (13 × 13) of each verb seen in a capture; verbs not captured yet have none.</summary>
    public static (int U, int V)? Icon(MenuVerb verb) => verb switch
    {
        MenuVerb.Summon => (65, 66), MenuVerb.SetMonster => (104, 92), MenuVerb.SetSpellTrap => (52, 66), MenuVerb.Attack => (91, 66),
        MenuVerb.ShowCardList => (65, 79), MenuVerb.Surrender => (0, 79), MenuVerb.ShowDetails => (0, 66), MenuVerb.ChangePhase => (91, 92),
        MenuVerb.Option => (78, 92), MenuVerb.Help => (52, 79), _ => null
    };
}

/// <summary>How the current legal choices are presented: the open field (cursor and menus), a Chain-style Yes/No response prompt, an
/// automatic Draw, or a PC decision list for decisions whose original screens were not measured.</summary>
public enum DuelInputMode { Field, Response, AutoDraw, DecisionList, Waiting }

/// <summary>Builds menus from the authorized legal choices. Wording observed in the original: Summon, Set, Attack, Show Details, Show Card
/// List, Surrender, Change Phase, Option, Help; "Activate" and "Change Position" are PC wording until their menus are captured.</summary>
public static class DuelFieldCommands
{
    public const string ShowDetails = "Show Details";
    /// <summary>Inferred: the one captured summon to an empty row went to the centre slot; other zones are tried centre-out.</summary>
    public static readonly int[] PreferredZones = [2, 1, 3, 0, 4];

    private static readonly ActionKind[] ResponseKinds =
        [ActionKind.ChainResponse, ActionKind.ChainPass, ActionKind.BattleResponse, ActionKind.BattlePass];

    public static DuelInputMode Mode(DuelView view)
    {
        var kinds = view.Choices.Select(c => c.Kind).ToArray();
        if (kinds.Length == 0) return DuelInputMode.Waiting;
        if (kinds.Any(k => k is ActionKind.Phase or ActionKind.EndTurn)) return DuelInputMode.Field;
        if (kinds.All(k => k == ActionKind.Draw)) return DuelInputMode.AutoDraw;
        if (kinds.Any(k => k is ActionKind.ChainPass or ActionKind.BattlePass) &&
            view.Choices.All(c => c.Kind is { } k && (ResponseKinds.Contains(k) || c.FocusInstanceId is not null)))
            return DuelInputMode.Response;
        return DuelInputMode.DecisionList;
    }

    /// <summary>The menu ○ opens on a focused object: its legal commands grouped by verb, then Show Details for a visible card.</summary>
    public static IReadOnlyList<MenuItem> ObjectMenu(DuelView view, FieldObject target)
    {
        var items = new List<MenuItem>();
        if (target.Own && target.Kind == FieldObjectKind.Deck)
            return [new("Surrender", new UnsupportedCommand("Surrender is not available in the PC duel yet."), MenuVerb.Surrender)];
        // Measured: Show Card List on the own Extra Deck opens the card list. Graveyards and Banished piles use the same list (inferred).
        var owner = target.Own ? view.Self : view.Opponent;
        if (target.Kind == FieldObjectKind.Extra && target.Own && view.Self.ExtraDeckCount + view.Self.FaceUpExtraDeck.Count > 0)
            return [new("Show Card List", new CardListCommand(true, CardListPile.ExtraDeck), MenuVerb.ShowCardList)];
        if (target.Kind == FieldObjectKind.Graveyard && owner.Graveyard.Count > 0)
            return [new("Show Card List", new CardListCommand(target.Own, CardListPile.Graveyard), MenuVerb.ShowCardList)];
        if (target.Kind == FieldObjectKind.Banished && owner.BanishedCount > 0)
            return [new("Show Card List", new CardListCommand(target.Own, CardListPile.Banished), MenuVerb.ShowCardList)];
        if (target.Card is not { Hidden: false } card) return items;
        var own = view.Choices.Where(c => c.FocusInstanceId is { } id && id == card.InstanceId && c.Kind is not null && !ResponseKinds.Contains(c.Kind.Value));
        foreach (var group in own.GroupBy(c => Verb(c, card)).OrderBy(g => g.Key))
            items.Add(new(Label(group.Key, group.First()), Command(group.Key, group.ToArray()), group.Key));
        items.Add(new(ShowDetails, new DetailsCommand(card), MenuVerb.ShowDetails));
        return items;
    }

    /// <summary>The original verb for a legal action; the engine's own label decides between summon kinds.</summary>
    public static MenuVerb Verb(ChoiceView choice, CardView card) => choice.Kind switch
    {
        ActionKind.Summon when choice.Label.StartsWith("Pendulum Summon", StringComparison.Ordinal) => MenuVerb.PendulumSummon,
        ActionKind.Summon when choice.Label.StartsWith("Special Summon", StringComparison.Ordinal) => MenuVerb.SpecialSummon,
        ActionKind.Summon => MenuVerb.Summon,
        ActionKind.SetMonster => MenuVerb.SetMonster,
        ActionKind.SetSpellTrap => MenuVerb.SetSpellTrap,
        ActionKind.Attack => MenuVerb.Attack,
        ActionKind.Position when card.Position == BattlePosition.FaceDownDefense && choice.Position == BattlePosition.FaceUpAttack => MenuVerb.FlipSummon,
        ActionKind.Position when choice.Position == BattlePosition.FaceUpAttack => MenuVerb.ToAttack,
        ActionKind.Position => MenuVerb.ToDefense,
        ActionKind.ActivateSpell when choice.Label.StartsWith("Scale ", StringComparison.Ordinal) => MenuVerb.PendulumScale,
        ActionKind.ActivateSpell or ActionKind.ActivateSetSpell or ActionKind.MonsterEffect or ActionKind.CardEffect => MenuVerb.Activate,
        _ => MenuVerb.Other
    };

    private static string Label(MenuVerb verb, ChoiceView choice) => verb switch
    {
        MenuVerb.Summon => "Summon", MenuVerb.SpecialSummon => "Sp. Summon", MenuVerb.PendulumSummon => "Pendulum Summon",
        MenuVerb.FlipSummon => "Flip Summon", MenuVerb.SetMonster or MenuVerb.SetSpellTrap => "Set", MenuVerb.Activate => "Activate",
        MenuVerb.PendulumScale => "Set Pendulum Scale", MenuVerb.Attack => "Attack", MenuVerb.ToAttack => "Change to ATK Position",
        MenuVerb.ToDefense => "Change to DEF Position", _ => choice.Label
    };

    private static MenuCommand Command(MenuVerb verb, ChoiceView[] choices)
    {
        // Measured: one monster target still opens the target state; a lone direct attack submits (its screen was not captured).
        if (verb == MenuVerb.Attack && choices.Any(c => AttackTarget(c) is not null)) return new TargetCommand(choices);
        // Measured: a Tribute Summon opens the Tribute prompt and selection even when only one set of Tributes is possible.
        if (verb == MenuVerb.Summon && TributeSelection.Applies(choices)) return new TributeCommand(choices);
        if (choices.Length == 1) return new SubmitCommand(choices[0].Id);
        // Several free zones for the same summon or set: the original places the card itself.
        if (verb is MenuVerb.Summon or MenuVerb.SetMonster or MenuVerb.SetSpellTrap && choices.Select(c => c.Kind).Distinct().Count() == 1 &&
            choices.Select(c => StripZone(c.Label)).Distinct().Count() == 1 && choices.All(c => c is { Zone: not null, TargetZone: null, Position: null }))
            return new SubmitCommand(choices.OrderBy(c => Array.IndexOf(PreferredZones, c.Zone!.Value) is var i && i < 0 ? 99 : i).First().Id);
        return new ListCommand(choices);
    }

    private static string StripZone(string label) => label.Contains(" [", StringComparison.Ordinal) ? label[..label.LastIndexOf(" [", StringComparison.Ordinal)] : label;

    /// <summary>Global menu (△): Show Details, Change Phase, Option, Help (measured order).</summary>
    public static IReadOnlyList<string> GlobalMenu { get; } = [ShowDetails, "Change Phase", "Option", "Help"];
    public static IReadOnlyList<MenuVerb> GlobalVerbs { get; } = [MenuVerb.ShowDetails, MenuVerb.ChangePhase, MenuVerb.Option, MenuVerb.Help];

    /// <summary>Phase selector options: the current phase and each reachable phase, in phase order (End Phase via End Turn when no explicit
    /// End Phase choice exists). Each maps to the choice that enters it; the current phase has none.</summary>
    public static IReadOnlyList<(DuelPhase Phase, int? ChoiceId)> PhaseOptions(DuelView view)
    {
        var options = new Dictionary<DuelPhase, int?> { [view.Phase] = null };
        foreach (var c in view.Choices)
        {
            if (c.Kind == ActionKind.Phase && c.Phase is { } phase) options[phase] = c.Id;
            else if (c.Kind == ActionKind.EndTurn && !options.ContainsKey(DuelPhase.End)) options[DuelPhase.End] = c.Id;
        }
        return options.OrderBy(o => o.Key).Select(o => (o.Key, o.Value)).ToArray();
    }

    /// <summary>The response prompt's Yes choices and its pass choice.</summary>
    public static (IReadOnlyList<ChoiceView> Responses, ChoiceView? Pass) Responses(DuelView view) =>
        (view.Choices.Where(c => c.Kind is not (ActionKind.ChainPass or ActionKind.BattlePass)).ToArray(),
         view.Choices.FirstOrDefault(c => c.Kind is ActionKind.ChainPass or ActionKind.BattlePass));

    /// <summary>The board object an attack choice targets: the opponent's monster slot, or none for a direct attack.</summary>
    public static int? AttackTarget(ChoiceView choice) => choice.Kind == ActionKind.Attack ? choice.TargetZone : null;
}
