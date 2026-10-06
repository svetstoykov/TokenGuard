using Microsoft.Extensions.Logging;
using TokenGuard.Core.Defaults;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Tracks the health signals and running totals of one conversation context and reports them.
/// </summary>
/// <remarks>
///     <para>
///         A signal is a pattern across turns, such as compaction that runs on every turn. Each signal writes one log
///         record and increments <c>tokenguard.health.signals</c> when its condition starts to hold, and writes one
///         record when the condition stops. Nothing is written while a condition keeps holding.
///     </para>
///     <para>
///         The state is a fixed set of counters and flags, so observing a prepare call allocates nothing unless a signal
///         changes state. The type only reports; it never changes how the context compacts.
///     </para>
/// </remarks>
internal sealed class ConversationHealth
{
    private readonly ILogger _logger;
    private readonly ConversationDiagnostics _diagnostics;
    private readonly int[] _recentReclaimedTokens = new int[ConversationHealthDefaults.RepeatedCompactionTurns];

    private bool _estimatorDriftActive;
    private bool _repeatedCompactionActive;
    private bool _lowYieldActive;
    private bool _summarizationFailureStreakActive;
    private bool _repeatedOverBudgetActive;
    private bool _pinnedPressureActive;

    private int _compactionTurnStreak;
    private int _lastCompactionTurn = -1;
    private int _summarizationFailureStreak;
    private int _overBudgetStreak;

    private int _prepareCalls;
    private int _strategyRuns;
    private long _tokensReclaimed;
    private int _emergencyTruncations;
    private int _peakPreparedTokens;
    private double _largestDriftPercent;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConversationHealth" /> class.
    /// </summary>
    /// <param name="logger">The logger that receives the signal and summary records.</param>
    /// <param name="diagnostics">The identity of the conversation being tracked.</param>
    internal ConversationHealth(ILogger logger, ConversationDiagnostics diagnostics)
    {
        this._logger = logger;
        this._diagnostics = diagnostics;
    }

    /// <summary>
    ///     Records the start of a prepare call and evaluates pinned pressure.
    /// </summary>
    /// <param name="pinnedTokens">The token total of all pinned messages.</param>
    /// <param name="maxTokens">The configured maximum token count.</param>
    internal void OnPrepareStarted(int pinnedTokens, int maxTokens)
    {
        this._prepareCalls++;

        var underPressure = pinnedTokens > maxTokens * ConversationHealthDefaults.PinnedPressureRatio;
        if (this.Update(ref this._pinnedPressureActive, underPressure, ConversationHealthLog.PinnedPressure))
        {
            ConversationHealthLog.PinnedPressureDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, pinnedTokens, maxTokens,
                pinnedTokens * 100.0 / maxTokens);
        }
    }

    /// <summary>
    ///     Records a prepare call that returned the history without running the strategy.
    /// </summary>
    /// <param name="preparedTokens">The estimated token total of the prepared payload.</param>
    internal void OnPreparedBelowTrigger(int preparedTokens)
    {
        this._peakPreparedTokens = Math.Max(this._peakPreparedTokens, preparedTokens);
        this._compactionTurnStreak = 0;
        this._overBudgetStreak = 0;

        this.Update(ref this._repeatedCompactionActive, false, ConversationHealthLog.RepeatedCompaction);
        this.Update(ref this._lowYieldActive, false, ConversationHealthLog.LowCompactionYield);
        this.Update(ref this._repeatedOverBudgetActive, false, ConversationHealthLog.RepeatedOverBudget);
    }

    /// <summary>
    ///     Records a prepare call that ran the strategy and evaluates the signals that depend on its result.
    /// </summary>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="tokensBefore">The estimated token total before compaction.</param>
    /// <param name="tokensAfter">The estimated token total of the prepared payload.</param>
    /// <param name="summarizationError">The summarization failure reported by the strategy, or <see langword="null" />.</param>
    /// <param name="isOverBudget">Whether the prepared payload exceeds the effective maximum.</param>
    /// <param name="effectiveMaxTokens">The maximum token count plus the overrun tolerance.</param>
    /// <param name="emergencyTruncated">Whether emergency truncation removed messages.</param>
    internal void OnCompacted(
        int turn, int tokensBefore, int tokensAfter, Exception? summarizationError, bool isOverBudget, long effectiveMaxTokens,
        bool emergencyTruncated)
    {
        var reclaimed = tokensBefore - tokensAfter;
        this._strategyRuns++;
        this._tokensReclaimed += reclaimed;
        this._peakPreparedTokens = Math.Max(this._peakPreparedTokens, tokensAfter);
        if (emergencyTruncated)
            this._emergencyTruncations++;

        this.EvaluateRepeatedCompaction(turn, reclaimed);

        var lowYield = tokensBefore > 0 && reclaimed < tokensBefore * ConversationHealthDefaults.LowYieldRatio;
        if (this.Update(ref this._lowYieldActive, lowYield, ConversationHealthLog.LowCompactionYield))
        {
            ConversationHealthLog.LowCompactionYieldDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, tokensBefore, tokensAfter,
                reclaimed * 100.0 / tokensBefore);
        }

        this._summarizationFailureStreak = summarizationError is null ? 0 : this._summarizationFailureStreak + 1;
        var failing = this._summarizationFailureStreak >= ConversationHealthDefaults.SummarizationFailureStreak;
        if (this.Update(ref this._summarizationFailureStreakActive, failing, ConversationHealthLog.SummarizationFailureStreak))
        {
            ConversationHealthLog.SummarizationFailureStreakDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._summarizationFailureStreak,
                summarizationError!.GetType().Name);
        }

        this._overBudgetStreak = isOverBudget ? this._overBudgetStreak + 1 : 0;
        var repeatedlyOverBudget = this._overBudgetStreak >= ConversationHealthDefaults.RepeatedOverBudgetCalls;
        if (this.Update(ref this._repeatedOverBudgetActive, repeatedlyOverBudget, ConversationHealthLog.RepeatedOverBudget))
        {
            ConversationHealthLog.RepeatedOverBudgetDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._overBudgetStreak, tokensAfter,
                effectiveMaxTokens);
        }
    }

    /// <summary>
    ///     Records a provider-reported input token count and evaluates estimator drift.
    /// </summary>
    /// <param name="providerInputTokens">The input token count reported by the provider.</param>
    /// <param name="estimatedTokens">The token estimate of the most recently prepared payload.</param>
    internal void OnProviderTokensReported(int providerInputTokens, int estimatedTokens)
    {
        if (providerInputTokens <= 0)
            return;

        var difference = providerInputTokens - estimatedTokens;
        var driftPercent = difference * 100.0 / providerInputTokens;
        this._largestDriftPercent = Math.Max(this._largestDriftPercent, Math.Abs(driftPercent));

        var drifting = Math.Abs(difference) > providerInputTokens * ConversationHealthDefaults.EstimatorDriftRatio;
        if (this.Update(ref this._estimatorDriftActive, drifting, ConversationHealthLog.EstimatorDrift))
        {
            ConversationHealthLog.EstimatorDriftDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, estimatedTokens, providerInputTokens, driftPercent);
        }
    }

    /// <summary>
    ///     Writes the end-of-conversation summary when at least one prepare call was made.
    /// </summary>
    /// <param name="turns">The number of turns the conversation reached.</param>
    internal void LogSummary(int turns)
    {
        if (this._prepareCalls == 0)
            return;

        ConversationHealthLog.ConversationSummary(
            this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, turns, this._prepareCalls, this._strategyRuns,
            this._tokensReclaimed, this._diagnostics.SummarizerCalls, this._diagnostics.SummarizerFailures, this._emergencyTruncations,
            this._peakPreparedTokens, this._largestDriftPercent);
    }

    /// <summary>
    ///     Moves one signal to the state its condition calls for and reports the change.
    /// </summary>
    /// <remarks>
    ///     A signal that starts increments <c>tokenguard.health.signals</c>; the caller writes the signal-specific log
    ///     record. A signal that stops writes the shared cleared record here.
    /// </remarks>
    /// <param name="active">The flag that holds whether the signal is currently reported.</param>
    /// <param name="condition">Whether the condition behind the signal holds now.</param>
    /// <param name="signal">The name of the signal.</param>
    /// <param name="logger">The logger that receives the cleared record.</param>
    /// <param name="diagnostics">The identity of the conversation the signal belongs to.</param>
    /// <returns><see langword="true" /> when the signal just started; otherwise, <see langword="false" />.</returns>
    internal static bool Update(ref bool active, bool condition, string signal, ILogger logger, ConversationDiagnostics diagnostics)
    {
        if (condition == active)
            return false;

        active = condition;
        if (!condition)
        {
            ConversationHealthLog.HealthSignalCleared(logger, diagnostics.ConversationId, diagnostics.ContextName, signal);
            return false;
        }

        TokenGuardTelemetry.HealthSignals.Add(1, TokenGuardTelemetry.Tag(TokenGuardTelemetry.SignalTag, signal), diagnostics.ContextNameTag);
        return true;
    }

    private bool Update(ref bool active, bool condition, string signal) => Update(ref active, condition, signal, this._logger, this._diagnostics);

    /// <summary>
    ///     Counts consecutive turns on which the strategy ran and reports when the count reaches the threshold.
    /// </summary>
    /// <remarks>
    ///     A second prepare call on the same turn replaces that turn's reclaimed tokens instead of extending the streak.
    /// </remarks>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="reclaimed">The tokens reclaimed by the prepare call.</param>
    private void EvaluateRepeatedCompaction(int turn, int reclaimed)
    {
        if (turn != this._lastCompactionTurn)
        {
            this._compactionTurnStreak++;
            this._lastCompactionTurn = turn;
        }

        if (this._compactionTurnStreak <= this._recentReclaimedTokens.Length)
            this._recentReclaimedTokens[this._compactionTurnStreak - 1] = reclaimed;

        var repeated = this._compactionTurnStreak >= ConversationHealthDefaults.RepeatedCompactionTurns;
        if (this.Update(ref this._repeatedCompactionActive, repeated, ConversationHealthLog.RepeatedCompaction))
        {
            ConversationHealthLog.RepeatedCompactionDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._compactionTurnStreak,
                string.Join(", ", this._recentReclaimedTokens));
        }
    }
}
