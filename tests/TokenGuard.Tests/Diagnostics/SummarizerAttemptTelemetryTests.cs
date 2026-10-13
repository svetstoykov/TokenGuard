using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Checks which summarizer calls are counted as failures by the metrics and by the failure-streak health signal.
/// </summary>
[Collection(TelemetryCollection.Name)]
public sealed class SummarizerAttemptTelemetryTests
{
    private const int SummarizationFailureStreakDetected = 6004;

    [Fact]
    public async Task PrepareAsync_WhenTheCallerCancelsDuringTheSummarizerCall_RecordsNeitherASummarizationDurationNorAFailure()
    {
        // Arrange
        using var telemetry = new TelemetryCapture();
        using var cancellation = new CancellationTokenSource();
        var summarizer = new ScriptedSummarizer(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var contextName = NewContextName();
        using var context = CreateOverBudgetContext(contextName, summarizer, loggerFactory: null);

        // Act
        var act = () => context.PrepareAsync(cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        summarizer.Calls.Should().Be(1);
        telemetry.MeasurementsOf("tokenguard.summarization.duration", contextName).Should().BeEmpty();
        telemetry.MeasurementsOf("tokenguard.summarization.failures", contextName).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenAFailedSummaryIsNotAttemptedAgain_CountsOneFailureAndStartsNoFailureStreak()
    {
        // Arrange
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory(LogLevel.Debug);
        var summarizer = ScriptedSummarizer.Throwing(new TimeoutException("provider timed out"));
        var contextName = NewContextName();
        using var context = CreateOverBudgetContext(contextName, summarizer, logs);

        // Act
        for (var call = 1; call <= 4; call++)
        {
            await context.PrepareAsync();
        }

        // Assert
        summarizer.Calls.Should().Be(1);
        telemetry.MeasurementsOf("tokenguard.summarization.failures", contextName).Should().ContainSingle().Which.Value.Should().Be(1);
        telemetry.MeasurementsOf("tokenguard.summarization.duration", contextName).Should().ContainSingle()
            .Which.Tag("tokenguard.result").Should().Be("failure");
        logs.WithEventId(SummarizationFailureStreakDetected).Should().BeEmpty();
    }

    /// <summary>
    ///     Creates a context through the public factory whose text-only history is over the limit, so masking cannot help and
    ///     the summarizer is asked.
    /// </summary>
    /// <param name="contextName">The profile name the context reports with.</param>
    /// <param name="summarizer">The summarizer the context uses.</param>
    /// <param name="loggerFactory">The logger factory to write to, or <see langword="null" /> to write no log records.</param>
    /// <returns>The context, holding ten recorded messages.</returns>
    private static IConversationContext CreateOverBudgetContext(string contextName, ILlmSummarizer summarizer, ILoggerFactory? loggerFactory)
    {
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_500);
        builder.SetLlmSummarizer(() => summarizer, "Test", new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 100, maxSummaryTokens: 500));

        if (loggerFactory is not null)
            builder.WithLoggerFactory(loggerFactory);

        var context = new ConversationContextFactory(ConversationConfigBuilder.Default()).AddNamed(contextName, builder.Build()).Create(contextName);

        for (var turn = 0; turn < 5; turn++)
        {
            context.AddUserMessage(Prose(200));
            context.RecordModelResponse([new TextContent(Prose(200))]);
        }

        return context;
    }

    private static string NewContextName() => $"summarizer-attempt-{Guid.NewGuid():N}";

    private static string Prose(int words) => string.Join(' ', Enumerable.Repeat("word", words));
}
