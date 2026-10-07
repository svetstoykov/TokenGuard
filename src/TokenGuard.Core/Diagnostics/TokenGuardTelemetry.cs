using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the activity source, the meter, and the instruments shared by TokenGuard and its extension packages.
/// </summary>
/// <remarks>
///     Activity names, instrument names, and tag names are a public contract for dashboards and alerts. Tags carry
///     identifiers, names, and counts only, never conversation content, and metric tags never carry a conversation ID.
/// </remarks>
internal static class TokenGuardTelemetry
{
    /// <summary>
    ///     The name of the activity that covers one prepare call.
    /// </summary>
    internal const string PrepareActivityName = "tokenguard.prepare";

    /// <summary>
    ///     The name of the activity that covers one compaction strategy call.
    /// </summary>
    internal const string CompactActivityName = "tokenguard.compact";

    /// <summary>
    ///     The name of the activity that covers one summarizer call.
    /// </summary>
    internal const string SummarizeActivityName = "tokenguard.summarize";

    /// <summary>
    ///     The name of the activity event added when emergency truncation removed messages.
    /// </summary>
    internal const string EmergencyTruncationEventName = "tokenguard.emergency_truncation";

    /// <summary>
    ///     The tag that holds the conversation identifier. Used on activities only.
    /// </summary>
    internal const string ConversationIdTag = "tokenguard.conversation.id";

    /// <summary>
    ///     The tag that holds the configuration name of the conversation.
    /// </summary>
    internal const string ContextNameTag = "tokenguard.context.name";

    /// <summary>
    ///     The tag that holds the turn number of a prepare call.
    /// </summary>
    internal const string TurnTag = "tokenguard.turn";

    /// <summary>
    ///     The tag that holds the configured maximum token count.
    /// </summary>
    internal const string MaxTokensTag = "tokenguard.tokens.max";

    /// <summary>
    ///     The tag that holds a token total before compaction.
    /// </summary>
    internal const string TokensBeforeTag = "tokenguard.tokens.before";

    /// <summary>
    ///     The tag that holds a token total after compaction.
    /// </summary>
    internal const string TokensAfterTag = "tokenguard.tokens.after";

    /// <summary>
    ///     The tag that holds the token budget handed to the compaction strategy.
    /// </summary>
    internal const string AvailableTokensTag = "tokenguard.tokens.available";

    /// <summary>
    ///     The tag that holds the summary size requested from the summarizer.
    /// </summary>
    internal const string TargetTokensTag = "tokenguard.tokens.target";

    /// <summary>
    ///     The tag that holds the output tokens the provider reported for a summarizer call.
    /// </summary>
    internal const string OutputTokensTag = "tokenguard.tokens.output";

    /// <summary>
    ///     The tag that holds the reasoning tokens the provider reported for a summarizer call.
    /// </summary>
    internal const string ReasoningTokensTag = "tokenguard.tokens.reasoning";

    /// <summary>
    ///     The tag that holds the reason the provider gave for ending a summarizer answer.
    /// </summary>
    internal const string FinishReasonTag = "tokenguard.finish_reason";

    /// <summary>
    ///     The tag that holds the outcome of a prepare call.
    /// </summary>
    internal const string OutcomeTag = "tokenguard.outcome";

    /// <summary>
    ///     The tag that holds the name reported by the compaction strategy.
    /// </summary>
    internal const string StrategyTag = "tokenguard.strategy";

    /// <summary>
    ///     The tag that holds the number of messages compacted or dropped by a prepare call.
    /// </summary>
    internal const string MessagesCompactedTag = "tokenguard.messages.compacted";

    /// <summary>
    ///     The tag that holds the number of messages changed by the compaction strategy.
    /// </summary>
    internal const string MessagesAffectedTag = "tokenguard.messages.affected";

    /// <summary>
    ///     The tag that holds the number of messages dropped by emergency truncation.
    /// </summary>
    internal const string MessagesDroppedTag = "tokenguard.messages.dropped";

    /// <summary>
    ///     The tag that holds the number of messages sent to the summarizer.
    /// </summary>
    internal const string MessageCountTag = "tokenguard.messages.count";

    /// <summary>
    ///     The tag that holds how a message was changed: <c>masked</c>, <c>summarized</c>, or <c>dropped</c>.
    /// </summary>
    internal const string KindTag = "tokenguard.kind";

    /// <summary>
    ///     The tag that holds the result of a summarizer call: <c>success</c> or <c>failure</c>.
    /// </summary>
    internal const string ResultTag = "tokenguard.result";

    /// <summary>
    ///     The tag that holds the name of a health signal.
    /// </summary>
    internal const string SignalTag = "tokenguard.signal";

    /// <summary>
    ///     The <see cref="KindTag" /> value for messages whose tool results were replaced with placeholders.
    /// </summary>
    internal const string MaskedKind = "masked";

    /// <summary>
    ///     The <see cref="KindTag" /> value for messages replaced by a summary.
    /// </summary>
    internal const string SummarizedKind = "summarized";

    /// <summary>
    ///     The <see cref="KindTag" /> value for messages removed by emergency truncation.
    /// </summary>
    internal const string DroppedKind = "dropped";

    /// <summary>
    ///     The <see cref="ResultTag" /> value for a summarizer call that returned a summary.
    /// </summary>
    internal const string SuccessResult = "success";

    /// <summary>
    ///     The <see cref="ResultTag" /> value for a summarizer call that threw.
    /// </summary>
    internal const string FailureResult = "failure";

    private static readonly string? Version =
        typeof(TokenGuardTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    ///     The source of every TokenGuard activity.
    /// </summary>
    internal static readonly ActivitySource ActivitySource = new(TokenGuardDiagnostics.ActivitySourceName, Version);

    /// <summary>
    ///     The meter that owns every TokenGuard instrument.
    /// </summary>
    internal static readonly Meter Meter = new(TokenGuardDiagnostics.MeterName, Version);

    /// <summary>
    ///     Counts prepare calls that returned a result, tagged with outcome.
    /// </summary>
    internal static readonly Counter<long> PrepareCount =
        Meter.CreateCounter<long>("tokenguard.prepare.count", "{call}", "Prepare calls that returned a result.");

    /// <summary>
    ///     Records the duration of each prepare call that returned a result, tagged with outcome.
    /// </summary>
    internal static readonly Histogram<double> PrepareDuration =
        Meter.CreateHistogram<double>("tokenguard.prepare.duration", "s", "Duration of a prepare call.");

    /// <summary>
    ///     Records the duration of each compaction strategy call, tagged with strategy name.
    /// </summary>
    internal static readonly Histogram<double> CompactionDuration =
        Meter.CreateHistogram<double>("tokenguard.compaction.duration", "s", "Duration of a compaction strategy call.");

    /// <summary>
    ///     Records the duration of each summarizer call, tagged with result.
    /// </summary>
    internal static readonly Histogram<double> SummarizationDuration =
        Meter.CreateHistogram<double>("tokenguard.summarization.duration", "s", "Duration of a summarizer call.");

    /// <summary>
    ///     Records the time spent counting tokens within one prepare call.
    /// </summary>
    internal static readonly Histogram<double> TokenCountingDuration =
        Meter.CreateHistogram<double>("tokenguard.token_counting.duration", "s", "Time spent counting tokens within one prepare call.");

    /// <summary>
    ///     Records the estimated size of each prepared payload, tagged with outcome.
    /// </summary>
    internal static readonly Histogram<int> ContextTokens =
        Meter.CreateHistogram<int>("tokenguard.context.tokens", "{token}", "Estimated tokens in a prepared payload.");

    /// <summary>
    ///     Records the tokens removed by each prepare call that ran the compaction strategy.
    /// </summary>
    internal static readonly Histogram<int> TokensReclaimed =
        Meter.CreateHistogram<int>("tokenguard.compaction.tokens_reclaimed", "{token}", "Tokens removed by a prepare call that ran compaction.");

    /// <summary>
    ///     Counts messages changed by compaction, tagged with kind.
    /// </summary>
    internal static readonly Counter<long> CompactionMessages =
        Meter.CreateCounter<long>("tokenguard.compaction.messages", "{message}", "Messages masked, summarized, or dropped by compaction.");

    /// <summary>
    ///     Counts summarizer calls that threw.
    /// </summary>
    internal static readonly Counter<long> SummarizationFailures =
        Meter.CreateCounter<long>("tokenguard.summarization.failures", "{failure}", "Summarizer calls that threw.");

    /// <summary>
    ///     Counts prepare calls in which emergency truncation removed messages.
    /// </summary>
    internal static readonly Counter<long> EmergencyTruncations =
        Meter.CreateCounter<long>(
            "tokenguard.emergency_truncation.count", "{truncation}", "Prepare calls in which emergency truncation removed messages.");

    /// <summary>
    ///     Records the signed estimator error as a fraction of the provider-reported input tokens.
    /// </summary>
    internal static readonly Histogram<double> EstimateErrorRatio =
        Meter.CreateHistogram<double>(
            "tokenguard.estimate.error_ratio", "1",
            "Provider-reported input tokens minus the last estimate, divided by the provider-reported value.");

    /// <summary>
    ///     Counts health signals that started, tagged with signal.
    /// </summary>
    internal static readonly Counter<long> HealthSignals =
        Meter.CreateCounter<long>("tokenguard.health.signals", "{signal}", "Conversation health signals that started.");

    /// <summary>
    ///     Returns the tag value for a prepare outcome.
    /// </summary>
    /// <param name="outcome">The outcome to name.</param>
    /// <returns>The name of the outcome.</returns>
    internal static string OutcomeName(PrepareOutcome outcome) =>
        outcome switch
        {
            PrepareOutcome.Ready => nameof(PrepareOutcome.Ready),
            PrepareOutcome.Compacted => nameof(PrepareOutcome.Compacted),
            PrepareOutcome.CompactionInsufficient => nameof(PrepareOutcome.CompactionInsufficient),
            PrepareOutcome.CannotCompact => nameof(PrepareOutcome.CannotCompact),
            _ => outcome.ToString(),
        };

    /// <summary>
    ///     Creates a metric or activity tag.
    /// </summary>
    /// <param name="name">The tag name.</param>
    /// <param name="value">The tag value.</param>
    /// <returns>The tag.</returns>
    internal static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);

    /// <summary>
    ///     Adds what the provider reported about a summarizer answer to the current summarize activity.
    /// </summary>
    /// <remarks>
    ///     Provider summarizers call this as soon as a response arrives, so the tags are present on an answer that is
    ///     then rejected as empty. A value that is <see langword="null" /> adds no tag. Nothing is added when the
    ///     current activity is not the <see cref="SummarizeActivityName" /> activity of <see cref="ActivitySource" />.
    /// </remarks>
    /// <param name="finishReason">The provider's reason for ending the answer, or <see langword="null" /> when not reported.</param>
    /// <param name="outputTokens">The output tokens reported by the provider, or <see langword="null" /> when not reported.</param>
    /// <param name="reasoningTokens">The reasoning tokens reported by the provider, or <see langword="null" /> when not reported.</param>
    internal static void RecordSummarizerResponse(string? finishReason, long? outputTokens, long? reasoningTokens)
    {
        if (Activity.Current is not { OperationName: SummarizeActivityName } activity || activity.Source != ActivitySource)
            return;

        if (finishReason is not null)
            activity.SetTag(FinishReasonTag, finishReason);

        if (outputTokens is not null)
            activity.SetTag(OutputTokensTag, outputTokens.Value);

        if (reasoningTokens is not null)
            activity.SetTag(ReasoningTokensTag, reasoningTokens.Value);
    }
}
