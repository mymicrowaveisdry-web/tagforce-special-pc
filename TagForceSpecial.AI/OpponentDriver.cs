using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.AI;

public enum OpponentStatus { Advanced, Waiting, Paused, Finished, Unsupported, Rejected, BudgetExceeded }
public sealed record OpponentStep(OpponentStatus Status, long Revision, PolicyChoice? Choice = null, string Diagnostic = "");

/// <summary>One command per call, with pause, cancellation and hard command/rejection limits.</summary>
public sealed class OpponentDriver
{
    private readonly DeterministicPolicy policy;
    private readonly PlayerId player;
    private readonly int stepBudget;
    private int rejections;
    public int Steps { get; private set; }
    public bool Paused { get; set; }
    public string PolicyVersion => DeterministicPolicy.Version;
    public uint Seed => policy.Seed;

    public OpponentDriver(PlayerId player, uint seed, int stepBudget = 12000)
    {
        if (!Enum.IsDefined(player) || stepBudget <= 0) throw new ArgumentOutOfRangeException(nameof(stepBudget));
        this.player = player; this.stepBudget = stepBudget; policy = new(seed);
    }

    public OpponentStep Step(DuelActionSource source, CancellationToken cancellation = default)
    {
        var view = source.Observe(player);
        if (view.Outcome is not null) return new(OpponentStatus.Finished, view.Revision);
        if (Paused || cancellation.IsCancellationRequested) return new(OpponentStatus.Paused, view.Revision);
        if (view.Availability == ActionAvailability.Waiting) return new(OpponentStatus.Waiting, view.Revision);
        if (Steps >= stepBudget || rejections >= 3) return new(OpponentStatus.BudgetExceeded, view.Revision, Diagnostic: "Opponent command/rejection budget exhausted");
        var choice = policy.Choose(view);
        if (choice is null) return new(OpponentStatus.Unsupported, view.Revision, Diagnostic: view.Diagnostic.Length > 0 ? view.Diagnostic : "No supported policy choice");
        var result = source.Execute(player, choice.Revision, choice.Token);
        if (!result.Accepted)
        {
            rejections++;
            return new(OpponentStatus.Rejected, result.Revision, choice, result.Error.ToString());
        }
        rejections = 0;
        Steps++;
        return new(OpponentStatus.Advanced, result.Revision, choice);
    }
}
