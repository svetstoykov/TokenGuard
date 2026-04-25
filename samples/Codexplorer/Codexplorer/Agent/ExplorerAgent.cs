using System.Diagnostics;
using System.Text.Json;
using Codexplorer.Configuration;
using Codexplorer.Sessions;
using Codexplorer.Tools;
using OpenAI;
using OpenAI.Chat;
using Microsoft.Extensions.Options;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.OpenAI;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Agent;

/// <summary>
/// Runs the Codexplorer repository agent loop over TokenGuard and OpenRouter.
/// </summary>
/// <remarks>
/// This service owns the full control loop: it prepares context with TokenGuard, sends requests to OpenRouter, executes
/// tool calls serially inside the current workspace, records every relevant event to the session transcript, and turns
/// terminal outcomes into stable result records for the caller.
/// </remarks>
public sealed class ExplorerAgent : IExplorerAgent
{
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    private readonly ChatClient _chatClient;
    private readonly IConversationContextFactory _conversationContextFactory;
    private readonly ISessionLoggerFactory _sessionLoggerFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly CodexplorerOptions _options;
    private readonly IReadOnlyList<ChatTool> _chatTools;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplorerAgent"/> class.
    /// </summary>
    /// <param name="conversationContextFactory">Factory for fresh TokenGuard conversation contexts.</param>
    /// <param name="toolRegistry">Registry for workspace-scoped read-only tools.</param>
    /// <param name="sessionLoggerFactory">Factory for per-query session transcripts.</param>
    /// <param name="options">The validated Codexplorer options snapshot.</param>
    public ExplorerAgent(
        IConversationContextFactory conversationContextFactory,
        IToolRegistry toolRegistry,
        ISessionLoggerFactory sessionLoggerFactory,
        IOptions<CodexplorerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(conversationContextFactory);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(sessionLoggerFactory);
        ArgumentNullException.ThrowIfNull(options);

        this._conversationContextFactory = conversationContextFactory;
        this._toolRegistry = toolRegistry;
        this._sessionLoggerFactory = sessionLoggerFactory;
        this._options = options.Value;
        var openRouterOptions = this._options.OpenRouter
            ?? throw new InvalidOperationException("Codexplorer OpenRouter options are not configured.");
        var modelOptions = this._options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");
        var apiKey = openRouterOptions.ApiKey
            ?? throw new InvalidOperationException("Codexplorer OpenRouter API key is not configured.");
        var modelName = modelOptions.Name
            ?? throw new InvalidOperationException("Codexplorer model name is not configured.");

        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = OpenRouterEndpoint });

        this._chatClient = client.GetChatClient(modelName);
        this._chatTools = this._toolRegistry.GetSchemas()
            .Select(CreateChatTool)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<AgentRunResult> RunAsync(WorkspaceModel workspace, string userQuery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        ISessionLogger? sessionLogger = null;
        IConversationContext? conversationContext = null;
        var totalTurns = 0;
        var totalTokens = 0;
        string? lastAssistantText = null;
        var agentOptions = this._options.Agent
            ?? throw new InvalidOperationException("Codexplorer agent options are not configured.");
        var modelOptions = this._options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");

        try
        {
            sessionLogger = this._sessionLoggerFactory.BeginSession(workspace, userQuery);
            conversationContext = this._conversationContextFactory.Create();

            conversationContext.SetSystemPrompt(SystemPrompt.Text);
            conversationContext.AddUserMessage(userQuery);

            for (var turnIndex = 0; turnIndex < agentOptions.MaxTurns; turnIndex++)
            {
                if (ct.IsCancellationRequested)
                {
                    return await CancelAsync(sessionLogger, lastAssistantText, totalTurns, turnIndex).ConfigureAwait(false);
                }

                var prepareResult = await conversationContext.PrepareAsync().ConfigureAwait(false);
                await sessionLogger.AppendAsync(
                        new PreparedContextEvent(DateTime.UtcNow, turnIndex, prepareResult),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (prepareResult.Outcome == PrepareOutcome.ContextExhausted)
                {
                    var reason = prepareResult.DegradationReason
                        ?? "Context budget exhausted after compaction; a prepared request could no longer fit.";

                    await sessionLogger.EndAsync(
                            new SessionEndedEvent(DateTime.UtcNow, totalTurns, totalTokens, $"Degraded: {reason}"),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    return new AgentDegraded(reason, lastAssistantText, totalTurns);
                }

                await sessionLogger.AppendAsync(
                        new ModelRequestedEvent(DateTime.UtcNow, turnIndex, prepareResult.Messages),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                var completion = (await this._chatClient.CompleteChatAsync(
                        prepareResult.Messages.ForOpenAI(),
                        CreateChatCompletionOptions(this._chatTools, modelOptions.MaxOutputTokens),
                        CancellationToken.None)
                    .ConfigureAwait(false))
                    .Value;

                var assistantText = string.Join(
                    Environment.NewLine,
                    completion.TextSegments()
                        .Select(segment => segment.Content)
                        .Where(static content => !string.IsNullOrWhiteSpace(content)));

                if (!string.IsNullOrWhiteSpace(assistantText))
                {
                    lastAssistantText = assistantText;
                }

                var toolCalls = completion.ToolCalls
                    .Select(call => new SessionToolCall(call.Id, call.FunctionName, call.FunctionArguments.ToString()))
                    .ToArray();

                totalTurns = turnIndex + 1;
                totalTokens += completion.Usage?.TotalTokenCount ?? 0;

                await sessionLogger.AppendAsync(
                        new ModelRespondedEvent(
                            DateTime.UtcNow,
                            turnIndex,
                            assistantText,
                            toolCalls,
                            completion.Usage?.InputTokenCount,
                            completion.Usage?.OutputTokenCount,
                            completion.Usage?.TotalTokenCount),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                conversationContext.RecordModelResponse(completion.ResponseSegments(), completion.InputTokens());

                if (toolCalls.Length == 0)
                {
                    await sessionLogger.AppendAsync(
                            new FinalAnswerEvent(DateTime.UtcNow, assistantText),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    await sessionLogger.EndAsync(
                            new SessionEndedEvent(DateTime.UtcNow, totalTurns, totalTokens, "Succeeded"),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    return new AgentSucceeded(assistantText, totalTurns, totalTokens);
                }

                foreach (var toolCall in toolCalls)
                {
                    await sessionLogger.AppendAsync(
                            new ToolCalledEvent(DateTime.UtcNow, toolCall.ToolName, toolCall.ArgumentsJson),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    var stopwatch = Stopwatch.StartNew();
                    var toolResult = await this._toolRegistry.ExecuteAsync(
                            toolCall.ToolName,
                            ParseArguments(toolCall.ArgumentsJson),
                            workspace,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    stopwatch.Stop();

                    conversationContext.RecordToolResult(toolCall.ToolCallId, toolCall.ToolName, toolResult);

                    await sessionLogger.AppendAsync(
                            new ToolCompletedEvent(DateTime.UtcNow, toolCall.ToolName, toolResult, stopwatch.Elapsed),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            await sessionLogger.EndAsync(
                    new SessionEndedEvent(DateTime.UtcNow, totalTurns, totalTokens, "MaxTurnsReached"),
                    CancellationToken.None)
                .ConfigureAwait(false);

            return new AgentMaxTurnsReached(lastAssistantText, totalTurns);
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is OperationCanceledException)
        {
            if (sessionLogger is not null)
            {
                return await CancelAsync(sessionLogger, lastAssistantText, totalTurns, totalTurns).ConfigureAwait(false);
            }

            return new AgentCancelled(lastAssistantText, totalTurns);
        }
        catch (Exception ex)
        {
            if (sessionLogger is not null)
            {
                await sessionLogger.AppendAsync(new SessionFailedEvent(DateTime.UtcNow, ex), CancellationToken.None).ConfigureAwait(false);
            }

            return new AgentFailed(ex, totalTurns);
        }
        finally
        {
            conversationContext?.Dispose();

            if (sessionLogger is not null)
            {
                await sessionLogger.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static ChatCompletionOptions CreateChatCompletionOptions(IReadOnlyList<ChatTool> chatTools, int maxOutputTokens)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxOutputTokens,
            ToolChoice = ChatToolChoice.CreateAutoChoice(),
            AllowParallelToolCalls = false
        };

        foreach (var chatTool in chatTools)
        {
            options.Tools.Add(chatTool);
        }

        return options;
    }

    private static ChatTool CreateChatTool(ToolSchema schema)
    {
        return ChatTool.CreateFunctionTool(
            schema.Function.Name,
            schema.Function.Description,
            BinaryData.FromString(schema.Function.Parameters.GetRawText()),
            functionSchemaIsStrict: false);
    }

    private static JsonElement ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

    private static async Task<AgentCancelled> CancelAsync(
        ISessionLogger sessionLogger,
        string? lastAssistantText,
        int totalTurns,
        int turnIndex)
    {
        await sessionLogger.AppendAsync(
                new SessionCancelledEvent(DateTime.UtcNow, turnIndex, "Cancellation requested between turns."),
                CancellationToken.None)
            .ConfigureAwait(false);

        return new AgentCancelled(lastAssistantText, totalTurns);
    }
}
