using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Configuration;

/// <summary>
///     Provides a fluent API for configuring <see cref="ConversationContextConfiguration"/> instances.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="ConversationConfigBuilder"/> when a conversation-context configuration needs to be composed from
///         a token budget and TokenGuard's built-in compaction pipeline without constructing the
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
    private double? _emergencyThreshold = ConversationDefaults.EmergencyThreshold;
    private double? _overrunTolerance;
    private SlidingWindowOptions? _slidingWindowOptions;
    private Func<ILlmSummarizer>? _llmSummarizerFactory;
    private LlmSummarizationOptions? _llmSummarizationOptions;
    private string? _llmSummarizationProviderName;
    /// <summary>
    ///     Creates a <see cref="ConversationContextConfiguration"/> using the default builder configuration.
    /// </summary>
    /// <remarks>
    ///     This method delegates to a new <see cref="ConversationConfigBuilder"/> instance and applies only
    ///     <see cref="WithMaxTokens(int)"/> before calling <see cref="Build"/>. When no value is supplied,
    ///     the resulting configuration uses the library default profile: 25,000 tokens, a 0.80 compaction
    ///     threshold, no emergency truncation, TokenGuard's built-in heuristic token counting, and
    ///     <see cref="TieredCompactionStrategy"/> with <see cref="SlidingWindowOptions.Default"/> and no LLM stage.
/// </remarks>
    /// <param name="maxTokens">
    ///     The maximum number of tokens allowed in the conversation. Defaults to 25,000 when omitted.
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
    ///     When not configured, the library default value from <see cref="ConversationDefaults"/> is used
    ///     (<c>1.0</c> — emergency fires only when the context reaches the absolute token limit). To disable
    ///     emergency truncation entirely, call <see cref="WithoutEmergencyThreshold"/> instead.
    ///     <para>
    ///         Emergency truncation is destructive: it drops whole turn groups oldest-first and cannot be reversed.
    ///         Prefer keeping the default unless you need a more aggressive trigger point.
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
    ///     Disables emergency truncation so the runtime never drops messages after the primary compaction strategy runs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Emergency truncation is a last-resort safety net: it fires at <c>1.0</c> of
    ///         <see cref="ContextBudget.MaxTokens"/> by default and drops whole turn groups oldest-first when the primary
    ///         compaction strategy cannot bring the context within budget. Our tests show that truncating older messages
    ///         is rarely required in normal operation, so disabling the safety net is safe in practice — but when it
    ///         does trigger, it prevents silent context overflow.
    ///     </para>
    ///     <para>
    ///         When emergency truncation is disabled, a conversation that the compaction strategy cannot resolve returns
    ///         <see cref="Enums.PrepareOutcome.Degraded"/> or <see cref="Enums.PrepareOutcome.ContextExhausted"/>
    ///         instead of silently dropping messages. Use this overload only when those outcomes are preferable to
    ///         message loss — for example, when the caller needs to detect and handle overflow explicitly.
    ///     </para>
    /// </remarks>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithoutEmergencyThreshold()
    {
        this._emergencyThreshold = null;
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
    ///     Captures an immutable construction recipe from the current builder state as a
    ///     <see cref="ConversationContextConfiguration"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Budget values not explicitly configured on the builder are merged with the library defaults
    ///         from <see cref="ContextBudget.For(int)"/> for the configured maximum token count: 0.80 compaction,
    ///         no emergency truncation, and 0 reserved tokens.
    ///     </para>
    ///     <para>
    ///         Token counting always uses TokenGuard's built-in heuristic <see cref="ITokenCounter"/> implementation.
    ///         Compaction always uses a freshly created <see cref="TieredCompactionStrategy"/> with configured
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

        var strategyFactory = BuildStrategyFactory(
            this._slidingWindowOptions ?? SlidingWindowOptions.Default,
            this._llmSummarizerFactory,
            this._llmSummarizationOptions);

        return new ConversationContextConfiguration(budget, strategyFactory);
    }

    /// <summary>
    /// Registers provider-backed LLM summarization for the current builder.
    /// </summary>
    /// <param name="summarizerFactory">
    /// A factory that creates the provider implementation that produces summaries when masking is insufficient.
    /// </param>
    /// <param name="providerName">The human-readable provider name used in conflict messages.</param>
    /// <param name="options">
    /// Optional summarization options. When omitted, <see cref="LlmSummarizationOptions.Default"/> is used.
    /// </param>
    /// <returns>The current builder instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="summarizerFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="providerName"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a summarization provider has already been registered on this builder instance.
    /// </exception>
    internal ConversationConfigBuilder SetLlmSummarizer(
        Func<ILlmSummarizer> summarizerFactory,
        string providerName,
        LlmSummarizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summarizerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (this._llmSummarizerFactory is not null)
        {
            throw new InvalidOperationException(BuildProviderConflictMessage(
                this._llmSummarizationProviderName!,
                providerName));
        }

        this._llmSummarizerFactory = summarizerFactory;
        this._llmSummarizationOptions = options;
        this._llmSummarizationProviderName = providerName;

        return this;
    }

    private static Func<ITokenCounter, ICompactionStrategy> BuildStrategyFactory(
        SlidingWindowOptions slidingWindowOptions,
        Func<ILlmSummarizer>? llmSummarizerFactory,
        LlmSummarizationOptions? llmSummarizationOptions)
    {
        if (llmSummarizerFactory is null)
        {
            return tokenCounter => new TieredCompactionStrategy(tokenCounter, slidingWindowOptions);
        }

        return tokenCounter =>
        {
            var llmStrategy = llmSummarizationOptions.HasValue
                ? new LlmSummarizationStrategy(llmSummarizerFactory(), tokenCounter, llmSummarizationOptions.Value)
                : new LlmSummarizationStrategy(llmSummarizerFactory(), tokenCounter);

            return new TieredCompactionStrategy(tokenCounter, slidingWindowOptions, llmStrategy);
        };
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
