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
            If you need genuine outside clarification, ask one precise question with `QUESTION_FOR_RUNNER:`.
            Remaining task turn budget is approximately {turnsRemaining}.
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
            If you need genuine outside clarification, ask one precise question with `QUESTION_FOR_RUNNER:`.
            Remaining task turn budget is approximately {turnsRemaining}.
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
