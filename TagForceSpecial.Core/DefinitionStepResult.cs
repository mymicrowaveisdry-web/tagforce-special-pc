namespace TagForceSpecial.Core;

/// <summary>How the step runner goes on after one definition step.</summary>
internal enum StepFlow
{
    /// <summary>Go straight to the next step.</summary>
    Next,
    /// <summary>End resolution if the duel is over, otherwise go to the next step.</summary>
    Checked,
    /// <summary>The rest of the link does not resolve.</summary>
    Stop
}

/// <summary>One resolved definition step: the new state, how to go on, the answer left for later steps, or a choice that pauses resolution.</summary>
internal readonly record struct StepResult(DuelState State, StepFlow Flow, DuelCommand? Choice, PendingDefinitionChoice? Pause = null)
{
    internal static StepResult Paused(DuelState state, PendingDefinitionChoice pause) => new(state, StepFlow.Stop, null, pause);

    /// <summary>Records why the link stopped and ends it.</summary>
    internal static StepResult Skip(DuelState state, ChainLink link, ChainSkipReason reason, List<DuelEvent> events)
    {
        events.Add(new ChainLinkSkipped(link.Number, reason));
        return new(state, StepFlow.Stop, null);
    }

    /// <summary>Records why the step did nothing and goes on to the next one.</summary>
    internal static StepResult SkipAndContinue(DuelState state, ChainLink link, ChainSkipReason reason, List<DuelEvent> events, DuelCommand? choice)
    {
        events.Add(new ChainLinkSkipped(link.Number, reason));
        return new(state, StepFlow.Next, choice);
    }
}
