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
public sealed record MenuItem(string Label, MenuCommand Command);

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
            return [new("Surrender", new UnsupportedCommand("Surrender is not available in the PC duel yet."))];
        if (target.Kind == FieldObjectKind.Extra && target.Own || target.Kind == FieldObjectKind.Graveyard && target.Card is not null)
            return [new("Show Card List", new UnsupportedCommand("The card list screen is not available in the PC duel yet."))];
        if (target.Card is not { Hidden: false } card) return items;
        var own = view.Choices.Where(c => c.FocusInstanceId is { } id && id == card.InstanceId && c.Kind is not null && !ResponseKinds.Contains(c.Kind.Value));
        foreach (var group in own.GroupBy(c => Verb(c.Kind!.Value)).OrderBy(g => VerbOrder(g.Key)))
            items.Add(new(group.Key, Command(group.Key, group.ToArray())));
        items.Add(new(ShowDetails, new DetailsCommand(card)));
        return items;
    }

    private static string Verb(ActionKind kind) => kind switch
    {
        ActionKind.Summon => "Summon",
        ActionKind.SetMonster or ActionKind.SetSpellTrap => "Set",
        ActionKind.Attack => "Attack",
        ActionKind.Position => "Change Position",
        ActionKind.Unequip => "Unequip",
        _ => "Activate"
    };

    private static int VerbOrder(string verb) => verb switch { "Summon" => 0, "Set" => 1, "Activate" => 2, "Attack" => 3, "Change Position" => 4, _ => 5 };

    private static MenuCommand Command(string verb, ChoiceView[] choices)
    {
        if (choices.Length == 1) return new SubmitCommand(choices[0].Id);
        if (verb == "Attack") return new TargetCommand(choices);
        // Several free zones for the same summon or set: the original places the card itself.
        if (verb is "Summon" or "Set" && choices.Select(c => c.Kind).Distinct().Count() == 1 &&
            choices.Select(c => StripZone(c.Label)).Distinct().Count() == 1 && choices.All(c => c.Zone is not null))
            return new SubmitCommand(choices.OrderBy(c => Array.IndexOf(PreferredZones, c.Zone!.Value) is var i && i < 0 ? 99 : i).First().Id);
        return new ListCommand(choices);
    }

    private static string StripZone(string label) => label.Contains(" [", StringComparison.Ordinal) ? label[..label.LastIndexOf(" [", StringComparison.Ordinal)] : label;

    /// <summary>Global menu (△): Show Details, Change Phase, Option, Help (measured order).</summary>
    public static IReadOnlyList<string> GlobalMenu { get; } = [ShowDetails, "Change Phase", "Option", "Help"];

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
