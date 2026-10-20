using Microsoft.Extensions.Logging;
using TokenGuard.Core.Abstractions;

namespace TokenGuard.Core.Models;

/// <summary>
///     Represents the services and configuration name available when constructing a conversation's compaction strategy.
/// </summary>
/// <remarks>
///     A fresh instance is supplied for each conversation. The logger factory uses the configured factory, then the
///     container's factory, then a factory that writes no records.
/// </remarks>
public sealed class CompactionStrategyContext
{
    /// <summary>Initializes a new instance of the <see cref="CompactionStrategyContext"/> class.</summary>
    /// <param name="tokenCounter">The conversation's timed token counter.</param>
    /// <param name="loggerFactory">The resolved logger factory.</param>
    /// <param name="contextName">The registered configuration name.</param>
    internal CompactionStrategyContext(ITokenCounter tokenCounter, ILoggerFactory loggerFactory, string contextName)
    {
        this.TokenCounter = tokenCounter;
        this.LoggerFactory = loggerFactory;
        this.ContextName = contextName;
    }

    /// <summary>Gets the conversation's token counter, including TokenGuard's timing instrumentation.</summary>
    public ITokenCounter TokenCounter { get; }

    /// <summary>Gets the resolved logger factory used by the conversation.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>Gets the registered configuration name, or <c>default</c> for the unnamed configuration.</summary>
    public string ContextName { get; }
}
