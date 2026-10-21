using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Client;
using Codexplorer.Automation.Protocol;
using Codexplorer.Automation.Runner;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codexplorer.Automation.AgentComparison.Codexplorer;

/// <summary>Adapts the existing Codexplorer open, submit and close protocol.</summary>
/// <remarks>This per-session adapter preserves the native runner continuation and helper prompts.</remarks>
internal sealed class CodexplorerRuntime(IRecordingScope recording, IComparisonHelper helper) : IComparisonRuntime
{
    /// <inheritdoc />
    public async Task<RuntimeResult> RunAsync(RuntimeInvocation invocation, CancellationToken cancellationToken)
    {
        var childInvocation = invocation with { SummarizerCredential = recording.CredentialFor(RecordedRequestKind.Compaction) };
        await using IAutomationProtocolTransport transport = new ComparisonProtocolTransport(childInvocation);
        ICodexplorerAutomationClient client = new CodexplorerAutomationClient(transport, NullLogger<CodexplorerAutomationClient>.Instance);
        var captures = new List<string>();
        string? sessionId = null;
        string? answer = null;
        string? error = null;
        var outcome = "failed";
        var sessionOpen = false;
        var wrapUp = false;
        try
        {
            var ping = await client.PingAsync(cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(invocation.StateRoot, "effective-settings.json"),
                System.Text.Json.JsonSerializer.Serialize(ping, AutomationProtocolJson.SerializerOptions), cancellationToken);
            var opened = await client.OpenSessionAsync(new OpenSessionRequest
            {
                WorkspacePath = invocation.Checkout,
                RepositoryCommit = invocation.Session.Task.RepositoryCommit,
                ModelCallBudget = invocation.Session.MaxAgentCalls,
                WrapUpWindow = invocation.Session.WrapUpCalls,
                SessionDirectory = Path.GetDirectoryName(invocation.Artifacts),
                Capture = true
            }, cancellationToken).ConfigureAwait(false);
            sessionId = opened.SessionId;
            sessionOpen = true;
            var message = invocation.Session.Prompt;
            while (true)
            {
                var response = await client.SubmitAsync(new SubmitRequest(sessionId, message), cancellationToken).ConfigureAwait(false);
                sessionOpen = response.SessionOpen;
                answer = response.AssistantText ?? answer;
                captures.Add(response.LogFilePath);
                if (response.Outcome == "reply_received" && wrapUp)
                {
                    outcome = "completed";
                    break;
                }
                if (response.Outcome is not ("reply_received" or "max_turns_reached"))
                {
                    outcome = response.Outcome is "turn_budget_reached" or "budget_exceeded" ? "budgetExhausted"
                        : response.Outcome == "cancelled" ? "cancelled" : "failed";
                    error = response.Failure?.ExceptionType ?? response.BudgetFailureReason ?? response.Outcome;
                    break;
                }
                var consumed = recording.AgentCallsConsumed;
                if (!wrapUp && recording.MainCallsConsumed >= invocation.Session.MaxAgentCalls - invocation.Session.WrapUpCalls)
                {
                    recording.BeginWrapUp();
                    wrapUp = true;
                    message = AutomationRunnerPrompts.CreateWrapUpPrompt();
                }
                else if (consumed >= invocation.Session.MaxAgentCalls)
                {
                    outcome = "budgetExhausted";
                    break;
                }
                else if (response.AsksRunner)
                {
                    message = await helper.AnswerAsync(invocation, response.RunnerQuestion!, response.AssistantText,
                        consumed, wrapUp, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var remaining = invocation.Session.MaxAgentCalls - consumed;
                    message = response.Outcome == "max_turns_reached"
                        ? AutomationRunnerPrompts.CreateResumePrompt(remaining) : AutomationRunnerPrompts.CreateContinuationPrompt(remaining);
                }
            }
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name;
        }
        finally
        {
            if (sessionId is not null && sessionOpen)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var closed = await client.CloseSessionAsync(new CloseSessionRequest(sessionId), cleanup.Token).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(invocation.StateRoot, "close-session.json"),
                        System.Text.Json.JsonSerializer.Serialize(closed, AutomationProtocolJson.SerializerOptions));
                    if (closed.Status == "failed")
                        outcome = "failed";
                }
                catch (Exception exception)
                {
                    error ??= exception.GetType().Name;
                    outcome = "failed";
                }
            }
        }
        return new RuntimeResult(outcome, outcome != "completed", answer, recording.AgentCallsConsumed,
            captures.Distinct().ToArray(), error);
    }
}
