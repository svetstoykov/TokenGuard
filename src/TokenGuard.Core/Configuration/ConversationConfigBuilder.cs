using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Models;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Core.Configuration;

/// <summary>
///     Provides a fluent API for configuring <see cref="ConversationContextConfiguration"/> instances.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="ConversationConfigBuilder"/> when a conversation-context configuration needs to be composed from
///         a token budget, a token counter, and a compaction strategy without constructing the underlying
///         <see cref="ContextBudget"/> manually.
///     </para>
///     <para>
///         The builder requires <see cref="WithMaxTokens(int)"/> to be called before <see cref="Build"/>.
///         All other configuration methods are optional. Any budget values not explicitly configured are taken
///         from <see cref="ContextBudget.For(int)"/> for the configured maximum token count.
///     </para>
/// </remarks>
public sealed class ConversationConfigBuilder
{
    private int? _maxTokens;
    private double? _compactionThreshold;
    private double? _emergencyThreshold;
    private int? _reservedTokens;
    private double? _overrunTolerance;
    private ICompactionStrategy? _strategy;
    private ITokenCounter? _tokenCounter;
    private ICompactionObserver? _observer;

    /// <summary>
    ///     Creates a <see cref="ConversationContextConfiguration"/> using the default builder configuration.
    /// </summary>
    /// <remarks>
    ///     This method delegates to a new <see cref="ConversationConfigBuilder"/> instance and applies only
    ///     <see cref="WithMaxTokens(int)"/> before calling <see cref="Build"/>. When no value is supplied,
    ///     the resulting configuration uses the library default profile: 100,000 tokens, a 0.80 compaction
    ///     threshold, no emergency truncation, 0 reserved tokens, <see cref="EstimatedTokenCounter"/>,
    ///     and <see cref="SlidingWindowStrategy"/>.
    /// </remarks>
    /// <param name="maxTokens">
    ///     The hard token ceiling for the full context window. Defaults to 100,000 when omitted.
    /// </param>
    /// <returns>A configured <see cref="ConversationContextConfiguration"/> instance.</returns>
    public static ConversationContextConfiguration Default(int maxTokens = ConversationDefaults.MaxTokens) =>
        new ConversationConfigBuilder()
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
    public ConversationConfigBuilder WithMaxTokens(int maxTokens)
    {
        this._maxTokens = maxTokens;
        return this;
    }

    /// <summary>
    ///     Sets the fraction of available tokens at which normal compaction starts.
    /// </summary>
    /// <remarks>
    ///     When this value is not configured, the library default value from <see cref="ContextBudget.For(int)"/>
    ///     is used, which is 0.80 for the configured maximum token count.
    /// </remarks>
    /// <param name="compactionThreshold">The compaction trigger threshold.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithCompactionThreshold(double compactionThreshold)
    {
        this._compactionThreshold = compactionThreshold;
        return this;
    }

    /// <summary>
    ///     Sets the fraction of available tokens at which emergency truncation starts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When not configured, emergency truncation is disabled and the resulting budget has a
    ///         <see langword="null"/> <see cref="ContextBudget.EmergencyThreshold"/>.
    ///     </para>
    ///     <para>
    ///         Emergency truncation is destructive: it drops whole turn groups oldest-first until the context fits or
    ///         nothing further can be removed. Enable it only when that behavior is acceptable for the target use case.
    ///     </para>
    /// </remarks>
    /// <param name="emergencyThreshold">The emergency truncation trigger threshold.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithEmergencyThreshold(double emergencyThreshold)
    {
        this._emergencyThreshold = emergencyThreshold;
        return this;
    }

    /// <summary>
    ///     Sets the number of tokens reserved for fixed, non-message content.
    /// </summary>
    /// <remarks>
    ///     When this value is not configured, the library default value from <see cref="ContextBudget.For(int)"/>
    ///     is used, which is 0 reserved tokens.
    /// </remarks>
    /// <param name="reservedTokens">The reserved token count.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithReservedTokens(int reservedTokens)
    {
        this._reservedTokens = reservedTokens;
        return this;
    }

    /// <summary>
    ///     Sets the fraction of <see cref="ContextBudget.MaxTokens"/> by which a prepared result may exceed the budget
    ///     and still be considered acceptable.
    /// </summary>
    /// <remarks>
    ///     When not configured, the default is <see cref="ConversationDefaults.OverrunTolerance"/> (0.05 — 5% of the
    ///     configured maximum token count). Pass <c>0.0</c> to disable tolerance and restore strict-budget behavior.
    ///     A positive value lets callers accept a small estimated overrun without the result being classified as
    ///     <see cref="Enums.PrepareOutcome.Degraded"/>. Compaction strategies always target the hard
    ///     <see cref="ContextBudget.MaxTokens"/> ceiling; the tolerance only affects the final outcome classification
    ///     after all compaction techniques have run. See <see cref="ContextBudget.OverrunTolerance"/> for full
    ///     behavioral details.
    /// </remarks>
    /// <param name="overrunTolerance">
    ///     The fraction of <see cref="ContextBudget.MaxTokens"/> above the budget ceiling that is still considered
    ///     acceptable. Must be in the range [0.0, 1.0].
    /// </param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithOverrunTolerance(double overrunTolerance)
    {
        this._overrunTolerance = overrunTolerance;
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
    public ConversationConfigBuilder WithStrategy(ICompactionStrategy strategy)
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
    public ConversationConfigBuilder WithTokenCounter(ITokenCounter tokenCounter)
    {
        this._tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        return this;
    }

    /// <summary>
    ///     Sets the observer that is notified after each compaction cycle that modifies the history.
    /// </summary>
    /// <remarks>
    ///     The observer is optional. When not configured, no compaction notifications are emitted and
    ///     <see cref="ConversationContextConfiguration.Observer"/> is <see langword="null"/>.
    /// </remarks>
    /// <param name="observer">The compaction observer.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithCompactionObserver(ICompactionObserver observer)
    {
        this._observer = observer ?? throw new ArgumentNullException(nameof(observer));
        return this;
    }

    /// <summary>
    ///     Captures an immutable snapshot of the current builder state as a
    ///     <see cref="ConversationContextConfiguration"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Budget values not explicitly configured on the builder are merged with the library defaults
    ///         from <see cref="ContextBudget.For(int)"/> for the configured maximum token count: 0.80 compaction,
    ///         no emergency truncation, and 0 reserved tokens.
    ///     </para>
    ///     <para>
    ///         If no token counter or compaction strategy has been configured, this method uses
    ///         <see cref="EstimatedTokenCounter"/> and <see cref="SlidingWindowStrategy"/>, respectively.
    ///     </para>
    /// </remarks>
    /// <returns>
    ///     An immutable <see cref="ConversationContextConfiguration"/> reflecting the builder's current
    ///     configuration with all defaults applied.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="WithMaxTokens(int)"/> has not been called.
    /// </exception>
    public ConversationContextConfiguration Build()
    {
        if (!this._maxTokens.HasValue)
        {
            throw new InvalidOperationException("ConversationContextConfigurationBuilder requires WithMaxTokens(...) to be called before Build().");
        }

        var defaults = ContextBudget.For(this._maxTokens.Value);
        var budget = new ContextBudget(
            this._maxTokens.Value,
            this._compactionThreshold ?? defaults.CompactionThreshold,
            this._emergencyThreshold,
            this._reservedTokens ?? defaults.ReservedTokens,
            this._overrunTolerance ?? ConversationDefaults.OverrunTolerance);

        var counter = this._tokenCounter ?? new EstimatedTokenCounter();
        var strategy = this._strategy ?? new SlidingWindowStrategy();

        return new ConversationContextConfiguration(budget, counter, strategy, this._observer);
    }

    /// <summary>
    ///     Captures an immutable snapshot of the current builder state as a
    ///     <see cref="ConversationContextConfiguration"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method applies exactly the same defaulting logic as <see cref="Build"/>: any budget
    ///         values not explicitly configured are merged with the library defaults from
    ///         <see cref="ContextBudget.For(int)"/> of 0.80 compaction, no emergency truncation, and 0 reserved tokens,
    ///         and missing counter or strategy choices fall back to
    ///         <see cref="TokenCounting.EstimatedTokenCounter"/> and <see cref="Strategies.SlidingWindowStrategy"/>,
    ///         respectively.
    ///     </para>
    ///     <para>
    ///         Use <see cref="BuildConfiguration"/> instead of <see cref="Build"/> when the resulting
    ///         configuration will be handed to the built-in dependency-injection registration pipeline.
    ///         The factory behind <see cref="Abstractions.IConversationContextFactory"/> stores the
    ///         snapshot and constructs a new <see cref="ConversationContext"/> from it on every
    ///         <c>Create</c> call.
    ///     </para>
    /// </remarks>
    /// <returns>
    ///     An immutable <see cref="ConversationContextConfiguration"/> reflecting the builder's current
    ///     configuration with all defaults applied.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="WithMaxTokens(int)"/> has not been called.
    /// </exception>
    public ConversationContextConfiguration BuildConfiguration() => this.Build();
}
