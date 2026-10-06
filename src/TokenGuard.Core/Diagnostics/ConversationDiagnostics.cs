using Microsoft.Extensions.Logging;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Represents the diagnostic identity of one conversation context.
/// </summary>
/// <remarks>
///     One instance is created for each <see cref="ConversationContext" /> and shared with the compaction strategy
///     built for it, so every log record of one conversation carries the same identifiers.
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
}
