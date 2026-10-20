using Microsoft.Extensions.Logging;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Diagnostics;

namespace TokenGuard.Core.Models;

/// <summary>
/// An immutable construction recipe for the built-in token counter, the compaction services, and the budget that define the behaviour of a
/// <see cref="ConversationContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationContextConfiguration"/> is produced by <see cref="ConversationConfigBuilder.Build"/>
/// and consumed by the built-in factory behind <see cref="Abstractions.IConversationContextFactory"/> to stamp out
/// fresh context instances on demand without re-running the builder defaults logic each time.
/// </para>
/// <para>
/// TokenGuard's built-in <see cref="ITokenCounter"/> implementation is constructed exactly once per
/// <see cref="Abstractions.IConversationContextFactory.Create()"/> or
/// <see cref="Abstractions.IConversationContextFactory.Create(string)"/> call.
/// <paramref name="StrategyFactory"/> is invoked exactly once per factory call. No object produced for one context
/// lifetime is shared with another context created through the built-in factory.
/// </para>
/// <para>
/// The record is intentionally minimal — it contains only what is needed to construct a
/// <see cref="ConversationContext"/> and nothing else. Do not add extra properties to it; it is a
/// construction recipe, not a general-purpose options bag.
/// </para>
/// <para>
/// <see cref="MaxToolResultTokens"/> is set from <paramref name="Budget"/> when the record is created. A copy made with
/// <see langword="with"/> that changes only <see cref="Budget"/> keeps the earlier limit; set both to change both.
/// </para>
/// </remarks>
/// <param name="Budget">
/// Defines the token limits for conversations created from this configuration, including the
/// compaction trigger threshold and the emergency truncation threshold. The built-in factory rejects a configuration
/// whose budget is <c>default(ContextBudget)</c>.
/// </param>
/// <param name="StrategyFactory">
/// Creates the <see cref="ICompactionStrategy"/> used by one conversation-context instance from that context's
/// <see cref="ITokenCounter"/>. The built-in factory invokes this delegate once for each created context so
/// compaction state is never shared across context lifetimes and the counting dependency stays explicit.
/// </param>
public sealed record ConversationContextConfiguration(
    ContextBudget Budget,
    Func<ITokenCounter, ICompactionStrategy> StrategyFactory)
{
    private readonly Func<ITokenCounter, ICompactionStrategy> _strategyFactory = StrategyFactory;
    private readonly int? _maxToolResultTokens = ToolResultLimitDefaults.Resolve(Budget.MaxTokens);

    /// <summary>
    ///     Gets the delegate that creates the <see cref="ICompactionStrategy" /> used by one conversation-context instance.
    /// </summary>
    public Func<ITokenCounter, ICompactionStrategy> StrategyFactory
    {
        get => this._strategyFactory;
        init
        {
            this._strategyFactory = value;
            this.DiagnosticStrategyFactory = null;
        }
    }

    /// <summary>
    ///     Gets the largest estimated token count a tool result is stored at, or <see langword="null" /> when tool results are
    ///     stored whole.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ConversationContext.RecordToolResult" /> stores a result whose estimate is above this limit in shortened
    ///         form, with whole lines from its start and its end and a notice in place of the omitted middle. The estimate is made
    ///         by the context's token counter.
    ///     </para>
    ///     <para>
    ///         The default is the smaller of 25,000 and a quarter of <see cref="ContextBudget.MaxTokens" /> of the
    ///         <see cref="Budget" /> the record is created with, and at least one: 10,000 tokens at a 40,000-token budget. A copy
    ///         made with <see langword="with" /> that changes only <see cref="Budget" /> keeps the earlier limit. A limit larger
    ///         than the budget is allowed. A limit smaller than the omission notice stores the full notice alone and may be exceeded.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? MaxToolResultTokens
    {
        get => this._maxToolResultTokens;
        init
        {
            if (value is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The tool result limit must be positive, or null for no limit.");
            }

            this._maxToolResultTokens = value;
        }
    }

    /// <summary>
    ///     Gets the delegate that creates the same strategy as <see cref="StrategyFactory" /> with the loggers of one
    ///     conversation attached, or <see langword="null" /> when <see cref="StrategyFactory" /> is a caller-supplied delegate.
    /// </summary>
    internal Func<ITokenCounter, ConversationDiagnostics, ICompactionStrategy>? DiagnosticStrategyFactory { get; init; }

    /// <summary>
    ///     Gets the logger factory set through <see cref="ConversationConfigBuilder.WithLoggerFactory" />, or
    ///     <see langword="null" /> when the configuration names none.
    /// </summary>
    internal ILoggerFactory? LoggerFactory { get; init; }
}
