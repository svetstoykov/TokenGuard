using SemanticFold.Core.Abstractions;
using SemanticFold.Core.Models;
using SemanticFold.Core.Strategies;
using SemanticFold.Core.TokenCounting;

namespace SemanticFold.Core;

/// <summary>
///     Provides a fluent API for configuring and creating <see cref="ConversationContext"/> instances.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="ConversationContextBuilder"/> when a conversation context needs to be composed from
///         a token budget, a token counter, and a compaction strategy without constructing the underlying
///         <see cref="ContextBudget"/> manually.
///     </para>
///     <para>
///         The builder requires <see cref="WithMaxTokens(int)"/> to be called before <see cref="Build"/>.
///         All other configuration methods are optional. Any budget values not explicitly configured are taken
///         from <see cref="ContextBudget.For(int)"/> for the configured maximum token count.
///     </para>
/// </remarks>
public sealed class ConversationContextBuilder
{
    private int? _maxTokens;
    private double? _compactionThreshold;
    private double? _emergencyThreshold;
    private int? _reservedTokens;
    private ICompactionStrategy? _strategy;
    private ITokenCounter? _tokenCounter;

    /// <summary>
    ///     Creates a <see cref="ConversationContext"/> using the default builder configuration.
    /// </summary>
    /// <remarks>
    ///     This method delegates to a new <see cref="ConversationContextBuilder"/> instance and applies only
    ///     <see cref="WithMaxTokens(int)"/> before calling <see cref="Build"/>. When no value is supplied,
    ///     the context is created with a maximum token budget of 100,000.
    /// </remarks>
    /// <param name="maxTokens">
    ///     The hard token ceiling for the full context window. Defaults to 100,000 when omitted.
    /// </param>
    /// <returns>A configured <see cref="ConversationContext"/> instance.</returns>
    public static ConversationContext Default(int maxTokens = 100_000) =>
        new ConversationContextBuilder()
            .WithMaxTokens(maxTokens)
            .Build();

    /// <summary>
    ///     Sets the hard token ceiling for the context window.
    /// </summary>
    /// <remarks>
    ///     This value is required. <see cref="Build"/> throws <see cref="InvalidOperationException"/> if it has
    ///     not been configured.
    /// </remarks>
    /// <param name="maxTokens">The maximum number of tokens allowed in the context window.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationContextBuilder WithMaxTokens(int maxTokens)
    {
        this._maxTokens = maxTokens;
        return this;
    }

    /// <summary>
    ///     Sets the fraction of available tokens at which normal compaction starts.
    /// </summary>
    /// <remarks>
    ///     When this value is not configured, the default value from <see cref="ContextBudget.For(int)"/> is used.
    /// </remarks>
    /// <param name="compactionThreshold">The compaction trigger threshold.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationContextBuilder WithCompactionThreshold(double compactionThreshold)
    {
        this._compactionThreshold = compactionThreshold;
        return this;
    }

    /// <summary>
    ///     Sets the fraction of available tokens at which emergency compaction starts.
    /// </summary>
    /// <remarks>
    ///     When this value is not configured, the default value from <see cref="ContextBudget.For(int)"/> is used.
    /// </remarks>
    /// <param name="emergencyThreshold">The emergency compaction trigger threshold.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationContextBuilder WithEmergencyThreshold(double emergencyThreshold)
    {
        this._emergencyThreshold = emergencyThreshold;
        return this;
    }

    /// <summary>
    ///     Sets the number of tokens reserved for fixed, non-message content.
    /// </summary>
    /// <remarks>
    ///     When this value is not configured, the default value from <see cref="ContextBudget.For(int)"/> is used.
    /// </remarks>
    /// <param name="reservedTokens">The reserved token count.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationContextBuilder WithReservedTokens(int reservedTokens)
    {
        this._reservedTokens = reservedTokens;
        return this;
    }

    /// <summary>
    ///     Sets the compaction strategy used when the context exceeds the configured threshold.
    /// </summary>
    /// <remarks>
    ///     When no strategy is configured, <see cref="SlidingWindowStrategy"/> is used.
    /// </remarks>
    /// <param name="strategy">The compaction strategy.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationContextBuilder WithStrategy(ICompactionStrategy strategy)
    {
        this._strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    /// <summary>
    ///     Sets the token counter used to estimate message token counts.
    /// </summary>
    /// <remarks>
    ///     When no token counter is configured, <see cref="EstimatedTokenCounter"/> is used.
    /// </remarks>
    /// <param name="tokenCounter">The token counter.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationContextBuilder WithTokenCounter(ITokenCounter tokenCounter)
    {
        this._tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        return this;
    }

    /// <summary>
    ///     Creates a <see cref="ConversationContext"/> from the configured builder values.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Any budget values not explicitly configured on the builder are merged with the defaults from
    ///         <see cref="ContextBudget.For(int)"/> for the configured maximum token count.
    ///     </para>
    ///     <para>
    ///         If no token counter or compaction strategy has been configured, this method uses
    ///         <see cref="EstimatedTokenCounter"/> and <see cref="SlidingWindowStrategy"/>, respectively.
    ///     </para>
    /// </remarks>
    /// <returns>A configured <see cref="ConversationContext"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="WithMaxTokens(int)"/> was not called.</exception>
    public ConversationContext Build()
    {
        if (!this._maxTokens.HasValue)
        {
            throw new InvalidOperationException("ConversationContextBuilder requires WithMaxTokens(...) to be called before Build().");
        }

        var defaults = ContextBudget.For(this._maxTokens.Value);
        var budget = new ContextBudget(
            this._maxTokens.Value,
            this._compactionThreshold ?? defaults.CompactionThreshold,
            this._emergencyThreshold ?? defaults.EmergencyThreshold,
            this._reservedTokens ?? defaults.ReservedTokens);

        var counter = this._tokenCounter ?? new EstimatedTokenCounter();
        var strategy = this._strategy ?? new SlidingWindowStrategy();

        return new ConversationContext(budget, counter, strategy);
    }
}
