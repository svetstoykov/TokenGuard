using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;

namespace TokenGuard.Tests.Diagnostics;

public sealed class ObservabilityPrivacyTests
{
    private const string Sentinel = "SENTINEL";
    private const int CompactionCompleted = 1011;
    private const int EmergencyTruncationApplied = 1012;
    private const int SlidingWindowApplied = 2000;
    private const int ToolResultMasked = 2001;
    private const int SummarizationPathSelected = 3000;
    private const int SummaryCheckpointCreated = 3020;
    private const int TieredResultSelected = 4000;

    [Fact]
    public async Task FullCompactionCycle_AtTraceLevel_WritesNoConversationContentToLogs()
    {
        // Arrange
        var logs = new CapturingLoggerFactory(LogLevel.Trace);
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(400)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 2))
            .WithLoggerFactory(logs)
            .Build();

        // Act
        using (var context = new ConversationContextFactory(configuration).Create())
        {
            await RunConversationAsync(context);
        }

        // Assert
        logs.WithEventId(CompactionCompleted).Should().NotBeEmpty("the scripted conversation must reach compaction");
        logs.WithEventId(EmergencyTruncationApplied).Should().NotBeEmpty("the scripted conversation must reach emergency truncation");
        logs.WithEventId(SlidingWindowApplied).Should().NotBeEmpty("the scripted conversation must run the sliding window");
        logs.WithEventId(ToolResultMasked).Should().NotBeEmpty("the scripted conversation must mask tool results");
        logs.WithEventId(TieredResultSelected).Should().NotBeEmpty("the scripted conversation must run the tiered strategy");
        logs.Records.Should().OnlyContain(record => !ContainsSentinel(record));
    }

    [Fact]
    public async Task FullCompactionCycleWithSummarizer_AtTraceLevel_WritesNoConversationContentToLogs()
    {
        // Arrange
        var logs = new CapturingLoggerFactory(LogLevel.Trace);
        var summarizer = ScriptedSummarizer.Returning(Content("summary", 40));
        var builder = new ConversationConfigBuilder()
            .WithMaxTokens(400)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 2))
            .WithLoggerFactory(logs);
        builder.SetLlmSummarizer(() => summarizer, "Test", new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 10, maxSummaryTokens: 100));

        // Act
        using (var context = new ConversationContextFactory(builder.Build()).Create())
        {
            await RunConversationAsync(context);
        }

        // Assert
        summarizer.Calls.Should().BeGreaterThan(0, "the scripted conversation must reach summarization");
        logs.WithEventId(SummarizationPathSelected).Should().NotBeEmpty();
        logs.WithEventId(SummaryCheckpointCreated).Should().NotBeEmpty();
        logs.Records.Should().OnlyContain(record => !ContainsSentinel(record));
    }

    /// <summary>
    ///     Pushes sentinel-bearing content through every public recording method and enough turns to force compaction.
    /// </summary>
    /// <param name="context">The context to drive.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    internal static async Task RunConversationAsync(IConversationContext context)
    {
        context.SetSystemPrompt(Content("system-prompt", 40));
        context.SetSystemPrompt(Content("system-prompt-replacement", 40));
        context.AddPinnedMessage(MessageRole.User, Content("pinned-text", 40));
        context.AddPinnedMessage(MessageRole.Model, [new TextContent(Content("pinned-segment-a", 20)), new TextContent(Content("pinned-segment-b", 20))]);

        for (var turn = 1; turn <= 6; turn++)
        {
            context.AddUserMessage(Content($"user-{turn}", 120));
            await context.PrepareAsync();

            var callId = $"call_{turn}";
            context.RecordModelResponse(
                [new TextContent(Content($"model-{turn}", 120)), new ToolUseContent(callId, "search", Content($"tool-arguments-{turn}", 60))],
                providerInputTokens: 100 + (turn * 40));
            context.RecordToolResult(callId, "search", Content($"tool-result-{turn}", 600));
            await context.PrepareAsync();
        }
    }

    /// <summary>
    ///     Builds message content that starts with a unique sentinel and is padded to a minimum length.
    /// </summary>
    /// <param name="label">The label that makes the sentinel unique.</param>
    /// <param name="length">The minimum content length in characters.</param>
    /// <returns>The sentinel-bearing content.</returns>
    internal static string Content(string label, int length) => $"{Sentinel}-{label} ".PadRight(length, 'x');

    private static bool ContainsSentinel(CapturedLogRecord record) =>
        HasSentinel(record.Message)
        || record.State.Concat(record.Scope).Any(pair => HasSentinel(pair.Value?.ToString()))
        || HasSentinel(record.Exception?.ToString());

    private static bool HasSentinel(string? text) => text is not null && text.Contains(Sentinel, StringComparison.Ordinal);
}
