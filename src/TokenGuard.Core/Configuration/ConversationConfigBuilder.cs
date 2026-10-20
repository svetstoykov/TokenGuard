using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.Summarization;

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
///         from <see cref="ContextBudget.For(int)"/> for the configured maximum token count, while the default compaction pipeline
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

    // Set by WithMaxToolResultTokens and WithoutToolResultLimit; while false, Build uses the default for the budget.
    private bool _toolResultLimitConfigured;
    private int? _maxToolResultTokens;
    private SlidingWindowOptions? _slidingWindowOptions;
    private ILlmSummarizer? _llmSummarizer;
    private LlmSummarizationOptions? _llmSummarizationOptions;
    private ILoggerFactory? _loggerFactory;
    private ITokenCounter? _tokenCounter;
    private Func<CompactionStrategyContext, ICompactionStrategy>? _customStrategyFactory;

    /// <summary>
    ///     Creates a <see cref="ConversationContextConfiguration"/> using the default builder configuration.
    /// </summary>
    /// <remarks>
    ///     This method delegates to a new <see cref="ConversationConfigBuilder"/> instance and applies only
    ///     <see cref="WithMaxTokens(int)"/> before calling <see cref="Build"/>. When no value is supplied,
    ///     the resulting configuration uses the library default profile: 40,000 tokens, a 0.80 compaction
    ///     threshold, 1.0 emergency truncation as a last-resort safety net, a tool result limit of a quarter of the maximum
    ///     token count up to 25,000, TokenGuard's built-in heuristic token counting, and
    ///     <see cref="TieredCompactionStrategy"/> with <see cref="SlidingWindowOptions.Default"/> and no LLM stage.
    /// </remarks>
    /// <param name="maxTokens">
    ///     The maximum number of tokens allowed in the conversation. Defaults to 40,000 when omitted.
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
    ///     <para>
    ///         This value is required. <see cref="Build"/> throws <see cref="InvalidOperationException"/> if it has
    ///         not been configured.
    ///     </para>
    ///     <para>
    ///         The value is the input budget of one request, with no room reserved for the reply. Set it so that the
    ///         model's context window minus the largest reply requested is at least <c>maxTokens × (1 + overrun tolerance)</c>.
    ///         A caller that never passes a provider-reported input count also leaves room for tool definitions, about
    ///         140 tokens per tool in measured runs; see <see cref="ContextBudget.MaxTokens"/>.
    ///     </para>
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
    ///         <see cref="Enums.PrepareOutcome.CompactionInsufficient"/> or <see cref="Enums.PrepareOutcome.CannotCompact"/>
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
    ///     <see cref="Enums.PrepareOutcome.CompactionInsufficient"/>. Compaction strategies always target the hard
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
    ///     Sets the largest estimated token count a tool result is stored at.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When not configured, the limit is the smaller of 25,000 and a quarter of the configured maximum token count:
    ///         10,000 tokens at the default 40,000-token budget. To store every tool result whole, call
    ///         <see cref="WithoutToolResultLimit"/> instead.
    ///     </para>
    ///     <para>
    ///         <see cref="ConversationContext.RecordToolResult"/> stores a result whose estimate is above the limit with whole
    ///         lines from its start and its end and a notice in place of the omitted middle, so one large result cannot fill the
    ///         window. If the limit is smaller than the omission notice, the full notice is stored alone and may exceed the limit.
    ///         A limit larger than the budget is allowed. A tool that bounds its own output below the limit is never cut.
    ///     </para>
    /// </remarks>
    /// <param name="maxToolResultTokens">The largest estimated token count of one stored tool result. Must be positive.</param>
    /// <returns>The current builder instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxToolResultTokens"/> is zero or negative.</exception>
    public ConversationConfigBuilder WithMaxToolResultTokens(int maxToolResultTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxToolResultTokens);

        this._maxToolResultTokens = maxToolResultTokens;
        this._toolResultLimitConfigured = true;
        return this;
    }

    /// <summary>
    ///     Turns the tool result limit off so every tool result is stored whole.
    /// </summary>
    /// <remarks>
    ///     Without the limit, one tool result larger than the window can be neither masked nor dropped while its tool loop is
    ///     open, and the prepare call returns <see cref="Enums.PrepareOutcome.CompactionInsufficient"/> or
    ///     <see cref="Enums.PrepareOutcome.CannotCompact"/>. Use this only when every tool bounds its own output.
    /// </remarks>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithoutToolResultLimit()
    {
        this._maxToolResultTokens = null;
        this._toolResultLimitConfigured = true;
        return this;
    }

    /// <summary>
    ///     Sets the token counter used by every conversation created from this configuration.
    /// </summary>
    /// <remarks>
    ///     One instance is shared across contexts and must be safe for concurrent use. TokenGuard measures its execution time
    ///     and applies provider-reported correction on top of its estimates. Without this setting, the default is
    ///     <see cref="TokenCounting.EstimatedTokenCounter"/>.
    /// </remarks>
    /// <param name="tokenCounter">The counter to share. Cannot be <see langword="null"/>.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokenCounter"/> is <see langword="null"/>.</exception>
    public ConversationConfigBuilder WithTokenCounter(ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        this._tokenCounter = tokenCounter;
        return this;
    }

    /// <summary>
    ///     Sets the factory that creates the complete compaction strategy for each conversation.
    /// </summary>
    /// <remarks>
    ///     The factory runs once per created context and receives its timed counter, resolved logger factory and name.
    ///     Each call replaces the previous factory. A custom strategy replaces the built-in masking and summarization pipeline;
    ///     combining it with sliding-window options or a summarizer throws when <see cref="Build"/> is called.
    /// </remarks>
    /// <param name="strategyFactory">The factory for a fresh strategy per context. Cannot be <see langword="null"/>.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="strategyFactory"/> is <see langword="null"/>.</exception>
    public ConversationConfigBuilder WithCompactionStrategy(Func<CompactionStrategyContext, ICompactionStrategy> strategyFactory)
    {
        ArgumentNullException.ThrowIfNull(strategyFactory);
        this._customStrategyFactory = strategyFactory;
        return this;
    }

    /// <summary>
    ///     Sets the configuration used by the always-on sliding-window masking stage.
    /// </summary>
    /// <remarks>
    ///     When not configured, <see cref="SlidingWindowOptions.Default"/> is used. This only adjusts the masking
    ///     phase; LLM summarization is registered separately through <see cref="UseLlmSummarization"/> or the
    ///     <c>UseLlmSummarization(...)</c> methods of the provider packages.
    /// </remarks>
    /// <param name="options">The sliding-window masking configuration.</param>
    /// <returns>The current builder instance.</returns>
    public ConversationConfigBuilder WithSlidingWindowOptions(SlidingWindowOptions options)
    {
        this._slidingWindowOptions = options;
        return this;
    }

    /// <summary>
    ///     Sets the logger factory that conversation contexts created from this configuration log through.
    /// </summary>
    /// <remarks>
    ///     Without a logger factory, a context created outside dependency injection writes no log records. A context
    ///     created through <c>AddConversationContext(...)</c> uses the container's <see cref="ILoggerFactory"/> unless
    ///     this method supplies another one.
    /// </remarks>
    /// <param name="loggerFactory">The logger factory to log through. Cannot be <see langword="null"/>.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerFactory"/> is <see langword="null"/>.</exception>
    public ConversationConfigBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this._loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    ///     Adds LLM summarization through the given summarizer as the fallback compaction stage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Summarization runs when masking old tool results still leaves the context over budget. TokenGuard builds the
    ///         prompt and the transcript, sends them through <paramref name="summarizer"/>, and checks the answer; see
    ///         <see cref="ILlmSummarizer"/> for the contract.
    ///     </para>
    ///     <para>
    ///         One <paramref name="summarizer"/> instance serves every context created from the built configuration, so it
    ///         must be safe for concurrent use.
    ///     </para>
    ///     <para>
    ///         TokenGuard sets no time limit on a summarizer call. The call is bounded only by the cancellation token passed
    ///         to <c>PrepareAsync</c> and by whatever limit the summarizer applies. When a call fails, or returns a summary that
    ///         is too large for the budget, TokenGuard does not ask again until a message is added or the token budget changes.
    ///     </para>
    /// </remarks>
    /// <param name="summarizer">The model call that writes summaries. Cannot be <see langword="null"/>.</param>
    /// <param name="options">
    ///     Optional summarization options. When omitted, <see cref="LlmSummarizationOptions.Default"/> is used.
    /// </param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="summarizer"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A summarizer has already been registered on this builder.</exception>
    public ConversationConfigBuilder UseLlmSummarization(ILlmSummarizer summarizer, LlmSummarizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summarizer);

        if (this._llmSummarizer is not null)
            throw new InvalidOperationException(BuildSummarizerConflictMessage(this._llmSummarizer, summarizer));

        this._llmSummarizer = summarizer;
        this._llmSummarizationOptions = options;

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
    ///         and emergency truncation defaulting to the absolute token limit. Without <see cref="WithMaxToolResultTokens"/> or
    ///         <see cref="WithoutToolResultLimit"/>, <see cref="ConversationContextConfiguration.MaxToolResultTokens"/> is the
    ///         default for the configured maximum token count.
    ///     </para>
    ///     <para>
    ///         Token counting uses the configured counter or TokenGuard's built-in heuristic.
    ///         Without a custom strategy, compaction uses a freshly created <see cref="TieredCompactionStrategy"/> with configured
    ///         <see cref="SlidingWindowOptions"/> and an optional provider-backed <see cref="LlmSummarizationStrategy"/>.
    ///     </para>
    /// </remarks>
    /// <returns>
    ///     An immutable <see cref="ConversationContextConfiguration"/> reflecting the builder's current
    ///     configuration with all defaults applied.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="WithMaxTokens(int)"/> has not been called.
    ///     -or-
    ///     A custom strategy is combined with sliding-window options or a summarizer.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     The configured <see cref="SlidingWindowOptions"/> or <see cref="LlmSummarizationOptions"/> is the
    ///     uninitialized <see langword="default"/> value of its type.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The maximum token count is not positive.
    ///     -or-
    ///     A threshold or the overrun tolerance is not a finite number or is outside its range.
    ///     -or-
    ///     The compaction threshold is not less than the emergency threshold.
    /// </exception>
    public ConversationContextConfiguration Build()
    {
        if (!this._maxTokens.HasValue)
        {
            throw new InvalidOperationException("ConversationContextConfigurationBuilder requires WithMaxTokens(...) to be called before Build().");
        }

        if (this._customStrategyFactory is not null && (this._slidingWindowOptions is not null || this._llmSummarizer is not null))
        {
            throw new InvalidOperationException(
                "WithCompactionStrategy cannot be combined with WithSlidingWindowOptions or UseLlmSummarization.");
        }

        if (this._slidingWindowOptions is { IsInitialized: false })
        {
            throw new ArgumentException(BuildUninitializedOptionsMessage(nameof(SlidingWindowOptions)), "options");
        }

        if (this._llmSummarizationOptions is { IsInitialized: false })
        {
            throw new ArgumentException(BuildUninitializedOptionsMessage(nameof(LlmSummarizationOptions)), "options");
        }

        var defaults = ContextBudget.For(this._maxTokens.Value);
        var budget = new ContextBudget(
            this._maxTokens.Value,
            this._compactionThreshold ?? defaults.CompactionThreshold,
            this._emergencyThreshold,
            this._overrunTolerance ?? ConversationDefaults.OverrunTolerance);

        var strategyFactory = BuildStrategyFactory(
            this._slidingWindowOptions ?? SlidingWindowOptions.Default,
            this._llmSummarizer,
            this._llmSummarizationOptions);

        var customStrategyFactory = this._customStrategyFactory;
        return new ConversationContextConfiguration(budget, tokenCounter => customStrategyFactory is null
            ? strategyFactory(tokenCounter, CreateSilentDiagnostics())
            : customStrategyFactory(new CompactionStrategyContext(
                tokenCounter, NullLoggerFactory.Instance, ConversationDiagnostics.DefaultContextName)))
        {
            MaxToolResultTokens = this._toolResultLimitConfigured ? this._maxToolResultTokens : ToolResultLimitDefaults.Resolve(budget.MaxTokens),
            LoggerFactory = this._loggerFactory,
            DiagnosticStrategyFactory = customStrategyFactory is null ? strategyFactory : null,
            CustomStrategyFactory = customStrategyFactory,
            TokenCounter = this._tokenCounter,
        };
    }

    private static Func<ITokenCounter, ConversationDiagnostics, ICompactionStrategy> BuildStrategyFactory(
        SlidingWindowOptions slidingWindowOptions,
        ILlmSummarizer? llmSummarizer,
        LlmSummarizationOptions? llmSummarizationOptions)
    {
        var summarizationOptions = llmSummarizationOptions ?? LlmSummarizationOptions.Default;

        return (tokenCounter, diagnostics) =>
        {
            var loggerFactory = diagnostics.LoggerFactory;
            var llmStrategy = llmSummarizer is null
                ? null
                : new LlmSummarizationStrategy(
                    new SummarizerInvoker(
                        llmSummarizer, summarizationOptions.SummaryInstructions, loggerFactory.CreateLogger(llmSummarizer.GetType()), tokenCounter),
                    tokenCounter, summarizationOptions, loggerFactory.CreateLogger<LlmSummarizationStrategy>(), diagnostics);

            return new TieredCompactionStrategy(
                tokenCounter, slidingWindowOptions, llmStrategy, loggerFactory.CreateLogger<TieredCompactionStrategy>(),
                loggerFactory.CreateLogger<SlidingWindowStrategy>());
        };
    }

    private static ConversationDiagnostics CreateSilentDiagnostics() =>
        new(NullLoggerFactory.Instance, ConversationDiagnostics.DefaultContextName);

    private static string BuildUninitializedOptionsMessage(string optionsTypeName) =>
        $"{optionsTypeName} was not initialized. "
        + $"Create it with a constructor or use {optionsTypeName}.Default instead of default({optionsTypeName}).";

    private static string BuildSummarizerConflictMessage(ILlmSummarizer existing, ILlmSummarizer conflicting) =>
        $"LLM summarization is already registered with summarizer '{existing.GetType().Name}'. "
        + $"Only one provider can be registered per builder instance.{Environment.NewLine}"
        + $"Conflicting registration attempted with summarizer '{conflicting.GetType().Name}'. Remove one of the UseLlmSummarization calls.";
}
