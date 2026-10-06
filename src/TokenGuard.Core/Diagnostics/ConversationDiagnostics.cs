using Microsoft.Extensions.Logging;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Represents the diagnostic identity and shared counters of one conversation context.
/// </summary>
/// <remarks>
///     One instance is created for each <see cref="ConversationContext" /> and shared with the compaction strategy
///     built for it, so every log record of one conversation carries the same identifiers and the context can read the
///     summarizer totals the strategy counts.
/// </remarks>
internal sealed class ConversationDiagnostics
{
    /// <summary>
    ///     The context name reported for conversations created from the default configuration.
    /// </summary>
    internal const string DefaultContextName = "default";

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConversationDiagnostics" /> class.
    /// </summary>
    /// <param name="loggerFactory">The factory that creates the loggers of this conversation. Cannot be <see langword="null" />.</param>
    /// <param name="contextName">The registered configuration name of the conversation. Cannot be <see langword="null" />.</param>
    internal ConversationDiagnostics(ILoggerFactory loggerFactory, string contextName)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(contextName);

        this.LoggerFactory = loggerFactory;
        this.ContextName = contextName;
        this.ConversationId = Guid.NewGuid().ToString("N");
        this.ContextNameTag = TokenGuardTelemetry.Tag(TokenGuardTelemetry.ContextNameTag, contextName);
    }

    /// <summary>
    ///     Gets the factory that creates the loggers of this conversation.
    /// </summary>
    internal ILoggerFactory LoggerFactory { get; }

    /// <summary>
    ///     Gets the registered configuration name of the conversation.
    /// </summary>
    internal string ContextName { get; }

    /// <summary>
    ///     Gets the identifier generated for this conversation.
    /// </summary>
    internal string ConversationId { get; }

    /// <summary>
    ///     Gets the metric tag that names the configuration of this conversation.
    /// </summary>
    internal KeyValuePair<string, object?> ContextNameTag { get; }

    /// <summary>
    ///     Gets or sets the number of summarizer calls made for this conversation.
    /// </summary>
    internal int SummarizerCalls { get; set; }

    /// <summary>
    ///     Gets or sets the number of summarizer calls that threw for this conversation.
    /// </summary>
    internal int SummarizerFailures { get; set; }
}
