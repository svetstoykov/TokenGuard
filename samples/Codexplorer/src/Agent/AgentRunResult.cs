namespace Codexplorer.Agent;

/// <summary>
/// Represents the outcome of handling one user message inside an explorer session.
/// </summary>
/// <param name="ModelTurnsCompleted">The number of model turns completed while handling the current user message.</param>
public abstract record AgentExchangeResult(int ModelTurnsCompleted);

/// <summary>
/// Represents a successful exchange that produced an assistant reply.
/// </summary>
/// <param name="ReplyText">The assistant reply text produced for the current user message.</param>
/// <param name="ModelTurnsCompleted">The number of model turns completed while handling the current user message.</param>
/// <param name="ReportedTokensConsumed">The provider-reported tokens accumulated across model calls for this exchange.</param>
public sealed record AgentReplyReceived(string ReplyText, int ModelTurnsCompleted, int ReportedTokensConsumed) : AgentExchangeResult(ModelTurnsCompleted);

/// <summary>
/// Represents an exchange that stopped because the prepared context could no longer fit.
/// </summary>
/// <param name="Reason">The human-readable degradation reason.</param>
/// <param name="PartialText">The last partial assistant text seen before degradation, when any.</param>
/// <param name="ModelTurnsCompleted">The number of model turns completed while handling the current user message.</param>
public sealed record AgentExchangeDegraded(string Reason, string? PartialText, int ModelTurnsCompleted) : AgentExchangeResult(ModelTurnsCompleted);

/// <summary>
/// Represents an exchange that hit the configured per-message turn cap before reaching an assistant reply.
/// </summary>
/// <param name="PartialText">The last partial assistant text seen before the cap was hit, when any.</param>
/// <param name="ModelTurnsCompleted">The number of model turns completed while handling the current user message.</param>
public sealed record AgentExchangeMaxTurnsReached(string? PartialText, int ModelTurnsCompleted) : AgentExchangeResult(ModelTurnsCompleted);

/// <summary>
/// Represents an exchange cancelled by the caller, which also ends the live session.
/// </summary>
/// <param name="PartialText">The last partial assistant text seen before cancellation, when any.</param>
/// <param name="ModelTurnsCompleted">The number of model turns completed while handling the current user message.</param>
public sealed record AgentExchangeCancelled(string? PartialText, int ModelTurnsCompleted) : AgentExchangeResult(ModelTurnsCompleted);

/// <summary>
/// Represents an exchange that failed because of an unhandled exception, which also ends the live session.
/// </summary>
/// <param name="Exception">The exception that ended the run.</param>
/// <param name="ModelTurnsCompleted">The number of model turns completed while handling the current user message.</param>
public sealed record AgentExchangeFailed(Exception Exception, int ModelTurnsCompleted) : AgentExchangeResult(ModelTurnsCompleted);
