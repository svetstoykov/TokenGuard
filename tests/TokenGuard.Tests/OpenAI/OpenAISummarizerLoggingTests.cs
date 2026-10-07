using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.OpenAI;

public sealed class OpenAISummarizerLoggingTests
{
    private const int CallStarting = 5000;
    private const int CallCompleted = 5001;
    private const int CallFailed = 5002;
    private const string Summary = "SENTINEL-summary of the older turns";

    private const string CompletionJson = """
        {
          "id": "chatcmpl-1", "object": "chat.completion", "created": 1, "model": "gpt-test",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "SENTINEL-summary of the older turns" }, "finish_reason": "stop" }
          ],
          "usage": { "prompt_tokens": 11, "completion_tokens": 7, "total_tokens": 18 }
        }
        """;

    private const string EmptyCompletionJson = """
        {
          "id": "chatcmpl-2", "object": "chat.completion", "created": 1, "model": "gpt-test",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "" }, "finish_reason": "length" }
          ],
          "usage": {
            "prompt_tokens": 11, "completion_tokens": 20, "total_tokens": 31,
            "completion_tokens_details": { "reasoning_tokens": 19 }
          }
        }
        """;

    [Fact]
    public async Task SummarizeAsync_WhenTheCallSucceeds_LogsOneRecordBeforeAndOneAfter()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, HttpStatusCode.OK, CompletionJson);

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        var records = SummarizerRecords(logs);
        records.Select(record => record.EventId.Id).Should().Equal(CallStarting, CallCompleted);
        records.Should().OnlyContain(record => record.Level == LogLevel.Debug);

        var starting = logs.WithEventId(CallStarting).Single();
        starting.Property("Provider").Should().Be("OpenAI");
        starting.Property("MessageCount").Should().Be(3);
        starting.Property("TargetTokens").Should().Be(20);

        var completed = logs.WithEventId(CallCompleted).Single();
        completed.Property("SummaryLength").Should().Be(Summary.Length);
        completed.Property("InputTokens").Should().Be(11L);
        completed.Property("OutputTokens").Should().Be(7L);
        completed.Property("ElapsedMilliseconds").Should().BeOfType<double>();
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheCallFails_LogsOneFailedRecordAndLetsTheExceptionPropagate()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(
            logs, HttpStatusCode.BadRequest, """{ "error": { "message": "bad request", "type": "invalid_request_error" } }""");

        // Act
        var result = await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        var thrown = result.SummarizationError.Should().NotBeNull().And.Subject;
        SummarizerRecords(logs).Select(record => record.EventId.Id).Should().Equal(CallStarting, CallFailed);

        var failed = logs.WithEventId(CallFailed).Single();
        failed.Level.Should().Be(LogLevel.Debug);
        failed.Exception.Should().BeNull();
        failed.Property("ExceptionType").Should().Be(thrown!.GetType().Name);
        failed.Property("ElapsedMilliseconds").Should().BeOfType<double>();
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheCallSucceeds_LogsTheFinishReasonOnTheCompletedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, HttpStatusCode.OK, CompletionJson);

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        var completed = logs.WithEventId(CallCompleted).Single();
        completed.Property("FinishReason").Should().Be("stop");
        completed.Property("ReasoningTokens").Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheCallSucceeds_TagsTheSummarizeActivityWithTheFinishReasonAndOutputTokens()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        using var root = new Activity("test-root").Start();
        var strategy = CreateStrategy(new CapturingLoggerFactory(), HttpStatusCode.OK, CompletionJson);

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        var summarize = capture.ActivitiesNamed("tokenguard.summarize").Should().ContainSingle(activity => activity.TraceId == root.TraceId).Subject;
        summarize.GetTagItem("tokenguard.finish_reason").Should().Be("stop");
        summarize.GetTagItem("tokenguard.tokens.output").Should().Be(7L);
        summarize.GetTagItem("tokenguard.tokens.reasoning").Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheAnswerIsEmpty_ThrowsWithTheFinishReasonAndTokenCountsInTheMessage()
    {
        // Arrange
        var strategy = CreateStrategy(new CapturingLoggerFactory(), HttpStatusCode.OK, EmptyCompletionJson);

        // Act
        var result = await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        result.SummarizationError.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be(
            "OpenAI summarization returned an empty answer. Finish reason: length; output tokens: 20; reasoning tokens: 19.");
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheAnswerIsEmpty_LogsTheFinishReasonAndTokenCountsOnTheFailedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, HttpStatusCode.OK, EmptyCompletionJson);

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        SummarizerRecords(logs).Select(record => record.EventId.Id).Should().Equal(CallStarting, CallFailed);

        var failed = logs.WithEventId(CallFailed).Single();
        failed.Property("ExceptionType").Should().Be(nameof(InvalidOperationException));
        failed.Property("FinishReason").Should().Be("length");
        failed.Property("OutputTokens").Should().Be(20L);
        failed.Property("ReasoningTokens").Should().Be(19L);
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheAnswerIsEmpty_TagsTheSummarizeActivityWithTheFinishReasonAndTokenCounts()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        using var root = new Activity("test-root").Start();
        var strategy = CreateStrategy(new CapturingLoggerFactory(), HttpStatusCode.OK, EmptyCompletionJson);

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        var summarize = capture.ActivitiesNamed("tokenguard.summarize").Should().ContainSingle(activity => activity.TraceId == root.TraceId).Subject;
        summarize.Status.Should().Be(ActivityStatusCode.Error);
        summarize.GetTagItem("tokenguard.finish_reason").Should().Be("length");
        summarize.GetTagItem("tokenguard.tokens.output").Should().Be(20L);
        summarize.GetTagItem("tokenguard.tokens.reasoning").Should().Be(19L);
    }

    [Fact]
    public async Task SummarizeAsync_WhenTheRequestIsRejected_LogsNoResponseValuesOnTheFailedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(
            logs, HttpStatusCode.BadRequest, """{ "error": { "message": "bad request", "type": "invalid_request_error" } }""");

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        var failed = logs.WithEventId(CallFailed).Single();
        failed.Property("FinishReason").Should().BeNull();
        failed.Property("OutputTokens").Should().BeNull();
        failed.Property("ReasoningTokens").Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_NeverWritesPromptOrSummaryTextToAnyRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, HttpStatusCode.OK, CompletionJson);

        // Act
        await strategy.CompactAsync(CreateMessages(), availableTokens: 60);

        // Assert
        SummarizerRecords(logs).Should().NotBeEmpty();
        logs.Records.Should().OnlyContain(record =>
            !record.Message.Contains("SENTINEL") && record.State.All(pair => !$"{pair.Value}".Contains("SENTINEL")));
    }

    private static IReadOnlyList<CapturedLogRecord> SummarizerRecords(CapturingLoggerFactory logs) =>
        logs.Records.Where(record => record.Category == "TokenGuard.Extensions.OpenAI.OpenAISummarizer").ToArray();

    /// <summary>
    ///     Builds the strategy the way <c>UseLlmSummarization</c> wires it, with the OpenAI client answering from a stub.
    /// </summary>
    private static ICompactionStrategy CreateStrategy(CapturingLoggerFactory logs, HttpStatusCode statusCode, string json)
    {
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(new StubHttpMessageHandler(statusCode, json))),
        };
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(1_000)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20))
            .UseLlmSummarization(
                new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options),
                new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50))
            .Build();

        return configuration.DiagnosticStrategyFactory!(new TextLengthTokenCounter(), new ConversationDiagnostics(logs, "default"));
    }

    private static ContextMessage[] CreateMessages() =>
        Enumerable.Range(0, 5)
            .Select(index => ContextMessage.FromText(index % 2 == 0 ? MessageRole.User : MessageRole.Model, $"SENTINEL-{index}".PadRight(20, 'x')))
            .ToArray();
}
