using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Samples.Console.AgentLoops.Providers;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops;

using Console = System.Console;

/// <summary>
/// Executes a predefined <see cref="AgentLoopTaskDefinition"/> with TokenGuard context management.
/// </summary>
/// <remarks>
/// Seeds the workspace, runs the task using the selected provider, then asserts the outcome.
/// All context tunables are passed in at call time so they remain visible and editable at the
/// call site in <c>Program.cs</c>.
/// </remarks>
public sealed class AgentLoop
{
    private readonly AgentLoopTaskDefinition _task;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentLoop"/> class.
    /// </summary>
    /// <param name="task">The task definition to execute.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is null.</exception>
    public AgentLoop(AgentLoopTaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(task);
        this._task = task;
    }

    /// <summary>
    /// Executes the task using the specified provider and context configuration.
    /// </summary>
    /// <param name="options">Provider and runtime options.</param>
    /// <param name="maxTokens">Maximum context budget in tokens.</param>
    /// <param name="compactionThreshold">Fraction of <paramref name="maxTokens"/> that triggers compaction.</param>
    /// <param name="emergencyThreshold">Fraction of <paramref name="maxTokens"/> at which emergency truncation fires.</param>
    /// <param name="protectedWindowFraction">Fraction of the window that the sliding strategy keeps protected.</param>
    /// <param name="maxIterations">Maximum turns allowed before stopping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(
        AgentLoopOptions options,
        int maxTokens,
        double compactionThreshold,
        double emergencyThreshold,
        double protectedWindowFraction,
        int maxIterations,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var workspaceDirectory = Path.Combine(Path.GetTempPath(), $"tokenguard-task-{this._task.Name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceDirectory);

        try
        {
            using var logger = new SessionLogger();

            logger.LogBudgetInfo(new ContextBudget(maxTokens, compactionThreshold, emergencyThreshold, 0), nameof(SlidingWindowStrategy));

            var configuration = BuildConfiguration();
            var provider = CreateProvider(options, configuration);

            var services = new ServiceCollection();
            services.AddConversationContext(this._task.ConversationName, builder => builder
                .WithMaxTokens(maxTokens)
                .WithStrategy(new SlidingWindowStrategy(new SlidingWindowOptions(protectedWindowFraction: protectedWindowFraction)))
                .WithCompactionThreshold(compactionThreshold)
                .WithEmergencyThreshold(emergencyThreshold));

            await using var serviceProvider = services.BuildServiceProvider();
            using var conversationContext = serviceProvider
                .GetRequiredService<IConversationContextFactory>()
                .Create(this._task.ConversationName);

            Console.WriteLine("=========================================");
            Console.WriteLine("   TokenGuard Task-Based Agent Loop");
            Console.WriteLine("=========================================\n");
            Console.WriteLine($"Task: {this._task.Name}");
            Console.WriteLine($"Provider: {provider.Name}");
            Console.WriteLine($"Model: {provider.ModelId}");
            Console.WriteLine($"Workspace: {workspaceDirectory}\n");

            await this._task.SeedWorkspaceAsync(workspaceDirectory);
            logger.LogSystemInfo($"Workspace seeded: {workspaceDirectory}");

            var tools = CreateTools(workspaceDirectory);
            var toolMap = tools.ToDictionary(t => t.Name, t => t);

            conversationContext.SetSystemPrompt(this._task.SystemPrompt);
            logger.LogMessageAdded(conversationContext.History.First(), "System Prompt Set");

            conversationContext.AddUserMessage(this._task.UserMessage);
            logger.LogMessageAdded(conversationContext.History.Last(), "User Task Loaded");

            var totalCompactionCount = 0;
            var completed = false;
            string? finalResponseText = null;

            for (var turn = 1; turn <= maxIterations && !completed; turn++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogHistoryBeforePrepare(conversationContext.History);

                var prepareResult = await conversationContext.PrepareAsync(cancellationToken);
                var preparedMessages = prepareResult.Messages;
                logger.LogHistoryAfterPrepare(preparedMessages);

                var compacted = preparedMessages.Any(m => m.State != CompactionState.Original);
                if (compacted)
                {
                    totalCompactionCount++;
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[TokenGuard.Core: Turn {turn}, Prepared {preparedMessages.Count} messages, Compacted={compacted}]");
                Console.ResetColor();

                ProviderTurnResult turnResult;
                try
                {
                    turnResult = await provider.ExecuteTurnAsync(preparedMessages, tools, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError($"{provider.Name} turn execution", ex);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"API Error: {ex.Message}");
                    Console.ResetColor();
                    break;
                }

                DisplayResponse(turnResult);

                if (turnResult.HasToolCalls)
                {
                    conversationContext.RecordModelResponse(turnResult.ResponseSegments, turnResult.InputTokens);
                    logger.LogModelResponse(
                        conversationContext.History.Last(),
                        turnResult.InputTokens,
                        $"Tool Calls [{string.Join(", ", turnResult.ToolCalls.Select(call => call.ToolName))}]");

                    foreach (var call in turnResult.ToolCalls)
                    {
                        var resultText = toolMap.TryGetValue(call.ToolName, out var tool)
                            ? tool.Execute(call.ArgumentsJson)
                            : "Error: Unknown tool.";

                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.WriteLine($"[Tool Result: {resultText.Length} chars]");
                        Console.ResetColor();

                        conversationContext.RecordToolResult(call.ToolCallId, call.ToolName, resultText);
                        logger.LogToolResultRecorded(conversationContext.History.Last());
                    }

                    continue;
                }

                conversationContext.RecordModelResponse(turnResult.ResponseSegments, turnResult.InputTokens);
                logger.LogModelResponse(
                    conversationContext.History.Last(),
                    turnResult.InputTokens,
                    "Text Response");

                finalResponseText = string.Join(
                    Environment.NewLine,
                    turnResult.ResponseSegments.OfType<TokenGuard.Core.Models.Content.TextContent>().Select(s => s.Content));

                if (finalResponseText.Contains(this._task.CompletionMarker, StringComparison.Ordinal))
                {
                    completed = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Completion marker '{this._task.CompletionMarker}' detected]");
                    Console.ResetColor();
                }
                else
                {
                    conversationContext.AddUserMessage(
                        "The task is not complete yet. Continue using tools until all required output files are correctly written. " +
                        $"Only the final completion message may contain {this._task.CompletionMarker}.");
                    logger.LogMessageAdded(conversationContext.History.Last(), "Continuation Prompt Added");
                }
            }

            var duration = DateTime.Now - startTime;
            var finalTokenCount = conversationContext.History.Sum(m => m.TokenCount ?? 0);
            logger.LogSessionSummary(conversationContext.History.Count, totalCompactionCount, duration, finalTokenCount);

            if (completed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nTask completed successfully. Running assertions...");
                Console.ResetColor();

                try
                {
                    await this._task.AssertOutcomeAsync(workspaceDirectory, finalResponseText);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("All assertions passed.");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Assertion failed: {ex.Message}");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nTask did not complete within {maxIterations} iterations.");
                Console.ResetColor();
            }

            Console.WriteLine($"\nSession complete. Log file: {logger.LogFilePath}");
            Console.WriteLine($"Workspace: {workspaceDirectory}");
        }
        finally
        {
            // Uncomment to clean up workspace after run
            Directory.Delete(workspaceDirectory, recursive: true);
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static IAgentLoopProvider CreateProvider(AgentLoopOptions options, IConfiguration configuration)
        => options.Provider switch
        {
            ProviderKind.OpenRouter => new OpenRouterAgentLoopProvider(configuration, options),
            ProviderKind.Anthropic => new AnthropicAgentLoopProvider(configuration, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported provider."),
        };

    private static void DisplayResponse(ProviderTurnResult turnResult)
    {
        foreach (var text in turnResult.ResponseSegments.OfType<TokenGuard.Core.Models.Content.TextContent>())
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Agent: {text.Content}\n");
            Console.ResetColor();
        }

        foreach (var call in turnResult.ToolCalls)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Agent calls tool: {call.ToolName}({call.ArgumentsJson})]");
            Console.ResetColor();
        }
    }

    private static ITool[] CreateTools(string workspaceDirectory) =>
    [
        new ListFilesTool(workspaceDirectory),
        new ReadFileTool(workspaceDirectory),
        new CreateTextFileTool(workspaceDirectory),
        new EditTextFileTool(workspaceDirectory),
    ];
}
