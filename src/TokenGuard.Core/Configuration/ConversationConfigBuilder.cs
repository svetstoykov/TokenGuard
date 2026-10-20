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
    private ILlmSummarizer? _llmSummarizer;
    private LlmSummarizationOptions? _llmSummarizationOptions;
    private ILoggerFactory? _loggerFactory;

    /// <summary>
    ///     Creates a <see cref="ConversationContextConfiguration"/> using the default builder configuration.
    /// </summary>
    /// <remarks>
    ///     This method delegates to a new <see cref="ConversationConfigBuilder"/> instance and applies only
    ///     <see cref="WithMaxTokens(int)"/> before calling <see cref="Build"/>. When no value is supplied,
    ///     the resulting configuration uses the library default profile: 40,000 tokens, a 0.80 compaction
    ///     threshold, 1.0 emergency truncation as a last-resort safety net, TokenGuard's built-in heuristic token counting, and
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
    ///         and emergency truncation defaulting to the absolute token limit.
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

        return new ConversationContextConfiguration(budget, tokenCounter => strategyFactory(tokenCounter, CreateSilentDiagnostics()))
        {
            LoggerFactory = this._loggerFactory,
            DiagnosticStrategyFactory = strategyFactory,
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
                        llmSummarizer, summarizationOptions.SummaryInstructions, loggerFactory.CreateLogger(llmSummarizer.GetType())),
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
