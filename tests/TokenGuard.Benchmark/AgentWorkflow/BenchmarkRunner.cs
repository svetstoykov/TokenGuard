using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using TokenGuard.Benchmark.AgentWorkflow.Models;
using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Benchmark.Helpers;
using TokenGuard.Benchmark.Reporting;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Extensions.OpenAI;

namespace TokenGuard.Benchmark.AgentWorkflow;

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

    private readonly JsonReportWriter reportWriter = new();

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
        using var workspace = TestWorkspace.CreateInBaseDirectory($"{task.Name}-{configuration.Name}");
        await task.SeedWorkspaceAsync(workspace.DirectoryPath);

        var tools = OpenRouterE2ETestSupport.CreateTools(workspace.DirectoryPath);
        var parameters = new ExecutionParameters(
            Task: task,
            Configuration: configuration,
            ChatClient: OpenRouterE2ETestSupport.CreateChatClient(ModelName),
            ChatOptions: OpenRouterE2ETestSupport.CreateChatOptions(tools),
            ToolMap: tools.ToDictionary(static tool => tool.Name, static tool => tool, StringComparer.Ordinal),
            Turns: [],
            WorkspaceDirectory: workspace.DirectoryPath);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var compactionEvents = 0;
        var completed = false;
        var runId = $"{task.Name}-{configuration.Name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}";
        string? finalResponseText = null;
        string? failureReason = null;

        Action<TurnTelemetry> onTurnCompleted = telemetry =>
        {
            totalInputTokens += telemetry.InputTokens ?? 0;
            totalOutputTokens += telemetry.OutputTokens ?? 0;
            if (telemetry.Compacted) compactionEvents++;
        };
        Action<string> onCompleted = responseText => { completed = true; finalResponseText = responseText; };
        Action<string> onFailure = reason => failureReason = reason;

        Console.WriteLine($"[{configuration.Name}] task={task.Name} model={ModelName}");

        try
        {
            var execute = configuration.Mode switch
            {
                BenchmarkMode.Raw => ExecuteRawAsync(parameters, onTurnCompleted, onCompleted, onFailure, cancellationToken),
                BenchmarkMode.SlidingWindow => ExecuteManagedAsync(parameters, onTurnCompleted, onCompleted, onFailure, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(configuration.Mode), configuration.Mode, "Unsupported benchmark mode."),
            };

            await execute;

            if (completed)
            {
                await task.AssertOutcomeAsync(workspace.DirectoryPath, finalResponseText);
            }
        }
        catch (Exception ex) when (configuration.Mode == BenchmarkMode.Raw && IsContextLengthError(ex))
        {
            completed = false;
            failureReason = await this.PersistFailureAsync(
                task,
                configuration,
                parameters,
                totalInputTokens,
                totalOutputTokens,
                compactionEvents,
                runId,
                ex,
                cancellationToken);

            Console.WriteLine($"[{configuration.Name}] context window exceeded; {failureReason}");
        }
        catch (Exception ex)
        {
            completed = false;
            failureReason = await this.PersistFailureAsync(
                task,
                configuration,
                parameters,
                totalInputTokens,
                totalOutputTokens,
                compactionEvents,
                runId,
                ex,
                cancellationToken);

            Console.WriteLine($"[{configuration.Name}] failed: {failureReason}");
        }

        stopwatch.Stop();

        return new RunResult(
            configuration.Name,
            completed,
            parameters.Turns.Count,
            totalInputTokens,
            totalOutputTokens,
            compactionEvents,
            stopwatch.ElapsedMilliseconds,
            parameters.Turns,
            failureReason);
    }

    private static async Task ExecuteRawAsync(
        ExecutionParameters executionParameters,
        Action<TurnTelemetry> onTurnCompleted,
        Action<string> onCompleted,
        Action<string> onFailure,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new SystemChatMessage(executionParameters.Task.SystemPrompt),
            new UserChatMessage(executionParameters.Task.UserMessage),
        ];

        for (var turn = 1; turn <= executionParameters.Configuration.MaxIterations; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var turnStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var completion = (await executionParameters.ChatClient.CompleteChatAsync(messages, executionParameters.ChatOptions, cancellationToken)).Value;
            var inputTokens = completion.InputTokens();
            var outputTokens = completion.Usage?.OutputTokenCount;
            var cumulativeInputTokens = (executionParameters.Turns.LastOrDefault()?.CumulativeInputTokens ?? 0) + (inputTokens ?? 0);

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

            executionParameters.Turns.Add(telemetry);
            onTurnCompleted(telemetry);

            Console.WriteLine($"[{executionParameters.Configuration.Name}] turn={turn} finish={completion.FinishReason} input={inputTokens} output={outputTokens}");

            AppendModelMessage(messages, completion);

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var call in completion.ToolCalls)
                {
                    var resultText = executionParameters.ToolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    messages.Add(new ToolChatMessage(call.Id, resultText));
                }

                continue;
            }

            var finalResponseText = string.Join(Environment.NewLine, completion.TextSegments().Select(static segment => segment.Content));
            if (finalResponseText.Contains(executionParameters.Task.CompletionMarker, StringComparison.Ordinal))
            {
                onCompleted(finalResponseText);
                return;
            }

            messages.Add(new UserChatMessage(
                "The task is not complete yet. Continue using tools until all required output files are correctly written. " +
                $"Only the final completion message may contain {executionParameters.Task.CompletionMarker}."));
        }

        onFailure($"Maximum iteration budget reached for {executionParameters.Configuration.Name} in workspace {executionParameters.WorkspaceDirectory}.");
    }

    private static async Task ExecuteManagedAsync(
        ExecutionParameters p,
        Action<TurnTelemetry> onTurnCompleted,
        Action<string> onCompleted,
        Action<string> onFailure,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddConversationContext(p.Task.ConversationName, builder => builder
            .WithMaxTokens(p.Configuration.MaxTokens ?? 16_000)
            .WithStrategy(new SlidingWindowStrategy(new SlidingWindowOptions(protectedWindowFraction: 0.2)))
            .WithCompactionThreshold(p.Configuration.CompactionThreshold ?? 0.80));

        await using var serviceProvider = services.BuildServiceProvider();
        using var conversationContext = serviceProvider
            .GetRequiredService<IConversationContextFactory>()
            .Create(p.Task.ConversationName);

        conversationContext.SetSystemPrompt(p.Task.SystemPrompt);
        conversationContext.AddUserMessage(p.Task.UserMessage);

        for (var turn = 1; turn <= p.Configuration.MaxIterations; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prepareResult = await conversationContext.PrepareAsync(cancellationToken);
            var preparedMessages = prepareResult.Messages;
            var maskedCount = preparedMessages.Count(static message => message.State == CompactionState.Masked);
            var compacted = maskedCount > 0;

            var turnStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var openAiMessage = preparedMessages.ForOpenAI();
            var resultFromChat = await p.ChatClient.CompleteChatAsync(openAiMessage, p.ChatOptions, cancellationToken);
            
            var completion = resultFromChat.Value;
            
            turnStopwatch.Stop();

            var inputTokens = completion.InputTokens();
            var outputTokens = completion.Usage?.OutputTokenCount;
            var cumulativeInputTokens = (p.Turns.LastOrDefault()?.CumulativeInputTokens ?? 0) + (inputTokens ?? 0);

            var telemetry = new TurnTelemetry(
                turn,
                inputTokens,
                outputTokens,
                cumulativeInputTokens,
                compacted,
                maskedCount,
                turnStopwatch.ElapsedMilliseconds,
                completion.FinishReason.ToString());

            p.Turns.Add(telemetry);
            onTurnCompleted(telemetry);

            Console.WriteLine($"[{p.Configuration.Name}] turn={turn} finish={completion.FinishReason} input={inputTokens} output={outputTokens} compacted={compacted} masked={maskedCount}");

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                conversationContext.RecordModelResponse(completion.ResponseSegments(), inputTokens);

                foreach (var call in completion.ToolCalls)
                {
                    var resultText = p.ToolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    conversationContext.RecordToolResult(call.Id, call.FunctionName, resultText);
                }

                continue;
            }

            var textSegments = completion.TextSegments();
            conversationContext.RecordModelResponse(textSegments, inputTokens);
            var finalResponseText = string.Join(Environment.NewLine, textSegments.Select(static segment => segment.Content));

            if (finalResponseText.Contains(p.Task.CompletionMarker, StringComparison.Ordinal))
            {
                onCompleted(finalResponseText);
                return;
            }

            conversationContext.AddUserMessage(
                "The task is not complete yet. Continue using tools until all required output files are correctly written. " +
                $"Only the final completion message may contain {p.Task.CompletionMarker}.");
        }

        onFailure($"Maximum iteration budget reached for {p.Configuration.Name} in workspace {p.WorkspaceDirectory}.");
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

    private static BenchmarkComparison? BuildComparison(IReadOnlyList<RunResult> runs)
    {
        var raw = runs.SingleOrDefault(run => run.Configuration == BenchmarkConfiguration.Raw.Name);
        var managed = runs.SingleOrDefault(run => run.Configuration == BenchmarkConfiguration.SlidingWindow.Name);

        if (raw is null || managed is null)
        {
            return null;
        }

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

    private Task<string> PersistFailureAsync(
        AgentLoopTaskDefinition task,
        BenchmarkConfiguration configuration,
        ExecutionParameters parameters,
        int totalInputTokens,
        int totalOutputTokens,
        int compactionEvents,
        string runId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return this.reportWriter.WriteFailureAsync(
            task.Name,
            configuration,
            ModelName,
            parameters.WorkspaceDirectory,
            runId,
            parameters.Turns.Count,
            totalInputTokens,
            totalOutputTokens,
            compactionEvents,
            parameters.Turns.LastOrDefault(),
            exception);
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
