using Codexplorer.Configuration;
using TokenGuard.Core.Models;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Represents one ordered event in a Codexplorer session transcript.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the event occurred.</param>
public abstract record SessionEvent(DateTime TimestampUtc);

/// <summary>
/// Captures session metadata written at transcript creation time.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the session began.</param>
/// <param name="Workspace">The workspace targeted by the query.</param>
/// <param name="SessionLabel">The human-readable session label.</param>
/// <param name="Budget">The configured Codexplorer budget settings.</param>
/// <param name="ModelName">The configured model identifier.</param>
public sealed record SessionStartedEvent(
    DateTime TimestampUtc,
    WorkspaceModel Workspace,
    string SessionLabel,
    BudgetOptions Budget,
    string ModelName) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures one user message submitted during a live session.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the user message entered the session.</param>
/// <param name="ExchangeIndex">The zero-based index of the user-message exchange inside the session.</param>
/// <param name="Content">The raw user message content.</param>
public sealed record UserPromptEvent(
    DateTime TimestampUtc,
    int ExchangeIndex,
    string Content) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures the diagnostics returned by one TokenGuard <see cref="PrepareResult"/>.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when preparation completed.</param>
/// <param name="TurnIndex">The zero-based turn index being prepared.</param>
/// <param name="TokensBeforeCompaction">The token total before compaction ran.</param>
/// <param name="TokensAfterCompaction">The token total after compaction finished.</param>
/// <param name="Outcome">The preparation outcome name.</param>
/// <param name="MessagesCompacted">The number of messages compacted during preparation.</param>
/// <param name="DegradationReason">The degradation reason when present.</param>
public sealed record PreparedContextEvent(
    DateTime TimestampUtc,
    int TurnIndex,
    int TokensBeforeCompaction,
    int TokensAfterCompaction,
    string Outcome,
    int MessagesCompacted,
    string? DegradationReason) : SessionEvent(TimestampUtc)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreparedContextEvent"/> record from a <see cref="PrepareResult"/>.
    /// </summary>
    /// <param name="timestampUtc">The UTC timestamp when preparation completed.</param>
    /// <param name="turnIndex">The zero-based turn index being prepared.</param>
    /// <param name="result">The TokenGuard preparation result to flatten into session diagnostics.</param>
    public PreparedContextEvent(DateTime timestampUtc, int turnIndex, PrepareResult result)
        : this(
            timestampUtc,
            turnIndex,
            result?.TokensBeforeCompaction ?? throw new ArgumentNullException(nameof(result)),
            result.TokensAfterCompaction,
            result.Outcome.ToString(),
            result.MessagesCompacted,
            result.DegradationReason)
    {
    }
}

/// <summary>
/// Captures the outbound provider request immediately before the model call.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the request was issued.</param>
/// <param name="TurnIndex">The zero-based turn index being sent.</param>
/// <param name="OutboundMessages">The full prepared message payload sent to the model.</param>
public sealed record ModelRequestedEvent(
    DateTime TimestampUtc,
    int TurnIndex,
    IReadOnlyList<ContextMessage> OutboundMessages) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures one provider-requested tool call emitted by the model.
/// </summary>
/// <param name="ToolCallId">The provider tool call identifier.</param>
/// <param name="ToolName">The chosen tool name.</param>
/// <param name="ArgumentsJson">The raw JSON argument payload emitted by the model.</param>
public sealed record SessionToolCall(string ToolCallId, string ToolName, string ArgumentsJson);

/// <summary>
/// Captures the model response after one provider call.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the response arrived.</param>
/// <param name="TurnIndex">The zero-based turn index that produced this response.</param>
/// <param name="AssistantContent">The assistant text content extracted from the provider response.</param>
/// <param name="ToolCallsIssued">The tool calls requested by the model.</param>
/// <param name="InputTokensReported">The provider-reported input token count when available.</param>
/// <param name="OutputTokensReported">The provider-reported output token count when available.</param>
/// <param name="TotalTokensReported">The provider-reported total token count when available.</param>
public sealed record ModelRespondedEvent(
    DateTime TimestampUtc,
    int TurnIndex,
    string AssistantContent,
    IReadOnlyList<SessionToolCall> ToolCallsIssued,
    int? InputTokensReported,
    int? OutputTokensReported,
    int? TotalTokensReported) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures the start of one tool invocation.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the tool call began.</param>
/// <param name="ToolName">The tool name being executed.</param>
/// <param name="ArgumentsJson">The raw JSON argument payload passed to the tool.</param>
public sealed record ToolCalledEvent(
    DateTime TimestampUtc,
    string ToolName,
    string ArgumentsJson) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures the completion of one tool invocation.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the tool call completed.</param>
/// <param name="ToolName">The tool name that completed.</param>
/// <param name="ResultContent">The raw tool result content before markdown truncation.</param>
/// <param name="Duration">The tool execution duration.</param>
public sealed record ToolCompletedEvent(
    DateTime TimestampUtc,
    string ToolName,
    string ResultContent,
    TimeSpan Duration) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures one assistant reply emitted for one user message.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the assistant reply was produced.</param>
/// <param name="ExchangeIndex">The zero-based index of the user-message exchange inside the session.</param>
/// <param name="Content">The assistant reply content.</param>
public sealed record AssistantReplyEvent(
    DateTime TimestampUtc,
    int ExchangeIndex,
    string Content) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures the final outcome of one user-message exchange inside the live session.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the exchange outcome was known.</param>
/// <param name="ExchangeIndex">The zero-based index of the user-message exchange inside the session.</param>
/// <param name="Outcome">The short outcome name.</param>
/// <param name="Details">Optional details explaining the outcome.</param>
public sealed record ExchangeOutcomeEvent(
    DateTime TimestampUtc,
    int ExchangeIndex,
    string Outcome,
    string? Details) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures normal session completion.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the session ended.</param>
/// <param name="TotalTurns">The total number of turns completed.</param>
/// <param name="TotalReportedTokens">The total reported tokens when available.</param>
/// <param name="TerminalOutcome">The terminal outcome description.</param>
public sealed record SessionEndedEvent(
    DateTime TimestampUtc,
    int TotalTurns,
    int? TotalReportedTokens,
    string TerminalOutcome) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures cancellation before normal session completion.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when cancellation was observed.</param>
/// <param name="TurnIndex">The zero-based turn index active at cancellation time.</param>
/// <param name="PartialReason">The best available partial reason for cancellation.</param>
public sealed record SessionCancelledEvent(
    DateTime TimestampUtc,
    int TurnIndex,
    string PartialReason) : SessionEvent(TimestampUtc);

/// <summary>
/// Captures an unhandled failure before normal session completion.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when failure was observed.</param>
/// <param name="ExceptionType">The exception type name.</param>
/// <param name="Message">The exception message.</param>
/// <param name="StackTrace">The exception stack trace when available.</param>
public sealed record SessionFailedEvent(
    DateTime TimestampUtc,
    string ExceptionType,
    string Message,
    string StackTrace) : SessionEvent(TimestampUtc)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionFailedEvent"/> record from an exception.
    /// </summary>
    /// <param name="timestampUtc">The UTC timestamp when failure was observed.</param>
    /// <param name="exception">The exception to flatten into transcript-safe fields.</param>
    public SessionFailedEvent(DateTime timestampUtc, Exception exception)
        : this(
            timestampUtc,
            exception?.GetType().FullName ?? throw new ArgumentNullException(nameof(exception)),
            exception.Message,
            exception.StackTrace ?? string.Empty)
    {
    }
}
