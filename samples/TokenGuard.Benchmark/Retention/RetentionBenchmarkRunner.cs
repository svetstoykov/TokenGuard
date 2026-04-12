using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Samples.Benchmark.Retention;

/// <summary>
/// Runs end-to-end retention benchmarks against full and managed conversation paths.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RetentionBenchmarkRunner"/> synthesizes one conversation, replays it through two request paths, asks one
/// recall probe through a provider-agnostic delegate, then scores both outputs with identical logic. This isolates
/// compaction impact from prompt, profile, and scoring drift.
/// </para>
/// <para>
/// Managed replay records messages through public <see cref="ConversationContext"/> APIs so benchmark behavior matches
/// real TokenGuard usage instead of manipulating history internals.
/// </para>
/// </remarks>
public sealed class RetentionBenchmarkRunner
{
    private const string RecallSystemPrompt = "You are a recall assistant. Answer each question with only the fact requested.";
    private const string BaselineStrategyName = "Baseline";
    private const string ManagedStrategyName = "Managed";

    private readonly ITokenCounter _tokenCounter;
    private readonly Func<IReadOnlyList<ContextMessage>, string, Task<string>> _llmCall;
    private readonly Func<ConversationContext> _contextFactory;
    private readonly ConversationSynthesizer _synthesizer;
    private readonly RetentionScorer _scorer;

    /// <summary>
    /// Initializes a new <see cref="RetentionBenchmarkRunner"/> instance.
    /// </summary>
    /// <param name="tokenCounter">Token counter used for synthesis and request-size measurement. Cannot be <see langword="null"/>.</param>
    /// <param name="llmCall">
    /// Delegate that sends prepared <see cref="ContextMessage"/> values plus recall probe text to provider and returns raw
    /// model output. Cannot be <see langword="null"/>.
    /// </param>
    /// <param name="contextFactory">
    /// Factory that creates configured <see cref="ConversationContext"/> instances for managed benchmark runs. Cannot be
    /// <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tokenCounter"/>, <paramref name="llmCall"/>, or <paramref name="contextFactory"/> is
    /// <see langword="null"/>.
    /// </exception>
    public RetentionBenchmarkRunner(
        ITokenCounter tokenCounter,
        Func<IReadOnlyList<ContextMessage>, string, Task<string>> llmCall,
        Func<ConversationContext> contextFactory)
    {
        this._tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        this._llmCall = llmCall ?? throw new ArgumentNullException(nameof(llmCall));
        this._contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        this._synthesizer = new ConversationSynthesizer(tokenCounter);
        this._scorer = new RetentionScorer();
    }

    /// <summary>
    /// Runs retention benchmark for one scenario profile.
    /// </summary>
    /// <param name="profile">Scenario profile to synthesize and evaluate. Cannot be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token that can stop synthesis, preparation, or provider call flow.</param>
    /// <returns>Completed benchmark report for supplied profile.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is <see langword="null"/>.</exception>
    public async Task<RetentionBenchmarkReport> RunAsync(ScenarioProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ct.ThrowIfCancellationRequested();

        var conversation = this._synthesizer.Synthesize(profile);

        var baselineMessages = CreateBaselineMessages(conversation.Messages);
        var baselineTokens = this._tokenCounter.Count(baselineMessages) + this._tokenCounter.Count(ContextMessage.FromText(MessageRole.User, conversation.RecallProbe));
        var baselineResponse = await this.InvokeLlmAsync(baselineMessages, conversation.RecallProbe, ct).ConfigureAwait(false);

        var managedReplay = await this.RunManagedAsync(conversation, ct).ConfigureAwait(false);

        var baseline = this._scorer.Score(profile, baselineResponse, baselineTokens, baselineTokens, BaselineStrategyName);
        var managed = this._scorer.Score(profile, managedReplay.Response, baselineTokens, managedReplay.TokenCount, ManagedStrategyName);

        var report = new RetentionBenchmarkReport(
            profile.Name,
            baseline,
            managed,
            managed.RetentionScore - baseline.RetentionScore,
            managed.TokenSavingsPercent);

        WriteSummary(report);

        if (baseline.RetentionScore < 0.8)
        {
            Console.WriteLine($"WARNING [{profile.Name}] baseline retention {baseline.RetentionScore:P1} below 80.0%; profile may have poorly planted facts.");
        }

        return report;
    }

    /// <summary>
    /// Runs retention benchmark for multiple scenario profiles in sequence.
    /// </summary>
    /// <param name="profiles">Profiles to run sequentially. Cannot be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token that can stop sequence between or during runs.</param>
    /// <returns>Ordered list of reports in same order as input profiles.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiles"/> is <see langword="null"/>.</exception>
    public async Task<IReadOnlyList<RetentionBenchmarkReport>> RunAllAsync(IEnumerable<ScenarioProfile> profiles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var reports = new List<RetentionBenchmarkReport>();

        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();
            reports.Add(await this.RunAsync(profile, ct).ConfigureAwait(false));
        }

        return reports;
    }

    private async Task<(string Response, int TokenCount)> RunManagedAsync(SyntheticConversation conversation, CancellationToken ct)
    {
        var context = this._contextFactory();
        ArgumentNullException.ThrowIfNull(context);

        context.SetSystemPrompt(RecallSystemPrompt);

        foreach (var message in conversation.Messages)
        {
            ReplayMessage(context, message);
        }

        var preparedMessages = await context.PrepareAsync(ct).ConfigureAwait(false);
        var managedTokenCount = this._tokenCounter.Count(preparedMessages) + this._tokenCounter.Count(ContextMessage.FromText(MessageRole.User, conversation.RecallProbe));
        var managedResponse = await this.InvokeLlmAsync(preparedMessages, conversation.RecallProbe, ct).ConfigureAwait(false);

        return (managedResponse, managedTokenCount);
    }

    private async Task<string> InvokeLlmAsync(IReadOnlyList<ContextMessage> messages, string recallProbe, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = await this._llmCall(messages, recallProbe).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return response;
    }

    private static IReadOnlyList<ContextMessage> CreateBaselineMessages(IReadOnlyList<ContextMessage> conversationMessages)
    {
        var baselineMessages = new List<ContextMessage>(conversationMessages.Count + 1)
        {
            ContextMessage.FromText(MessageRole.System, RecallSystemPrompt),
        };

        baselineMessages.AddRange(conversationMessages);
        return baselineMessages;
    }

    private static void ReplayMessage(ConversationContext context, ContextMessage message)
    {
        if (message.Segments.Count == 1 && message.Segments[0] is TextContent textContent)
        {
            switch (message.Role)
            {
                case MessageRole.User:
                    context.AddUserMessage(textContent.Content);
                    return;

                case MessageRole.Model:
                    context.RecordModelResponse([textContent]);
                    return;
            }
        }

        if (message.Role == MessageRole.Model)
        {
            context.RecordModelResponse(message.Segments);
            return;
        }

        throw new InvalidOperationException(
            $"Synthetic conversation contains unsupported replay message role '{message.Role}'. Retention benchmark replay supports user and model messages only.");
    }

    private static void WriteSummary(RetentionBenchmarkReport report)
    {
        Console.WriteLine($"[{report.ProfileName}] baseline={report.Baseline.RetentionScore:P1} managed={report.Managed.RetentionScore:P1} delta={report.RetentionDelta:+0.0%;-0.0%;0.0%} token-savings={report.TokenSavingsPercent:P1}");
    }
}
