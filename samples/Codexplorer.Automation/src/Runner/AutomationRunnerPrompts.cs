namespace Codexplorer.Automation.Runner;

/// <summary>Provides instructions for continued exploration and task wrap-up.</summary>
internal static class AutomationRunnerPrompts
{
    /// <summary>Creates a continuation instruction with the remaining hard call allowance.</summary>
    /// <param name="turnsRemaining">The remaining provider-call allowance.</param>
    /// <returns>The instruction to submit to the exploration session.</returns>
    public static string CreateContinuationPrompt(int turnsRemaining)
    {
        return
            $"""
            Continue working on current task.
            Keep making concrete progress.
            Do not stop for summary yet.
            Be willing to ask for clarification sooner when requirements are ambiguous, repository context is missing, or a risky assumption would change your approach.
            When blocked by uncertainty, ask one precise question with `QUESTION_FOR_RUNNER:` instead of guessing.
            Remaining provider-call allowance is {turnsRemaining}. This is a hard limit; preserve time for the final summary.
            """;
    }

    /// <summary>Creates an instruction to resume after an exchange cap.</summary>
    /// <param name="turnsRemaining">The remaining provider-call allowance.</param>
    /// <returns>The instruction to submit to the exploration session.</returns>
    public static string CreateResumePrompt(int turnsRemaining)
    {
        return
            $"""
            Continue from previous partial state.
            You hit previous per-message turn cap before finishing reply.
            Do not repeat completed work.
            Keep moving task forward.
            Be more liberal about asking for clarification when the next step depends on intent, missing context, or a meaningful product decision.
            When blocked by uncertainty, ask one precise question with `QUESTION_FOR_RUNNER:` instead of forcing progress through guesses.
            Remaining provider-call allowance is {turnsRemaining}. This is a hard limit; preserve time for the final summary.
            """;
    }

    /// <summary>Creates the final task-summary instruction.</summary>
    /// <returns>The instruction to submit to the exploration session.</returns>
    public static string CreateWrapUpPrompt()
    {
        return
            $"""
            Stop live work for now.
            Summarize concrete progress, unfinished work, blockers, and next recommended steps.
            If you create task-owned notes or artifacts, write them under an `artifacts/` folder in your current workspace.
            Do not write task-owned artifacts anywhere else.
            After summary, stop.
            """;
    }
}
