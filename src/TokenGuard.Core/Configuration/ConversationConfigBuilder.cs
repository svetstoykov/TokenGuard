using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Core.Configuration;

/// <summary>
///     Provides a fluent API for configuring <see cref="ConversationContextConfiguration"/> instances.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="ConversationConfigBuilder"/> when a conversation-context configuration needs to be composed from
///         a token budget, a token counter, and TokenGuard's built-in compaction pipeline without constructing the
///         underlying <see cref="ContextBudget"/> manually.
///     </para>
///     <para>
///         The builder requires <see cref="WithMaxTokens(int)"/> to be called before <see cref="Build"/>.
///         All other configuration methods are optional. Any budget values not explicitly configured are taken
///         from <see cref="ContextBudget.For(int)"/> for the configured maximum token count, while compaction always
///         uses <see cref="TieredCompactionStrategy"/> with sliding-window masking and an optional provider-backed
///         summarization stage.
///     </para>
    /// </remarks>
public sealed class ConversationConfigBuilder
{
    private int? _maxTokens;
    private double? _compactionThreshold;
    private double? _emergencyThreshold;
    private double? _overrunTolerance;
    private SlidingWindowOptions? _slidingWindowOptions;
    private LlmSummarizationStrategy? _llmSummarizationStrategy;
    private string? _llmSummarizationProviderName;
    private ITokenCounter? _tokenCounter;
    private ICompactionObserver? _observer;

    /// <summary>
    ///     Creates a <see cref="ConversationContextConfiguration"/> using the default builder configuration.
    /// </summary>
    /// <remarks>
    ///     This method delegates to a new <see cref="ConversationConfigBuilder"/> instance and applies only
    ///     <see cref="WithMaxTokens(int)"/> before calling <see cref="Build"/>. When no value is supplied,
    ///     the resulting configuration uses the library default profile: 100,000 tokens, a 0.80 compaction
    ///     threshold, no emergency truncation, <see cref="EstimatedTokenCounter"/>, and
    ///     <see cref="TieredCompactionStrategy"/> with <see cref="SlidingWindowOptions.Default"/> and no LLM stage.
/// </remarks>
    /// <param name="maxTokens">
    ///     The maximum number of tokens allowed in the conversation. Defaults to 100,000 when omitted.
    /// </param>
    /// <returns>A configured <see cref="ConversationContextConfiguration"/> instance.</returns>
    public static ConversationContextConfiguration Default(int maxTokens = ConversationDefaults.MaxTokens) =>
        new ConversationConfigBuilder()
            .WithMaxTokens(maxTokens)
            .Build();

    /// <summary>
    ///     Sets the maximum number of tokens allowed in the conversation.
    /// </summary>
    /// <remarks>
    ///     This value is required. <see cref="Build"/> throws <see cref="InvalidOperationException"/> if it has
    ///     not been configured.
    /// </remarks>
    /// <param name="maxTokens">The maximum number of tokens the caller permits in the conversation.</param>
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
    ///     Sets the configuration used by the always-on sliding-window masking stage.
    /// </summary>
    /// <remarks>
    ///     When not configured, <see cref="SlidingWindowOptions.Default"/> is used. This only adjusts the masking
    ///     phase; provider-backed LLM summarization is registered separately through extension-package methods such as
    ///     <c>UseLlmSummarization(...)</c>.
    /// </remarks>
    /// <param name="options">The sliding-window masking configuration.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithSlidingWindowOptions(SlidingWindowOptions options)
    {
        this._slidingWindowOptions = options;
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
    ///         If no token counter has been configured, this method uses <see cref="EstimatedTokenCounter"/>.
    ///         Compaction always uses <see cref="TieredCompactionStrategy"/> with configured
    ///         <see cref="SlidingWindowOptions"/> and an optional provider-backed <see cref="LlmSummarizationStrategy"/>.
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
            this._overrunTolerance ?? ConversationDefaults.OverrunTolerance);

        var counter = this._tokenCounter ?? new EstimatedTokenCounter();
        var strategy = new TieredCompactionStrategy(
            this._slidingWindowOptions ?? SlidingWindowOptions.Default,
            this._llmSummarizationStrategy);

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
    ///         <see cref="ContextBudget.For(int)"/> of 0.80 compaction and no emergency truncation,
    ///         and a missing counter falls back to <see cref="TokenCounting.EstimatedTokenCounter"/>.
    ///         Compaction still uses the same internal <see cref="Strategies.TieredCompactionStrategy"/> pipeline as
    ///         <see cref="Build"/>.
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

    /// <summary>
    /// Registers provider-backed LLM summarization for the current builder.
    /// </summary>
    /// <param name="summarizer">The provider implementation that produces summaries when masking is insufficient.</param>
    /// <param name="providerName">The human-readable provider name used in conflict messages.</param>
    /// <param name="options">
    /// Optional summarization options. When omitted, <see cref="LlmSummarizationOptions.Default"/> is used.
    /// </param>
    /// <returns>The current builder instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="summarizer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="providerName"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a summarization provider has already been registered on this builder instance.
    /// </exception>
    internal ConversationConfigBuilder SetLlmSummarizer(
        ILlmSummarizer summarizer,
        string providerName,
        LlmSummarizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (this._llmSummarizationStrategy is not null)
        {
            throw new InvalidOperationException(BuildProviderConflictMessage(
                this._llmSummarizationProviderName!,
                providerName));
        }

        this._llmSummarizationStrategy = options.HasValue
            ? new LlmSummarizationStrategy(summarizer, options.Value)
            : new LlmSummarizationStrategy(summarizer);
        this._llmSummarizationProviderName = providerName;

        return this;
    }

    private static string BuildProviderConflictMessage(string existingProviderName, string conflictingProviderName)
    {
        return
            $"LLM summarization provider '{existingProviderName}' is already registered. Only one provider can be registered per builder instance.{Environment.NewLine}" +
            $"Conflicting registration attempted for provider '{conflictingProviderName}'.{Environment.NewLine}" +
            $"Remove the conflicting call before adding a new provider:{Environment.NewLine}{Environment.NewLine}" +
            $"    builder.UseLlmSummarization({GetClientVariableName(existingProviderName)});   // keep one{Environment.NewLine}" +
            $"    builder.UseLlmSummarization({GetClientVariableName(conflictingProviderName)}); // remove this";
    }

    private static string GetClientVariableName(string providerName) =>
        providerName switch
        {
            "OpenAI" => "openAiClient",
            "Anthropic" => "anthropicClient",
            _ => char.ToLowerInvariant(providerName[0]) + providerName[1..] + "Client",
        };
}
