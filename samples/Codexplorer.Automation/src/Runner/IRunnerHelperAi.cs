namespace Codexplorer.Automation.Runner;

/// <summary>Defines the helper model boundary.</summary>
internal interface IRunnerHelperAi
{
    /// <summary>Asynchronously receives an answer and its usage for a runner question.</summary>
    /// <param name="request">The task context and question.</param>
    /// <param name="ct">The token observed while receiving the response.</param>
    /// <returns>A task containing the answer and provider usage, including empty received answers.</returns>
    Task<RunnerHelperAiResult> AnswerAsync(RunnerHelperAiRequest request, CancellationToken ct);
}
