namespace Codexplorer.Automation.Runner;

internal interface IRunnerHelperAi
{
    Task<string> AnswerAsync(RunnerHelperAiRequest request, CancellationToken ct);
}
