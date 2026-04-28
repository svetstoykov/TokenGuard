namespace Codexplorer.Automation.Runner;

internal static class AutomationRunnerPrompts
{
    public static string CreateContinuationPrompt(int turnsRemaining)
    {
        return
            $"""
            Continue working on current task.
            Keep making concrete progress.
            Do not stop for summary yet.
            Be willing to ask for clarification sooner when requirements are ambiguous, repository context is missing, or a risky assumption would change your approach.
            When blocked by uncertainty, ask one precise question with `QUESTION_FOR_RUNNER:` instead of guessing.
            Remaining planned turn budget is approximately {turnsRemaining}. Treat this as guidance, not a hard stop.
            """;
    }

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
            Remaining planned turn budget is approximately {turnsRemaining}. Treat this as guidance, not a hard stop.
            """;
    }

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
