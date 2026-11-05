using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.AI;

public sealed record ReplayEntry(long BeforeRevision, long AfterRevision, PlayerId Player,
    string ActionKey, ActionKind Kind, long? DecisionId);
public sealed record DuelReplay(string PolicyVersion, uint Seed, string DeckManifest,
    IReadOnlyList<uint> FirstDeck, IReadOnlyList<uint> SecondDeck, bool StagedBattle,
    IReadOnlyList<ReplayEntry> Steps, DuelOutcome? Outcome)
{
    public int SchemaVersion => 1;
    public string ActionContractVersion => DuelActionSource.ContractVersion;
    public string CommandDigest() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Steps.Select(s => $"{s.BeforeRevision}|{s.AfterRevision}|{s.Player}|{s.ActionKey}|{s.DecisionId}")))));
}

public static class ReplayCommands
{
    /// <summary>Verifies semantic command identity/revision against freshly discovered authorized actions.</summary>
    public static ActionExecution Apply(DuelActionSource source, ReplayEntry entry)
    {
        var view = source.Observe(entry.Player);
        if (view.Revision != entry.BeforeRevision || view.Decision?.Id != entry.DecisionId || entry.AfterRevision != entry.BeforeRevision + 1)
            return new(false, DuelError.StaleDecision, view.Revision, null);
        var matches = view.Actions.Where(a => a.Key == entry.ActionKey && a.Kind == entry.Kind).ToArray();
        return matches.Length == 1 ? source.Execute(entry.Player, view.Revision, matches[0].Token)
            : new(false, DuelError.InvalidDecisionChoice, view.Revision, null);
    }
}
