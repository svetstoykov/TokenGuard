using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.E2E.Tasks;
using TokenGuard.Extensions.OpenAI;
using Xunit.Abstractions;

namespace TokenGuard.E2E.OpenAI;

/// <summary>
/// Exercises a real OpenRouter-backed agent loop and verifies that TokenGuard compacts context
/// before the task-specific workflow finishes.
/// </summary>
public sealed class OpenRouterAgentLoopE2ETests(ITestOutputHelper output)
{
    private const int MaxTokens = 6000;
    private const double CompactionThreshold = 0.80;
    private const int MaxIterations = 15;

    public static IEnumerable<object[]> AllTasks()
    {
        yield return [CodeReviewTask.Create()];
        yield return [ReleaseAuditTask.Create()];
        yield return [DependencyAuditTask.Create()];
    }

    /// <summary>
    /// Runs one seeded workspace task through a live model loop and asserts both task completion
    /// and compaction-related invariants.
    /// </summary>
    /// <param name="task">Defines the workspace fixture, prompts, completion marker, and final assertions for this scenario.</param>
    [Theory]
    [MemberData(nameof(AllTasks))]
    [Trait("Category", "E2E")]
    public async Task AgentLoop_WhenTaskRequiresRealToolWork_CompactsContextAndCompletesTask(
        AgentLoopTaskDefinition task)
    {
        // Arrange
        using var workspace = TestWorkspace.Create(nameof(this.AgentLoop_WhenTaskRequiresRealToolWork_CompactsContextAndCompletesTask));
        var workspaceDirectory = workspace.DirectoryPath;
        
        output.WriteLine($"[E2E] Seeding workspace {workspaceDirectory} with {task.Name}...");
        
        await task.SeedWorkspaceAsync(workspaceDirectory);

        var chatClient = OpenRouterE2ETestSupport.CreateChatClient();
        var tools = OpenRouterE2ETestSupport.CreateTools(workspaceDirectory);
        var chatOptions = OpenRouterE2ETestSupport.CreateChatOptions(tools);

        var services = new ServiceCollection();
        services.AddConversationContext(task.ConversationName, builder => builder
            .WithMaxTokens(MaxTokens)
            .WithCompactionThreshold(CompactionThreshold));

        await using var serviceProvider = services.BuildServiceProvider();
        
        using var conversationContext = serviceProvider
            .GetRequiredService<IConversationContextFactory>()
            .Create(task.ConversationName);

        conversationContext.SetSystemPrompt(task.SystemPrompt);
        conversationContext.AddUserMessage(task.UserMessage);

        var toolMap = tools.ToDictionary(static t => t.Name, static t => t);
        var toolExecutions = 0;
        var observedCompaction = false;
        var completed = false;
        string? finalResponseText = null;

        output.WriteLine(
            $"[E2E] task={task.Name} | maxTokens={MaxTokens} threshold={CompactionThreshold} maxIterations={MaxIterations}");

        // Act
        var iteration = 0;
        for (; iteration < MaxIterations && !completed; iteration++)
        {
            // PrepareAsync is the observation point for masking. Sampling here tells us whether
            // TokenGuard compacted before the next provider request goes out.
            var preparedMessages = await conversationContext.PrepareAsync();
            var maskedCount = preparedMessages.Count(m => m.State == CompactionState.Masked);

            if (maskedCount > 0)
            {
                observedCompaction = true;
                output.WriteLine(
                    $"[E2E] compaction triggered | iteration={iteration + 1} | masked={maskedCount}/{preparedMessages.Count}");
            }
            else
            {
                output.WriteLine($"[E2E] iteration={iteration + 1} | messages={preparedMessages.Count}");
            }

            var response = await chatClient.CompleteChatAsync(preparedMessages.ForOpenAI(), chatOptions);
            var completion = response.Value;

            output.WriteLine(
                $"[E2E] model response | finish={completion.FinishReason} inputTokens={completion.InputTokens()}");

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                conversationContext.RecordModelResponse(completion.ResponseSegments(), completion.InputTokens());

                foreach (var call in completion.ToolCalls)
                {
                    toolExecutions++;

                    var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    output.WriteLine(
                        $"[E2E]   tool={call.FunctionName} args={call.FunctionArguments} result={Truncate(resultText, 120)}");

                    conversationContext.RecordToolResult(call.Id, call.FunctionName, resultText);
                }

                // Tool-call turns are intentionally open-ended. Continue loop so model can inspect
                // fresh tool output and decide whether more work remains.
                continue;
            }

            var textSegments = completion.TextSegments();
            conversationContext.RecordModelResponse(textSegments, completion.InputTokens());
            finalResponseText = string.Join(Environment.NewLine, textSegments.Select(s => s.Content));
            completed = finalResponseText.Contains(task.CompletionMarker, StringComparison.Ordinal);

            output.WriteLine(
                $"[E2E] text response | completed={completed} | text={Truncate(finalResponseText, 200)}");

            if (!completed)
            {
                // Completion marker is sentinel, not semantic judgment. If model responds early,
                // push it back into tool use instead of accepting natural-language confidence.
                conversationContext.AddUserMessage(
                    "The task is not complete yet. Continue using tools until all required output files are correctly written. " +
                    $"Only the final completion message may contain {task.CompletionMarker}.");
            }
        }

        // Last loop iteration can terminate immediately after text response, so sample one more
        // PrepareAsync call to avoid missing masking that first appears at final boundary.
        var finalPrepared = await conversationContext.PrepareAsync();
        if (finalPrepared.Any(m => m.State == CompactionState.Masked))
        {
            observedCompaction = true;
        }

        output.WriteLine(
            $"[E2E] Done | task={task.Name} toolExecutions={toolExecutions} compaction={observedCompaction} completed={completed}");

        // Structural loop invariants. These stay stable even when live-model phrasing shifts.
        toolExecutions.Should().BeGreaterThanOrEqualTo(5,
            because: "the model must inspect, read multiple files, and write all required outputs through actual tool calls");
        observedCompaction.Should().BeTrue(
            because: "a rich multi-file workspace with 15 iterations should accumulate enough tokens to trigger compaction");
        completed.Should().BeTrue(
            because: $"the loop must reach the '{task.CompletionMarker}' completion marker within {MaxIterations} iterations");
        // Model should finish with headroom, not stumble into success on final allowed turn.
        iteration.Should().BeLessThan(MaxIterations,
            because: "the loop should terminate via the completion marker, not by exhausting the iteration budget");
        finalResponseText.Should().NotBeNullOrWhiteSpace();
        // Response prose is non-deterministic. Verify envelope and sentinel, not wording.
        var responseLines = finalResponseText!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        responseLines.Should().HaveCountGreaterThanOrEqualTo(1).And.HaveCountLessThanOrEqualTo(10,
            because: "the model is instructed to respond with bullet points plus a completion marker; exact count varies with preamble/formatting");
        responseLines.TakeLast(2).Should().Contain(line => line.Contains(task.CompletionMarker),
            because: "the completion marker must appear in or immediately after the final bullet, confirming the task is structurally done");

        // Task-specific assertions validate actual file artefacts in seeded workspace.
        await task.AssertOutcomeAsync(workspaceDirectory, finalResponseText);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");
}
