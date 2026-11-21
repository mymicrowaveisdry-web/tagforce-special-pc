using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Summon on a card that needs Tributes: the original confirmation, then the Tribute selection on the field.</summary>
public sealed record TributeCommand(IReadOnlyList<ChoiceView> Choices) : MenuCommand;

/// <summary>"You must Tribute n monster(s)… Tribute Summon?" with YES (0) or NO (1) selected.</summary>
public sealed record TributeConfirmModal(IReadOnlyList<ChoiceView> Choices, int Index) : DuelModal;

/// <summary>Tribute selection: the field cursor on own Monster Zone <paramref name="Zone"/>; <paramref name="Marked"/> is a zone bit mask.</summary>
public sealed record TributeModal(IReadOnlyList<ChoiceView> Choices, int Zone, int Marked = 0) : DuelModal;

/// <summary>Measured (order32-win-5, Tribute Summon of a Level 6 monster with one monster on the field): Summon opens the Tribute prompt
/// (YES selected); YES puts the normal field cursor on the monster; ○ Tributes it and the summon resolves into its zone. Inferred: Left/Right
/// move among eligible monsters, ○ on a marked monster unmarks it, the summon submits once enough monsters are marked, NO and × back out.
/// Tribute choices carry the Tribute zones as a bit mask in <c>TargetZone</c> (2000 + mask, Core's Tribute Summon candidates).</summary>
public static class TributeSelection
{
    private const int MaskBase = 2000;

    public static int? Mask(ChoiceView choice) =>
        choice.Label.StartsWith("Tribute ", StringComparison.Ordinal) && choice.TargetZone is >= MaskBase and < MaskBase + 32
            ? choice.TargetZone - MaskBase : null;

    /// <summary>Summon choices that are all Tribute Summons needing the same number of Tributes.</summary>
    public static bool Applies(IReadOnlyList<ChoiceView> choices) =>
        choices.Count > 0 && choices.All(c => c.Kind == ActionKind.Summon && Mask(c) is > 0)
        && choices.Select(c => Count(Mask(c)!.Value)).Distinct().Count() == 1;

    public static int Count(IReadOnlyList<ChoiceView> choices) => Count(Mask(choices[0])!.Value);

    private static int Count(int mask) => System.Numerics.BitOperations.PopCount((uint)mask);

    /// <summary>Own Monster Zones some choice can Tribute, left to right.</summary>
    public static IReadOnlyList<int> Eligible(IReadOnlyList<ChoiceView> choices)
    {
        int all = choices.Aggregate(0, (m, c) => m | Mask(c)!.Value);
        return [.. Enumerable.Range(0, 5).Where(z => (all & 1 << z) != 0)];
    }

    public static TributeModal Open(IReadOnlyList<ChoiceView> choices) => new(choices, Eligible(choices)[0]);

    public static TributeModal Move(TributeModal modal, int dx)
    {
        var zones = Eligible(modal.Choices);
        int at = Math.Max(0, zones.ToList().IndexOf(modal.Zone));
        return modal with { Zone = zones[((at + dx) % zones.Count + zones.Count) % zones.Count] };
    }

    /// <summary>○ toggles the focused monster; the choice whose Tributes equal the marked set, once complete.</summary>
    public static (TributeModal Modal, ChoiceView? Submit) Toggle(TributeModal modal)
    {
        var next = modal with { Marked = modal.Marked ^ 1 << modal.Zone };
        if (Count(next.Marked) < Count(modal.Choices)) return (next, null);
        return (next, modal.Choices.FirstOrDefault(c => Mask(c) == next.Marked));
    }

    /// <summary>The field object for the focused zone.</summary>
    public static FieldObject? Slot(DuelFieldFocus focus, int zone) =>
        focus.Row(DuelFieldRows.OwnMonster).FirstOrDefault(o => o.Kind == FieldObjectKind.Monster && o.Index == zone);

    public static DuelChainPrompt Prompt(TributeConfirmModal modal)
    {
        int count = Count(modal.Choices);
        return new(DuelPart.Prompt, count.ToString(System.Globalization.CultureInfo.InvariantCulture), [], modal.Index)
            { Layout = DuelPromptLayout.Tribute(count) };
    }
}
