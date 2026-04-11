using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.E2E;
using TokenGuard.E2E.OpenAI;
using TokenGuard.E2E.Tasks;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Samples.Benchmark.Models;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Benchmark;

/// <summary>
/// Executes raw and managed benchmark runs for seeded E2E task definitions.
/// </summary>
/// <remarks>
/// <para>
/// This runner keeps every benchmark variable constant except conversation management. Both modes
/// use same OpenRouter client, model, task prompt, system prompt, and tool set. Only history
/// handling changes between plain provider messages and <see cref="IConversationContext"/>.
/// </para>
/// <para>
/// Raw runs are intentionally unbounded so benchmark measures true cumulative resend cost. Managed
/// runs use <see cref="SlidingWindowStrategy"/> with same thresholds requested by benchmark spec.
/// </para>
/// </remarks>
public sealed class BenchmarkRunner
{
    /// <summary>
    /// Gets model identifier used by benchmark runner.
    /// </summary>
    public const string ModelName = "openai/gpt-5.4-nano";

    /// <summary>
    /// Executes selected task under each supplied configuration and returns report model.
    /// </summary>
    /// <param name="task">Task definition to seed, execute, and evaluate.</param>
    /// <param name="configurations">Configurations to run sequentially.</param>
    /// <param name="cancellationToken">Cancellation token for benchmark execution.</param>
    /// <returns>Structured benchmark report containing run results and comparison metrics.</returns>
    public async Task<BenchmarkReport> RunAsync(
        AgentLoopTaskDefinition task,
        IReadOnlyList<BenchmarkConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(configurations);

        List<RunResult> runs = [];

        foreach (var configuration in configurations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runs.Add(await this.RunConfigurationAsync(task, configuration, cancellationToken));
        }

        return new BenchmarkReport(
            task.Name,
            ModelName,
            DateTimeOffset.UtcNow,
            configurations.ToArray(),
            runs,
            BuildComparison(runs));
    }

    private async Task<RunResult> RunConfigurationAsync(
        AgentLoopTaskDefinition task,
        BenchmarkConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var workspace = TestWorkspace.Create($"benchmark-{task.Name}-{configuration.Name}");
        await task.SeedWorkspaceAsync(workspace.DirectoryPath);

        var chatClient = OpenRouterE2ETestSupport.CreateChatClient();
        var tools = OpenRouterE2ETestSupport.CreateTools(workspace.DirectoryPath);
        var chatOptions = OpenRouterE2ETestSupport.CreateChatOptions(tools);
        var toolMap = tools.ToDictionary(static tool => tool.Name, static tool => tool, StringComparer.Ordinal);
        var turns = new List<TurnTelemetry>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var compactionEvents = 0;
        var completed = false;
        string? finalResponseText = null;
        string? failureReason = null;

        System.Console.WriteLine($"[{configuration.Name}] task={task.Name} model={ModelName}");

        try
        {
            switch (configuration.Mode)
            {
                case BenchmarkMode.Raw:
                    await ExecuteRawAsync(
                        task,
                        configuration,
                        chatClient,
                        chatOptions,
                        toolMap,
                        turns,
                        workspace.DirectoryPath,
                        onTurnCompleted: telemetry =>
                        {
                            totalInputTokens += telemetry.InputTokens ?? 0;
                            totalOutputTokens += telemetry.OutputTokens ?? 0;
                            if (telemetry.Compacted)
                            {
                                compactionEvents++;
                            }
                        },
                        onCompleted: responseText =>
                        {
                            completed = true;
                            finalResponseText = responseText;
                        },
                        onFailure: reason => failureReason = reason,
                        cancellationToken: cancellationToken);
                    break;

                case BenchmarkMode.SlidingWindow:
                    await ExecuteManagedAsync(
                        task,
                        configuration,
                        chatClient,
                        chatOptions,
                        toolMap,
                        turns,
                        workspace.DirectoryPath,
                        onTurnCompleted: telemetry =>
                        {
                            totalInputTokens += telemetry.InputTokens ?? 0;
                            totalOutputTokens += telemetry.OutputTokens ?? 0;
                            if (telemetry.Compacted)
                            {
                                compactionEvents++;
                            }
                        },
                        onCompleted: responseText =>
                        {
                            completed = true;
                            finalResponseText = responseText;
                        },
                        onFailure: reason => failureReason = reason,
                        cancellationToken: cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(configuration.Mode), configuration.Mode, "Unsupported benchmark mode.");
            }

            if (completed)
            {
                await task.AssertOutcomeAsync(workspace.DirectoryPath, finalResponseText);
            }
        }
        catch (Exception ex) when (configuration.Mode == BenchmarkMode.Raw && IsContextLengthError(ex))
        {
            failureReason = ex.Message;
            completed = false;
            System.Console.WriteLine($"[{configuration.Name}] context window exceeded; continuing benchmark");
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            completed = false;
            System.Console.WriteLine($"[{configuration.Name}] failed: {ex.Message}");
        }

        stopwatch.Stop();

        return new RunResult(
            configuration.Name,
            completed,
            turns.Count,
            totalInputTokens,
            totalOutputTokens,
            compactionEvents,
            stopwatch.ElapsedMilliseconds,
            turns,
            failureReason);
    }

    private static async Task ExecuteRawAsync(
        AgentLoopTaskDefinition task,
        BenchmarkConfiguration configuration,
        ChatClient chatClient,
        ChatCompletionOptions chatOptions,
        IReadOnlyDictionary<string, ITool> toolMap,
        List<TurnTelemetry> turns,
        string workspaceDirectory,
        Action<TurnTelemetry> onTurnCompleted,
        Action<string> onCompleted,
        Action<string> onFailure,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new SystemChatMessage(task.SystemPrompt),
            new UserChatMessage(task.UserMessage),
        ];

        for (var turn = 1; turn <= configuration.MaxIterations; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var turnStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var completion = (await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken)).Value;
            var inputTokens = completion.InputTokens();
            var outputTokens = completion.Usage?.OutputTokenCount;
            var cumulativeInputTokens = (turns.LastOrDefault()?.CumulativeInputTokens ?? 0) + (inputTokens ?? 0);

            turnStopwatch.Stop();

            var telemetry = new TurnTelemetry(
                turn,
                inputTokens,
                outputTokens,
                cumulativeInputTokens,
                false,
                0,
                turnStopwatch.ElapsedMilliseconds,
                completion.FinishReason.ToString());

            turns.Add(telemetry);
            onTurnCompleted(telemetry);

            System.Console.WriteLine($"[{configuration.Name}] turn={turn} finish={completion.FinishReason} input={inputTokens} output={outputTokens}");

            AppendModelMessage(messages, completion);

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var call in completion.ToolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    messages.Add(new ToolChatMessage(call.Id, resultText));
                }

                continue;
            }

            var finalResponseText = string.Join(Environment.NewLine, completion.TextSegments().Select(static segment => segment.Content));
            if (finalResponseText.Contains(task.CompletionMarker, StringComparison.Ordinal))
            {
                onCompleted(finalResponseText);
                return;
            }

            messages.Add(new UserChatMessage(
                "The task is not complete yet. Continue using tools until all required output files are correctly written. " +
                $"Only the final completion message may contain {task.CompletionMarker}."));
        }

        onFailure($"Maximum iteration budget reached for {configuration.Name} in workspace {workspaceDirectory}.");
    }

    private static async Task ExecuteManagedAsync(
        AgentLoopTaskDefinition task,
        BenchmarkConfiguration configuration,
        ChatClient chatClient,
        ChatCompletionOptions chatOptions,
        IReadOnlyDictionary<string, ITool> toolMap,
        List<TurnTelemetry> turns,
        string workspaceDirectory,
        Action<TurnTelemetry> onTurnCompleted,
        Action<string> onCompleted,
        Action<string> onFailure,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddConversationContext(task.ConversationName, builder => builder
            .WithMaxTokens(configuration.MaxTokens ?? 16_000)
            .WithStrategy(new SlidingWindowStrategy(new SlidingWindowOptions(protectedWindowFraction: 0.2)))
            .WithCompactionThreshold(configuration.CompactionThreshold ?? 0.80));

        await using var serviceProvider = services.BuildServiceProvider();
        using var conversationContext = serviceProvider
            .GetRequiredService<IConversationContextFactory>()
            .Create(task.ConversationName);

        conversationContext.SetSystemPrompt(task.SystemPrompt);
        conversationContext.AddUserMessage(task.UserMessage);

        for (var turn = 1; turn <= configuration.MaxIterations; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);
            var maskedCount = preparedMessages.Count(static message => message.State == CompactionState.Masked);
            var compacted = maskedCount > 0;

            var turnStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var completion = (await chatClient.CompleteChatAsync(preparedMessages.ForOpenAI(), chatOptions, cancellationToken)).Value;
            turnStopwatch.Stop();

            var inputTokens = completion.InputTokens();
            var outputTokens = completion.Usage?.OutputTokenCount;
            var cumulativeInputTokens = (turns.LastOrDefault()?.CumulativeInputTokens ?? 0) + (inputTokens ?? 0);

            var telemetry = new TurnTelemetry(
                turn,
                inputTokens,
                outputTokens,
                cumulativeInputTokens,
                compacted,
                maskedCount,
                turnStopwatch.ElapsedMilliseconds,
                completion.FinishReason.ToString());

            turns.Add(telemetry);
            onTurnCompleted(telemetry);

            System.Console.WriteLine($"[{configuration.Name}] turn={turn} finish={completion.FinishReason} input={inputTokens} output={outputTokens} compacted={compacted} masked={maskedCount}");

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                conversationContext.RecordModelResponse(completion.ResponseSegments(), inputTokens);

                foreach (var call in completion.ToolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    conversationContext.RecordToolResult(call.Id, call.FunctionName, resultText);
                }

                continue;
            }

            var textSegments = completion.TextSegments();
            conversationContext.RecordModelResponse(textSegments, inputTokens);
            var finalResponseText = string.Join(Environment.NewLine, textSegments.Select(static segment => segment.Content));

            if (finalResponseText.Contains(task.CompletionMarker, StringComparison.Ordinal))
            {
                onCompleted(finalResponseText);
                return;
            }

            conversationContext.AddUserMessage(
                "The task is not complete yet. Continue using tools until all required output files are correctly written. " +
                $"Only the final completion message may contain {task.CompletionMarker}.");
        }

        onFailure($"Maximum iteration budget reached for {configuration.Name} in workspace {workspaceDirectory}.");
    }

    private static void AppendModelMessage(List<ChatMessage> messages, ChatCompletion completion)
    {
        var text = string.Join(Environment.NewLine, completion.TextSegments().Select(static segment => segment.Content));
        AssistantChatMessage assistant = new(text);

        foreach (var call in completion.ToolCalls)
        {
            assistant.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                call.Id,
                call.FunctionName,
                BinaryData.FromString(call.FunctionArguments.ToString())));
        }

        messages.Add(assistant);
    }

    private static BenchmarkComparison BuildComparison(IReadOnlyList<RunResult> runs)
    {
        var raw = runs.Single(run => run.Configuration == BenchmarkConfiguration.Raw.Name);
        var managed = runs.Single(run => run.Configuration == BenchmarkConfiguration.SlidingWindow.Name);
        var savingsPercent = raw.TotalInputTokens == 0
            ? 0
            : ((raw.TotalInputTokens - managed.TotalInputTokens) / (double)raw.TotalInputTokens) * 100;

        return new BenchmarkComparison(
            Math.Round(savingsPercent, 1),
            raw.TotalInputTokens,
            managed.TotalInputTokens,
            raw.TurnCount,
            managed.TurnCount,
            raw.Completed && managed.Completed);
    }

    private static bool IsContextLengthError(Exception exception)
    {
        return exception.Message.Contains("context", StringComparison.OrdinalIgnoreCase)
               && (exception.Message.Contains("length", StringComparison.OrdinalIgnoreCase)
                   || exception.Message.Contains("window", StringComparison.OrdinalIgnoreCase)
                   || exception.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase)
                   || exception.Message.Contains("too large", StringComparison.OrdinalIgnoreCase));
    }
}
