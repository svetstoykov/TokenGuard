using System.Diagnostics;
using System.Text.Json;
using Codexplorer.Configuration;
using Codexplorer.Sessions;
using Codexplorer.Tools;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.OpenAI;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Agent;

/// <summary>
/// Implements one long-lived interactive Codexplorer session for one workspace.
/// </summary>
/// <remarks>
/// This session keeps TokenGuard state, transcript logging, and renderer output alive for the entire repo conversation.
/// Each submitted user message reuses the same conversation context so later turns can build on earlier exploration.
/// </remarks>
internal sealed class ExplorerSession : IExplorerSession
{
    private readonly WorkspaceModel _workspace;
    private readonly IConversationContext _conversationContext;
    private readonly ISessionLogger _sessionLogger;
    private readonly Task _rendererTask;
    private readonly IToolRegistry _toolRegistry;
    private readonly Lazy<ChatClient> _chatClient;
    private readonly IReadOnlyList<ChatTool> _chatTools;
    private readonly AgentOptions _agentOptions;
    private readonly ModelOptions _modelOptions;

    private bool _sessionClosed;
    private int _exchangeIndex;
    private int _totalTurns;
    private int _totalTokens;
    private string? _lastAssistantText;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplorerSession"/> class.
    /// </summary>
    /// <param name="workspace">The workspace being explored.</param>
    /// <param name="conversationContext">The long-lived TokenGuard conversation context.</param>
    /// <param name="sessionLogger">The session logger that persists the full transcript.</param>
    /// <param name="rendererTask">The live renderer task for the session event stream.</param>
    /// <param name="toolRegistry">The workspace tool registry.</param>
    /// <param name="chatClient">The deferred model client used for completions.</param>
    /// <param name="chatTools">The published tool definitions for model calls.</param>
    /// <param name="agentOptions">The configured agent options.</param>
    /// <param name="modelOptions">The configured model options.</param>
    public ExplorerSession(
        WorkspaceModel workspace,
        IConversationContext conversationContext,
        ISessionLogger sessionLogger,
        Task rendererTask,
        IToolRegistry toolRegistry,
        Lazy<ChatClient> chatClient,
        IReadOnlyList<ChatTool> chatTools,
        AgentOptions agentOptions,
        ModelOptions modelOptions)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(conversationContext);
        ArgumentNullException.ThrowIfNull(sessionLogger);
        ArgumentNullException.ThrowIfNull(rendererTask);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(chatTools);
        ArgumentNullException.ThrowIfNull(agentOptions);
        ArgumentNullException.ThrowIfNull(modelOptions);

        this._workspace = workspace;
        this._conversationContext = conversationContext;
        this._sessionLogger = sessionLogger;
        this._rendererTask = rendererTask;
        this._toolRegistry = toolRegistry;
        this._chatClient = chatClient;
        this._chatTools = chatTools;
        this._agentOptions = agentOptions;
        this._modelOptions = modelOptions;
    }

    /// <inheritdoc />
    public string LogFilePath => this._sessionLogger.LogFilePath;

    /// <inheritdoc />
    public async Task<AgentExchangeResult> SubmitAsync(string userMessage, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        this.ThrowIfSessionClosed();

        var currentExchangeIndex = this._exchangeIndex;
        this._exchangeIndex++;

        this._conversationContext.AddUserMessage(userMessage);
        await this._sessionLogger.AppendAsync(
                new UserPromptEvent(DateTime.UtcNow, currentExchangeIndex, userMessage),
                CancellationToken.None)
            .ConfigureAwait(false);

        var turnsAtExchangeStart = this._totalTurns;
        var tokensAtExchangeStart = this._totalTokens;
        string? lastAssistantTextThisExchange = null;

        try
        {
            for (var exchangeTurnIndex = 0; exchangeTurnIndex < this._agentOptions.MaxTurns; exchangeTurnIndex++)
            {
                if (ct.IsCancellationRequested)
                {
                    return await this.CancelSessionAsync(turnsAtExchangeStart).ConfigureAwait(false);
                }

                var globalTurnIndex = this._totalTurns;
                var prepareResult = await this._conversationContext.PrepareAsync(ct).ConfigureAwait(false);

                await this._sessionLogger.AppendAsync(
                        new PreparedContextEvent(DateTime.UtcNow, globalTurnIndex, prepareResult),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (prepareResult.Outcome is PrepareOutcome.Degraded or PrepareOutcome.ContextExhausted)
                {
                    var reason = prepareResult.DegradationReason
                                 ?? (prepareResult.Outcome == PrepareOutcome.ContextExhausted
                                     ? "Context budget exhausted after compaction; a prepared request could no longer fit."
                                     : "Context budget degraded after compaction; the prepared request may exceed provider limits.");

                    if (prepareResult.Outcome == PrepareOutcome.Degraded)
                    {
                        await this._sessionLogger.AppendAsync(
                                new ExchangeOutcomeEvent(DateTime.UtcNow, currentExchangeIndex, "DegradedWarning", reason),
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await this._sessionLogger.AppendAsync(
                                new ExchangeOutcomeEvent(DateTime.UtcNow, currentExchangeIndex, "ContextExhausted", reason),
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        await this.EndSessionAsync($"ContextExhausted: {reason}", CancellationToken.None).ConfigureAwait(false);

                        return new AgentExchangeDegraded(
                            reason,
                            lastAssistantTextThisExchange ?? this._lastAssistantText,
                            this._totalTurns - turnsAtExchangeStart);
                    }
                }

                await this._sessionLogger.AppendAsync(
                        new ModelRequestedEvent(DateTime.UtcNow, globalTurnIndex, prepareResult.Messages),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                var completion = (await this._chatClient.Value.CompleteChatAsync(
                            prepareResult.Messages.ForOpenAI(),
                            ExplorerAgent.CreateChatCompletionOptions(this._chatTools, this._modelOptions.MaxOutputTokens),
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
                    this._lastAssistantText = assistantText;
                    lastAssistantTextThisExchange = assistantText;
                }

                var toolCalls = completion.ToolCalls
                    .Select(call => new SessionToolCall(call.Id, call.FunctionName, call.FunctionArguments.ToString()))
                    .ToArray();

                this._totalTurns++;
                this._totalTokens += completion.Usage?.TotalTokenCount ?? 0;

                await this._sessionLogger.AppendAsync(
                        new ModelRespondedEvent(
                            DateTime.UtcNow,
                            globalTurnIndex,
                            assistantText,
                            toolCalls,
                            completion.Usage?.InputTokenCount,
                            completion.Usage?.OutputTokenCount,
                            completion.Usage?.TotalTokenCount),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                this._conversationContext.RecordModelResponse(completion.ResponseSegments(), completion.InputTokens());

                if (toolCalls.Length == 0)
                {
                    await this._sessionLogger.AppendAsync(
                            new AssistantReplyEvent(DateTime.UtcNow, currentExchangeIndex, assistantText),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    await this._sessionLogger.AppendAsync(
                            new ExchangeOutcomeEvent(DateTime.UtcNow, currentExchangeIndex, "Succeeded", null),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    return new AgentReplyReceived(
                        assistantText,
                        this._totalTurns - turnsAtExchangeStart,
                        this._totalTokens - tokensAtExchangeStart);
                }

                foreach (var toolCall in toolCalls)
                {
                    await this._sessionLogger.AppendAsync(
                            new ToolCalledEvent(DateTime.UtcNow, toolCall.ToolName, toolCall.ArgumentsJson),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    var stopwatch = Stopwatch.StartNew();
                    var toolResult = await this._toolRegistry.ExecuteAsync(
                            toolCall.ToolName,
                            ExplorerAgent.ParseArguments(toolCall.ArgumentsJson),
                            this._workspace,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    stopwatch.Stop();

                    this._conversationContext.RecordToolResult(toolCall.ToolCallId, toolCall.ToolName, toolResult);

                    await this._sessionLogger.AppendAsync(
                            new ToolCompletedEvent(DateTime.UtcNow, toolCall.ToolName, toolResult, stopwatch.Elapsed),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            var capMessage = $"The assistant hit the configured per-message turn cap of {this._agentOptions.MaxTurns} before producing a reply.";
            await this._sessionLogger.AppendAsync(
                    new ExchangeOutcomeEvent(DateTime.UtcNow, currentExchangeIndex, "MaxTurnsReached", capMessage),
                    CancellationToken.None)
                .ConfigureAwait(false);

            return new AgentExchangeMaxTurnsReached(
                lastAssistantTextThisExchange ?? this._lastAssistantText,
                this._totalTurns - turnsAtExchangeStart);
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is OperationCanceledException)
        {
            return await this.CancelSessionAsync(turnsAtExchangeStart).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this.FailSessionAsync(ex, CancellationToken.None).ConfigureAwait(false);
            return new AgentExchangeFailed(ex, this._totalTurns - turnsAtExchangeStart);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!this._sessionClosed)
        {
            await this.EndSessionAsync("EndedByUser", CancellationToken.None).ConfigureAwait(false);
        }

        this._conversationContext.Dispose();
        await this._sessionLogger.DisposeAsync().ConfigureAwait(false);
        await this._rendererTask.ConfigureAwait(false);
    }

    private async Task<AgentExchangeCancelled> CancelSessionAsync(int turnsAtExchangeStart)
    {
        await this._sessionLogger.AppendAsync(
                new SessionCancelledEvent(DateTime.UtcNow, this._totalTurns, "Cancellation requested for the active session."),
                CancellationToken.None)
            .ConfigureAwait(false);
        this._sessionClosed = true;

        return new AgentExchangeCancelled(this._lastAssistantText, this._totalTurns - turnsAtExchangeStart);
    }

    private async Task EndSessionAsync(string terminalOutcome, CancellationToken ct)
    {
        if (this._sessionClosed)
        {
            return;
        }

        await this._sessionLogger.EndAsync(
                new SessionEndedEvent(DateTime.UtcNow, this._totalTurns, this._totalTokens, terminalOutcome),
                ct)
            .ConfigureAwait(false);
        this._sessionClosed = true;
    }

    private async Task FailSessionAsync(Exception ex, CancellationToken ct)
    {
        if (this._sessionClosed)
        {
            return;
        }

        await this._sessionLogger.AppendAsync(new SessionFailedEvent(DateTime.UtcNow, ex), ct).ConfigureAwait(false);
        this._sessionClosed = true;
    }

    private void ThrowIfSessionClosed()
    {
        if (this._sessionClosed)
        {
            throw new InvalidOperationException("The explorer session is already closed.");
        }
    }
}
