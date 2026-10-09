extern alias sample;

using System.Text.Json;
using System.Diagnostics;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using sample::Codexplorer.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using sample::Codexplorer.Agent;
using sample::Codexplorer.Automation;
using sample::Codexplorer.Configuration;
using sample::Codexplorer.Measurements;
using sample::Codexplorer.Sessions;
using sample::Codexplorer.Tools;
using sample::Codexplorer.Workspace;

namespace Codexplorer.Automation.Tests;

/// <summary>Verifies hard task budgets and finalization across real sample boundaries.</summary>
[Collection("Sample telemetry")]
public sealed class SampleSessionTests
{
    /// <summary>Verifies an exhausted budget stops before prepare or provider work begins.</summary>
    [Fact]
    public async Task ZeroBudget_StopsBeforePreparation()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(0);
        await using var session = CreateSession(fixture, new SampleChatClient(_ => throw new InvalidOperationException()), 0);

        var result = await session.SubmitAsync("inspect", CancellationToken.None);

        result.Should().BeOfType<AgentExchangeTurnBudgetReached>();
        fixture.Collector.Snapshot().PrepareRecords.Should().BeEmpty();
        fixture.Collector.Snapshot().ProviderCalls.Should().BeEmpty();
    }

    /// <summary>Verifies tool loops stop at the task cap and skip further preparation.</summary>
    [Fact]
    public async Task LastAllowedCall_ReturnsTools_StopsBeforeAnotherPrepare()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(1);
        var provider = new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion(toolCall: true)));
        await using var session = CreateSession(fixture, provider, 1);

        var result = await session.SubmitAsync("inspect", CancellationToken.None);
        var measurements = fixture.Collector.Snapshot();

        result.Should().BeOfType<AgentExchangeTurnBudgetReached>().Which.ModelTurnsCompleted.Should().Be(1);
        measurements.ProviderCalls.Should().ContainSingle().Which.Status.Should().Be("completed");
        measurements.PrepareRecords.Should().ContainSingle().Which.OpeningMessagePresent.Should().BeTrue();
        measurements.ProviderCalls[0].PrepareIndex.Should().Be(measurements.PrepareRecords[0].Index);
        measurements.ProviderCalls[0].TranscriptIndex.Should().Be(0);
    }

    /// <summary>Verifies a final reply consumes the last allowance and still completes the protocol.</summary>
    [Fact]
    public async Task LastAllowedCall_ReturnsReply_CompletesExchange()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(1);
        var provider = new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion()));
        await using var session = CreateSession(fixture, provider, 1);

        var result = await session.SubmitAsync("inspect", CancellationToken.None);

        result.Should().BeOfType<AgentReplyReceived>();
        fixture.Collector.Snapshot().ProviderCalls.Should().ContainSingle().Which.InputTokens.Should().Be(100);
    }

    /// <summary>Verifies a long exchange yields before consuming the calls reserved for wrap-up.</summary>
    [Fact]
    public async Task LongToolExchange_YieldsAtWrapUpBoundaryAndAllowsTheFinalReply()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(30);
        var calls = 0;
        var provider = new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion(toolCall: ++calls <= 26)));
        await using var session = new ExplorerSession(SampleWorkspaceManager.Workspace, fixture.CreateContext(), new SampleSessionLogger(),
            SampleSessionDirectory, Task.CompletedTask, new SampleToolRegistry(), new Lazy<ChatClient>(() => provider), [],
            new AgentOptions { MaxTurns = 50 }, new ModelOptions(), 30, fixture.Collector, wrapUpWindow: 4);

        var exploration = await session.SubmitAsync("inspect", CancellationToken.None);
        var callsAtBoundary = fixture.Collector.Snapshot().ProviderCalls.Count;
        var wrapUp = await session.SubmitAsync("wrap up", CancellationToken.None);

        exploration.Should().BeOfType<AgentExchangeMaxTurnsReached>().Which.ModelTurnsCompleted.Should().Be(26);
        callsAtBoundary.Should().Be(26);
        wrapUp.Should().BeOfType<AgentReplyReceived>().Which.ModelTurnsCompleted.Should().Be(1);
        fixture.Collector.Snapshot().ProviderCalls.Should().HaveCount(27);
    }

    /// <summary>Verifies failed provider attempts consume allowance and retain their prepare pairing.</summary>
    [Fact]
    public async Task ProviderFailure_RecordsFailedAttempt()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(1);
        await using var session = CreateSession(fixture, new SampleChatClient(_ => throw new InvalidOperationException("failed")), 1);

        var result = await session.SubmitAsync("inspect", CancellationToken.None);

        result.Should().BeOfType<AgentExchangeFailed>().Which.ModelTurnsCompleted.Should().Be(0);
        fixture.Collector.Snapshot().ProviderCalls.Should().ContainSingle().Which.Status.Should().Be("failed");
        fixture.Collector.Snapshot().ProviderCalls[0].PrepareIndex.Should().Be(1);
    }

    /// <summary>Verifies provider cancellation reaches the actual call and keeps finalization available.</summary>
    [Fact]
    public async Task ProviderCancellation_RecordsCancelledAttempt()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(1);
        var provider = new SampleChatClient(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return SampleChatClient.Completion();
        });
        await using var session = CreateSession(fixture, provider, 1);
        using var cancellation = new CancellationTokenSource();
        var submission = session.SubmitAsync("inspect", cancellation.Token);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        var result = await submission.WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().BeOfType<AgentExchangeCancelled>().Which.ModelTurnsCompleted.Should().Be(0);
        fixture.Collector.Snapshot().ProviderCalls.Should().ContainSingle().Which.Status.Should().Be("cancelled");
    }

    /// <summary>Verifies tools receive the work cancellation token.</summary>
    [Fact]
    public async Task ToolCancellation_EndsSessionAfterCompletedProviderCall()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(2);
        var tools = new SampleToolRegistry(waitForCancellation: true);
        await using var session = CreateSession(
            fixture, new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion(toolCall: true))), 2, tools);
        using var cancellation = new CancellationTokenSource();
        var submission = session.SubmitAsync("inspect", cancellation.Token);
        await tools.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        var result = await submission.WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().BeOfType<AgentExchangeCancelled>().Which.ModelTurnsCompleted.Should().Be(1);
        fixture.Collector.Snapshot().ProviderCalls.Should().ContainSingle().Which.Status.Should().Be("completed");
    }

    /// <summary>Verifies terminal submit snapshots include disposal's real conversation summary.</summary>
    /// <param name="terminal">The terminal scenario.</param>
    [Theory]
    [InlineData("budget_exceeded")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("turn_budget_reached")]
    public async Task TerminalSubmit_TakesMeasurementsAfterDisposal(string terminal)
    {
        using var fixture = new SampleTelemetryFixture();
        using var cancellation = new CancellationTokenSource();
        var provider = new SampleChatClient(ct =>
        {
            if (terminal == "cancelled")
            {
                cancellation.Cancel();
                return Task.FromCanceled<ChatCompletion>(ct);
            }

            return terminal == "failed"
                ? throw new InvalidOperationException()
                : Task.FromResult(SampleChatClient.Completion(toolCall: true));
        });
        var explorer = new SampleExplorerAgent(budget => CreateSession(fixture, provider, budget));
        await using var registry = new AutomationSessionRegistry(NullLogger<AutomationSessionRegistry>.Instance);
        var dispatcher = new AutomationCommandDispatcher(
            explorer, new SampleWorkspaceManager(), registry, fixture.Collector, new EffectiveSettings());
        var opened = await dispatcher.DispatchAsync(Request("open_session", new { repositoryUrl = "https://github.com/a/b", modelCallBudget = 1 }),
            CancellationToken.None);
        var id = Result(opened).GetProperty("sessionId").GetString();
        var message = terminal == "budget_exceeded" ? string.Concat(Enumerable.Repeat("alpha beta gamma ", 60_000)) : "inspect";
        var submitted = await dispatcher.DispatchAsync(Request("submit", new { sessionId = id, message }), cancellation.Token);
        var result = Result(submitted);

        result.GetProperty("outcome").GetString().Should().Be(terminal);
        result.GetProperty("sessionOpen").GetBoolean().Should().BeFalse();
        result.GetProperty("measurements").GetProperty("complete").GetBoolean().Should().BeTrue();
        result.GetProperty("measurements").GetProperty("summaryCrossCheck").GetString().Should().Be("matched");
    }

    /// <summary>Verifies overlapping opens are rejected before creating another context.</summary>
    [Fact]
    public async Task SecondOpen_IsRejectedWhileFirstSessionIsActive()
    {
        using var fixture = new SampleTelemetryFixture();
        var explorer = new SampleExplorerAgent(
            budget => CreateSession(fixture, new SampleChatClient(_ => throw new InvalidOperationException()), budget));
        await using var registry = new AutomationSessionRegistry(NullLogger<AutomationSessionRegistry>.Instance);
        var dispatcher = new AutomationCommandDispatcher(
            explorer, new SampleWorkspaceManager(), registry, fixture.Collector, new EffectiveSettings());
        var request = Request("open_session", new { repositoryUrl = "https://github.com/a/b", modelCallBudget = 1 });
        var first = await dispatcher.DispatchAsync(request, CancellationToken.None);
        explorer.SessionFactory = _ => throw new InvalidOperationException("A second context must not be created.");

        var second = await dispatcher.DispatchAsync(request, CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Error!.Code.Should().Be("session_already_open");
    }

    /// <summary>Verifies close responses include final summary and cumulative provider usage.</summary>
    [Fact]
    public async Task CloseSession_TakesFinalSnapshotAfterDisposal()
    {
        using var fixture = new SampleTelemetryFixture();
        var explorer = new SampleExplorerAgent(budget => CreateSession(
            fixture, new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion())), budget));
        await using var registry = new AutomationSessionRegistry(NullLogger<AutomationSessionRegistry>.Instance);
        var dispatcher = new AutomationCommandDispatcher(
            explorer, new SampleWorkspaceManager(), registry, fixture.Collector, new EffectiveSettings());
        var opened = await dispatcher.DispatchAsync(Request("open_session", new { repositoryUrl = "https://github.com/a/b", modelCallBudget = 1 }),
            CancellationToken.None);
        var id = Result(opened).GetProperty("sessionId").GetString();
        await dispatcher.DispatchAsync(Request("submit", new { sessionId = id, message = "inspect" }), CancellationToken.None);

        var closed = await dispatcher.DispatchAsync(Request("close_session", new { sessionId = id }), CancellationToken.None);
        var measurements = Result(closed).GetProperty("measurements");

        measurements.GetProperty("complete").GetBoolean().Should().BeTrue();
        measurements.GetProperty("summaryCrossCheck").GetString().Should().Be("matched");
        measurements.GetProperty("providerCalls").GetArrayLength().Should().Be(1);
    }

    /// <summary>Verifies close marks the task failed when its final measurements disagree.</summary>
    [Fact]
    public async Task CloseSession_WithSummaryMismatch_ReturnsFailedStatus()
    {
        using var fixture = new SampleTelemetryFixture();
        var explorer = new SampleExplorerAgent(budget => CreateSession(
            fixture, new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion())), budget));
        await using var registry = new AutomationSessionRegistry(NullLogger<AutomationSessionRegistry>.Instance);
        var dispatcher = new AutomationCommandDispatcher(
            explorer, new SampleWorkspaceManager(), registry, fixture.Collector, new EffectiveSettings());
        var opened = await dispatcher.DispatchAsync(Request("open_session", new { repositoryUrl = "https://github.com/a/b", modelCallBudget = 1 }),
            CancellationToken.None);
        var id = Result(opened).GetProperty("sessionId").GetString();
        await dispatcher.DispatchAsync(Request("submit", new { sessionId = id, message = "inspect" }), CancellationToken.None);
        fixture.Collector.ObserveMeasurement("tokenguard.summarization.failures", 1, []);

        var closed = await dispatcher.DispatchAsync(Request("close_session", new { sessionId = id }), CancellationToken.None);

        Result(closed).GetProperty("status").GetString().Should().Be("failed");
        Result(closed).GetProperty("measurements").GetProperty("summaryCrossCheck").GetString().Should().Be("mismatched");
    }

    /// <summary>Verifies EOF interrupts an active provider call and permits a terminal response.</summary>
    [Fact]
    public async Task RunnerDisconnect_CancelsActiveSubmissionAndWritesFinalMeasurements()
    {
        using var fixture = new SampleTelemetryFixture();
        var provider = new SampleChatClient(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return SampleChatClient.Completion();
        });
        var explorer = new SampleExplorerAgent(budget => CreateSession(fixture, provider, budget));
        var registry = new AutomationSessionRegistry(NullLogger<AutomationSessionRegistry>.Instance);
        var dispatcher = new AutomationCommandDispatcher(
            explorer, new SampleWorkspaceManager(), registry, fixture.Collector, new EffectiveSettings());
        var channel = new SampleProtocolChannel();
        await using var host = new AutomationHost(channel, dispatcher, registry, NullLogger<AutomationHost>.Instance);
        var running = host.RunAsync(CancellationToken.None);
        await channel.Input.Writer.WriteAsync(JsonSerializer.Serialize(
            Request("open_session", new { repositoryUrl = "https://github.com/a/b", modelCallBudget = 2 }),
            AutomationProtocolJson.SerializerOptions));
        var opened = await channel.Output.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var id = Result(opened).GetProperty("sessionId").GetString();
        await channel.Input.Writer.WriteAsync(JsonSerializer.Serialize(
            Request("submit", new { sessionId = id, message = "inspect" }), AutomationProtocolJson.SerializerOptions));
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        channel.Input.Writer.TryComplete();
        var terminal = Result(await channel.Output.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        await running.WaitAsync(TimeSpan.FromSeconds(5));

        terminal.GetProperty("outcome").GetString().Should().Be("cancelled");
        var measurements = terminal.GetProperty("measurements");
        measurements.GetProperty("complete").GetBoolean().Should().BeTrue();
        measurements.GetProperty("summaryCrossCheck").GetString().Should().Be("matched");
        measurements.GetProperty("providerCalls")[0].GetProperty("status").GetString().Should().Be("cancelled");
    }

    /// <summary>Verifies the full retained opening text is compared against typed prepared user messages.</summary>
    /// <param name="mode">The replacement mode.</param>
    /// <param name="expected">Whether the full opening survives the second prepare.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("summary", false)]
    [InlineData("dropped", false)]
    [InlineData("normalized", false)]
    [InlineData("wrongRole", false)]
    [InlineData("multipleSegments", false)]
    public async Task Prepare_TracksOriginalOpeningAcrossContinuation(string mode, bool expected)
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(null);
        var context = new PreparedContext(fixture.Collector, mode);
        var provider = new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion()));
        await using var session = new ExplorerSession(SampleWorkspaceManager.Workspace, context, new SampleSessionLogger(),
            SampleSessionDirectory, Task.CompletedTask, new SampleToolRegistry(), new Lazy<ChatClient>(() => provider), [],
            new AgentOptions { MaxTurns = 5 }, new ModelOptions(), collector: fixture.Collector);
        await session.SubmitAsync("opening **ABC123**", CancellationToken.None);
        var previous = fixture.Collector.Snapshot();

        await session.SubmitAsync("continue", CancellationToken.None);

        previous.PrepareRecords.Single().OpeningMessagePresent.Should().BeTrue();
        fixture.Collector.Snapshot().PrepareRecords.Last().OpeningMessagePresent.Should().Be(expected);
        previous.PrepareRecords.Single().OpeningMessagePresent.Should().BeTrue();
    }

    /// <summary>Verifies returned failure outcomes retain opening evidence and throwing prepares retain null.</summary>
    /// <param name="mode">The completed or throwing outcome.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("CannotCompact")]
    [InlineData("CompactionInsufficient")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task Prepare_RecordsEvidenceOnlyForReturnedResults(string mode)
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(null);
        var provider = new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion()));
        await using var session = new ExplorerSession(SampleWorkspaceManager.Workspace, new PreparedContext(fixture.Collector, mode),
            new SampleSessionLogger(), SampleSessionDirectory, Task.CompletedTask, new SampleToolRegistry(), new Lazy<ChatClient>(() => provider), [],
            new AgentOptions { MaxTurns = 5 }, new ModelOptions(), collector: fixture.Collector);

        await session.SubmitAsync("opening", CancellationToken.None);

        var record = fixture.Collector.Snapshot().PrepareRecords.Single();
        record.OpeningMessagePresent.Should().Be(mode is "throw" or "cancel" ? null : true);
        record.Status.Should().Be(mode is "throw" or "cancel" ? "incomplete" : "completed");
    }

    /// <summary>Verifies nullable opening evidence and independent counters cross the sample/runner JSON boundary.</summary>
    /// <param name="opening">The opening evidence.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Measurements_WireRoundTripPreservesNewEvidence(bool? opening)
    {
        var measurements = new SessionMeasurements
        {
            MessagesMasked = 2, MessagesSummarized = 3,
            PrepareRecords = [new PrepareMeasurement { Index = 1, OpeningMessagePresent = opening }],
        };
        var json = JsonSerializer.Serialize(measurements, AutomationProtocolJson.SerializerOptions);
        var restored = JsonSerializer.Deserialize<Codexplorer.Measurements.SessionMeasurements>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        restored.MessagesMasked.Should().Be(2);
        restored.MessagesSummarized.Should().Be(3);
        restored.PrepareRecords.Single().OpeningMessagePresent.Should().Be(opening);
    }

    private sealed class PreparedContext(SessionMeasurementCollector collector, string mode) : IConversationContext
    {
        private readonly List<ContextMessage> _history = [];
        private string? _opening;
        private int _prepares;

        /// <inheritdoc />
        public IReadOnlyList<ContextMessage> History => this._history;

        /// <inheritdoc />
        public void AddUserMessage(string text)
        {
            this._opening ??= text;
            this._history.Add(ContextMessage.FromText(MessageRole.User, text));
        }

        /// <inheritdoc />
        public Task<PrepareResult> PrepareAsync(CancellationToken cancellationToken = default)
        {
            var activity = new Activity("tokenguard.prepare");
            this._prepares++;
            if (mode is "throw" or "cancel")
            {
                collector.ObserveActivity(activity);
                return Task.FromException<PrepareResult>(mode == "cancel" ? new OperationCanceledException() : new InvalidOperationException());
            }
            var outcome = Enum.TryParse<PrepareOutcome>(mode, out var parsed) ? parsed : PrepareOutcome.Ready;
            activity.SetTag("tokenguard.outcome", outcome.ToString());
            activity.SetTag("tokenguard.tokens.before", 10);
            activity.SetTag("tokenguard.tokens.after", 10);
            collector.ObserveActivity(activity);
            var opening = ContextMessage.FromText(MessageRole.User, this._opening!);
            IReadOnlyList<ContextMessage> messages = this._prepares == 1 || mode == "unchanged" ? [opening] : mode switch
            {
                "dropped" => [this._history.Last()],
                "normalized" => [ContextMessage.FromText(MessageRole.User, this._opening!.Replace("*", ""))],
                "wrongRole" => [opening with { Role = MessageRole.Model }],
                "multipleSegments" => [opening with { Segments = [new TextContent(this._opening!), new TextContent("extra")] }],
                _ => [ContextMessage.FromText(MessageRole.User, "summary ABC123"), this._history.Last()],
            };
            return Task.FromResult(new PrepareResult(messages, outcome, 10, 10, 0));
        }

        /// <inheritdoc />
        public void RecordModelResponse(IEnumerable<ContentSegment> content, int? providerInputTokens = null)
        {
        }

        /// <inheritdoc />
        public void RecordToolResult(string toolCallId, string toolName, string content)
        {
        }

        /// <inheritdoc />
        public void SetSystemPrompt(string text) => throw new NotSupportedException();

        /// <inheritdoc />
        public void AddPinnedMessage(MessageRole role, string text) => throw new NotSupportedException();

        /// <inheritdoc />
        public void AddPinnedMessage(MessageRole role, IEnumerable<ContentSegment> content) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    private static SessionDirectory SampleSessionDirectory => new(Path.Combine(Path.GetTempPath(), "sample-test-session"));

    private static ExplorerSession CreateSession(
        SampleTelemetryFixture fixture, ChatClient client, int? budget, SampleToolRegistry? tools = null)
    {
        var context = fixture.CreateContext();
        return new ExplorerSession(SampleWorkspaceManager.Workspace, context, new SampleSessionLogger(), SampleSessionDirectory,
            Task.CompletedTask, tools ?? new SampleToolRegistry(), new Lazy<ChatClient>(() => client), [], new AgentOptions { MaxTurns = 5 },
            new ModelOptions(), budget, fixture.Collector);
    }

    private static AutomationRequestEnvelope Request(string command, object payload) =>
        new() { RequestId = "request", Command = command, Payload = JsonSerializer.SerializeToElement(payload) };

    private static JsonElement Result(AutomationResponseEnvelope response) =>
        JsonSerializer.SerializeToElement(response, AutomationProtocolJson.SerializerOptions).GetProperty("result");
}
