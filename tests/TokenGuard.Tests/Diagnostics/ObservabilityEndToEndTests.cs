using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Checks what a tool loop reports through logs, activities, and metrics against the results it returns, with the
///     context created by the public factory and nothing replaced by a test double.
/// </summary>
[Collection(TelemetryCollection.Name)]
public sealed class ObservabilityEndToEndTests
{
    private const int MaxTokens = 600;
    private const int ToolExchanges = 8;
    private const int PrepareBelowTrigger = 1010;
    private const int CompactionCompleted = 1011;
    private const int EmergencyTruncationApplied = 1012;
    private const int SlidingWindowApplied = 2000;
    private const int ToolResultMasked = 2001;
    private const int TieredResultSelected = 4000;
    private const int ConversationSummary = 6100;

    [Fact]
    public async Task ToolLoopThatCompacts_ReportsEachPrepareResultInLogsActivitiesAndMetrics()
    {
        // Arrange
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory(LogLevel.Trace);
        var contextName = $"end-to-end-{Guid.NewGuid():N}";
        IReadOnlyList<PrepareResult> results;

        // Act
        using (var context = CreateFactory(contextName, logs).Create(contextName))
        {
            results = await RunToolLoopAsync(context);
        }

        // Assert
        var strategyRuns = results.Where(result => result.TokensBeforeCompaction >= MaxTokens * 0.8).ToArray();
        var masked = strategyRuns.Sum(result => result.MessagesCompacted - result.MessagesDropped);
        var dropped = strategyRuns.Sum(result => result.MessagesDropped);
        results.Should().Contain(result => result.Outcome == PrepareOutcome.Ready, "the loop must start below the compaction trigger");
        results.Should().Contain(
            result => result.Outcome == PrepareOutcome.Compacted && result.Messages.Any(message => message.State == CompactionState.Masked),
            "the loop must reach a prepare call that masks tool results");
        dropped.Should().BeGreaterThan(0, "the loop must reach emergency truncation");

        var prepares = telemetry.ActivitiesNamed("tokenguard.prepare")
            .Where(activity => contextName.Equals(activity.GetTagItem("tokenguard.context.name"))).ToArray();
        prepares.Select(DescribePrepare).Should().Equal(results.Select(Describe));
        prepares.Should().OnlyContain(activity => MaxTokens.Equals(activity.GetTagItem("tokenguard.tokens.max")));
        prepares.Select(activity => activity.GetTagItem("tokenguard.turn")).Should().Equal(Enumerable.Range(1, results.Count).Cast<object?>());
        prepares.Select(activity => activity.Status).Should().OnlyContain(status => status == ActivityStatusCode.Ok);
        prepares.Sum(activity => activity.Events.Where(e => e.Name == "tokenguard.emergency_truncation").Sum(e => (int)e.Tags.Single().Value!))
            .Should().Be(dropped);

        var compacts = telemetry.ActivitiesNamed("tokenguard.compact").Where(activity => prepares.Contains(activity.Parent)).ToArray();
        compacts.Should().HaveCount(strategyRuns.Length);
        compacts.Should().OnlyContain(activity => nameof(TieredCompactionStrategy).Equals(activity.GetTagItem("tokenguard.strategy")));

        logs.WithEventId(PrepareBelowTrigger).Should().HaveCount(results.Count - strategyRuns.Length);
        logs.WithEventId(CompactionCompleted).Select(DescribeCompaction).Should().Equal(strategyRuns.Select(Describe));
        logs.WithEventId(CompactionCompleted).Select(record => record.Property("EmergencyMessagesDropped"))
            .Should().Equal(strategyRuns.Select(result => (object?)result.MessagesDropped));
        logs.WithEventId(CompactionCompleted).Select(record => record.Property("StrategyName")).Distinct()
            .Should().Equal(nameof(TieredCompactionStrategy));
        logs.WithEventId(EmergencyTruncationApplied).Sum(record => (int)record.Property("MessagesDropped")!).Should().Be(dropped);
        logs.WithEventId(SlidingWindowApplied).Should().HaveCount(strategyRuns.Length);
        logs.WithEventId(SlidingWindowApplied).Sum(record => (int)record.Property("ToolResultsMasked")!)
            .Should().Be(logs.WithEventId(ToolResultMasked).Count);
        logs.WithEventId(TieredResultSelected).Should().HaveCount(strategyRuns.Length);
        logs.Records.Should().OnlyContain(record => contextName.Equals(record.Property("ContextName")));

        telemetry.MeasurementsOf("tokenguard.prepare.count", contextName).Select(m => m.Tag("tokenguard.outcome"))
            .Should().Equal(results.Select(result => (object?)result.Outcome.ToString()));
        telemetry.MeasurementsOf("tokenguard.prepare.duration", contextName).Should().HaveCount(results.Count);
        telemetry.MeasurementsOf("tokenguard.context.tokens", contextName).Select(m => m.Value)
            .Should().Equal(results.Select(result => (double)result.TokensAfterCompaction));
        telemetry.MeasurementsOf("tokenguard.compaction.duration", contextName).Should().HaveCount(strategyRuns.Length);
        telemetry.MeasurementsOf("tokenguard.compaction.tokens_reclaimed", contextName).Select(m => m.Value)
            .Should().Equal(strategyRuns.Select(result => (double)(result.TokensBeforeCompaction - result.TokensAfterCompaction)));
        SumOfKind(telemetry, contextName, "masked").Should().Be(masked);
        SumOfKind(telemetry, contextName, "dropped").Should().Be(dropped);
        SumOfKind(telemetry, contextName, "summarized").Should().Be(0);
        telemetry.MeasurementsOf("tokenguard.emergency_truncation.count", contextName)
            .Should().HaveCount(strategyRuns.Count(result => result.MessagesDropped > 0));
        telemetry.MeasurementsOf("tokenguard.estimate.error_ratio", contextName).Should().HaveCount(ToolExchanges + 1);

        var summary = logs.WithEventId(ConversationSummary).Should().ContainSingle().Subject;
        summary.Property("PrepareCalls").Should().Be(results.Count);
        summary.Property("StrategyRuns").Should().Be(strategyRuns.Length);
        summary.Property("TokensReclaimed")
            .Should().Be(strategyRuns.Sum(result => (long)(result.TokensBeforeCompaction - result.TokensAfterCompaction)));
        summary.Property("PeakPreparedTokens").Should().Be(results.Max(result => result.TokensAfterCompaction));
    }

    [Fact]
    public async Task ToolLoopThatCompacts_ReturnsTheSameResultsWithAndWithoutLogsAndTelemetry()
    {
        // Arrange
        var contextName = $"end-to-end-{Guid.NewGuid():N}";
        IReadOnlyList<PrepareResult> observed;
        IReadOnlyList<PrepareResult> unobserved;

        // Act
        using (new TelemetryCapture())
        using (var context = CreateFactory(contextName, new CapturingLoggerFactory(LogLevel.Trace)).Create(contextName))
        {
            observed = await RunToolLoopAsync(context);
        }

        using (var context = CreateFactory(contextName, loggerFactory: null).Create(contextName))
        {
            unobserved = await RunToolLoopAsync(context);
        }

        // Assert
        observed.Select(Describe).Should().Equal(unobserved.Select(Describe));
        observed.Select(DescribeMessages).Should().Equal(unobserved.Select(DescribeMessages));
    }

    private static ConversationContextFactory CreateFactory(string contextName, ILoggerFactory? loggerFactory)
    {
        var builder = new ConversationConfigBuilder().WithMaxTokens(MaxTokens).WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 2));

        if (loggerFactory is not null)
            builder.WithLoggerFactory(loggerFactory);

        return new ConversationContextFactory(ConversationConfigBuilder.Default()).AddNamed(contextName, builder.Build());
    }

    /// <summary>
    ///     Asynchronously runs one user request through a loop of tool calls, preparing before every model call.
    /// </summary>
    /// <remarks>
    ///     The provider is simulated: it reports the prepared total plus a fixed 20 tokens, as a provider does when the host
    ///     attaches tool schemas that the context does not hold.
    /// </remarks>
    /// <param name="context">The context to drive.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the result of every prepare call in
    ///     order.
    /// </returns>
    private static async Task<IReadOnlyList<PrepareResult>> RunToolLoopAsync(IConversationContext context)
    {
        var results = new List<PrepareResult>();
        context.SetSystemPrompt("You are a careful coding assistant. Call a tool again when its earlier result was removed.");
        context.AddUserMessage("Find out why OrderTests.Total_IncludesTax fails and name the file that has to change.");

        for (var exchange = 1; exchange <= ToolExchanges; exchange++)
        {
            var prepared = await context.PrepareAsync();
            results.Add(prepared);

            var callId = $"call_{exchange}";
            var toolCall = new ToolUseContent(callId, "read_file", $$"""{"path":"src/Orders/File{{exchange}}.cs"}""");
            context.RecordModelResponse(
                [new TextContent($"I will read file number {exchange}."), toolCall], providerInputTokens: prepared.TokensAfterCompaction + 20);
            context.RecordToolResult(callId, "read_file", FileContent(exchange));
        }

        var last = await context.PrepareAsync();
        results.Add(last);
        context.RecordModelResponse([new TextContent("The tax rate is never applied.")], providerInputTokens: last.TokensAfterCompaction + 20);

        return results;
    }

    private static string FileContent(int number) =>
        $$"""
        namespace Orders;

        public sealed class File{{number}}
        {
            private readonly decimal _taxRate = 0.08m;

            public decimal Total(IReadOnlyList<OrderLine> lines)
            {
                var subtotal = lines.Sum(line => line.UnitPrice * line.Quantity);
                return Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);
            }
        }
        """;

    private static double SumOfKind(TelemetryCapture telemetry, string contextName, string kind) =>
        telemetry.MeasurementsOf("tokenguard.compaction.messages", contextName)
            .Where(m => kind.Equals(m.Tag("tokenguard.kind"))).Sum(m => m.Value);

    private static string Describe(PrepareResult result) =>
        $"{result.Outcome}|{result.TokensBeforeCompaction}|{result.TokensAfterCompaction}|{result.MessagesCompacted}";

    private static string DescribePrepare(Activity activity) =>
        $"{activity.GetTagItem("tokenguard.outcome")}|{activity.GetTagItem("tokenguard.tokens.before")}"
        + $"|{activity.GetTagItem("tokenguard.tokens.after")}|{activity.GetTagItem("tokenguard.messages.compacted")}";

    private static string DescribeCompaction(CapturedLogRecord record) =>
        $"{record.Property("Outcome")}|{record.Property("TokensBefore")}|{record.Property("TokensAfter")}|{record.Property("MessagesCompacted")}";

    private static string DescribeMessages(PrepareResult result) =>
        string.Join(
            "\n",
            result.Messages.Select(m => $"{m.Role}|{m.State}|{string.Join("|", m.Segments.Select(segment => segment.Content))}"));
}
