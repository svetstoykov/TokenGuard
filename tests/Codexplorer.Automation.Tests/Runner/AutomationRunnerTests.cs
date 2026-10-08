using Codexplorer.Automation;
using Codexplorer.Automation.Client;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using Codexplorer.Automation.Reporting;
using Codexplorer.Automation.Runner;
using Codexplorer.Measurements;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.Runner;

/// <summary>Verifies task execution and partial-report finalization.</summary>
public sealed class AutomationRunnerTests : IDisposable
{
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), "tg-runner-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this._outputDirectory))
        {
            Directory.Delete(this._outputDirectory, recursive: true);
        }
    }

    /// <summary>Verifies the run folder is named by the UTC start time and the arm and receives the report.</summary>
    /// <param name="arm">The treatment or control arm.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("treatment")]
    [InlineData("control")]
    public async Task RunAsync_NewRun_CreatesRunFolderNamedByStartTimeAndArm(string arm)
    {
        var fixture = this.CreateFixture(arm, []);
        fixture.Client.Submit = (_, _) => Task.FromResult(Response("failed", Snapshot(1, complete: true), open: false));

        await fixture.Runner.RunAsync(CancellationToken.None);

        var runFolder = Path.Combine(this._outputDirectory, "20261008-141502-" + arm);
        Directory.Exists(runFolder).Should().BeTrue();
        fixture.Writer.OutputDirectory.Should().Be(runFolder);
    }

    /// <summary>Verifies an existing run folder stops the run before any task starts.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_RunFolderExists_FailsBeforeRunningAnyTask()
    {
        var fixture = this.CreateFixture();
        Directory.CreateDirectory(Path.Combine(this._outputDirectory, "20261008-141502-treatment"));

        var run = () => fixture.Runner.RunAsync(CancellationToken.None);

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        fixture.Client.OpenRequest.Should().BeNull();
        fixture.Writer.Report.Should().BeNull();
    }

    /// <summary>Verifies each task opens its session in a run-folder directory named by its task identifier.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_SeveralTasks_PassesSessionDirectoryNamedByTaskId()
    {
        var fixture = this.CreateFixture("first", "second");
        var sessionDirectories = new List<string?>();
        fixture.Client.Submit = (_, _) =>
        {
            sessionDirectories.Add(fixture.Client.OpenRequest!.SessionDirectory);
            return Task.FromResult(Response("failed", Snapshot(1, complete: true), open: false));
        };

        await fixture.Runner.RunAsync(CancellationToken.None);

        var runFolder = Path.Combine(this._outputDirectory, "20261008-141502-treatment");
        sessionDirectories.Should().Equal(Path.Combine(runFolder, "first"), Path.Combine(runFolder, "second"));
    }

    /// <summary>Verifies an exhausted call allowance stops without helper work.</summary>
    [Fact]
    public async Task RunAsync_NoCallsRemain_StopsWithoutAskingHelper()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (_, _) => Task.FromResult(Response("max_turns_reached", Snapshot(3), asksRunner: true));
        fixture.Client.Close = (_, _) => Task.FromResult(new CloseSessionResponse("session", "closed", Snapshot(3, complete: true)));

        var exit = await fixture.Runner.RunAsync(CancellationToken.None);

        exit.Should().Be(1);
        fixture.Writer.Report!.Partial.Should().BeFalse();
        fixture.Writer.Report!.Tasks.Single().Outcome.Should().Be("turn_budget_reached");
        fixture.Writer.Report.Tasks.Single().Metrics.HelperCalls.Should().Be(0);
        fixture.Writer.Report.Tasks.Single().Metrics.BudgetOvershoot.Should().Be(0);
        fixture.Client.OpenRequest!.ModelCallBudget.Should().Be(3);
        fixture.Client.OpenRequest.WrapUpWindow.Should().Be(1);
    }

    /// <summary>Verifies budget-stop report coverage depends on measurement completeness in either arm.</summary>
    /// <param name="arm">The treatment or control arm.</param>
    /// <param name="complete">Whether the terminal snapshot contains complete measurements.</param>
    /// <param name="expectedPartial">The expected report coverage status.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("treatment", true, false)]
    [InlineData("control", true, false)]
    [InlineData("treatment", false, true)]
    [InlineData("control", false, true)]
    public async Task RunAsync_BudgetStop_PartialTracksMeasurementCompleteness(string arm, bool complete, bool expectedPartial)
    {
        var fixture = this.CreateFixture(arm, []);
        fixture.Client.Submit = (_, _) => Task.FromResult(Response("turn_budget_reached", Snapshot(3, complete), open: false));

        var exit = await fixture.Runner.RunAsync(CancellationToken.None);

        exit.Should().Be(1);
        var report = fixture.Writer.Report!;
        report.Partial.Should().Be(expectedPartial);
        report.UnrunTaskIds.Should().BeEmpty();
        report.Tasks.Single().ProtocolCompletion.Should().BeFalse();
        report.Tasks.Single().MeasurementsComplete.Should().Be(complete);
        if (complete)
        {
            ReportValidator.Validate(report).Should().BeEmpty();
        }
    }

    /// <summary>Verifies manifest preflight failures surface actionable diagnostics and preserve an existing report.</summary>
    /// <param name="failure">The manifest defect to exercise.</param>
    /// <param name="diagnostic">The required diagnostic fragment.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("missingTaskId", "Tasks:0:TaskId' is required")]
    [InlineData("missingFile", "does not exist")]
    [InlineData("invalidJson", "contains invalid JSON")]
    [InlineData("nullTasks", "must provide at least one task")]
    public async Task RunAsync_InvalidManifest_SurfacesDiagnosticWithoutReplacingReport(string failure, string diagnostic)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tg-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (failure != "missingFile")
            {
                var content = failure switch
                {
                    "invalidJson" => "{",
                    "nullTasks" => "{\"tasks\":null}",
                    _ => """
                        {"tasks":[{"title":"Task","repositoryUrl":"https://github.com/example/repo",
                        "initialPrompt":"Do not modify repository source files."}]}
                        """
                };
                await File.WriteAllTextAsync(manifestPath, content);
            }

            var reportPath = Path.Combine(directory, "run-report.json");
            await File.WriteAllTextAsync(reportPath, "existing report");
            var options = Options.Create(new CodexplorerAutomationOptions { ManifestPath = manifestPath, OutputDirectory = directory });
            var manifest = new AutomationTaskManifestLoader(options, NullLogger<AutomationTaskManifestLoader>.Instance);
            var runner = new AutomationRunner(new FakeTransport(), new FakeClient(), manifest, new FakeHelper(), new FakeIdentity(),
                new ReportAggregator(), new JsonRunReportWriter(), options, NullLogger<AutomationRunner>.Instance);

            var run = () => runner.RunAsync(CancellationToken.None);

            var thrown = await run.Should().ThrowAsync<OptionsValidationException>();
            thrown.Which.Failures.Should().Contain(message => message.Contains(diagnostic, StringComparison.Ordinal));
            (await File.ReadAllTextAsync(reportPath)).Should().Be("existing report");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies a repository preflight failure preserves its diagnostic and an existing report.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_InvalidRepository_SurfacesDiagnosticWithoutReplacingReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tg-repository-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "run-report.json");
            await File.WriteAllTextAsync(reportPath, "existing report");
            var options = Options.Create(new CodexplorerAutomationOptions { OutputDirectory = directory, RepositoryPath = directory });
            var runner = new AutomationRunner(new FakeTransport(), new FakeClient(), new FakeManifest([]), new FakeHelper(),
                new GitRepositoryIdentityReader(), new ReportAggregator(), new JsonRunReportWriter(), options,
                NullLogger<AutomationRunner>.Instance);

            var run = () => runner.RunAsync(CancellationToken.None);

            await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("Git could not read the configured repository identity.");
            (await File.ReadAllTextAsync(reportPath)).Should().Be("existing report");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies the wrap-up window takes priority over a helper question.</summary>
    [Fact]
    public async Task RunAsync_QuestionWithinWrapUpWindow_SendsWrapUpAndCompletesProtocol()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (request, _) => Task.FromResult(request.Message == "initial"
            ? Response("reply_received", Snapshot(2), asksRunner: true)
            : Response("reply_received", Snapshot(3)));
        fixture.Client.Close = (_, _) => Task.FromResult(new CloseSessionResponse("session", "closed", Snapshot(3, complete: true)));

        var exit = await fixture.Runner.RunAsync(CancellationToken.None);

        exit.Should().Be(0);
        fixture.Writer.Report!.Tasks.Single().ProtocolCompletion.Should().BeTrue();
        fixture.Writer.Report.Tasks.Single().Metrics.HelperCalls.Should().Be(0);
        fixture.Writer.Report.Tasks.Single().Metrics.ModelCallsMade.Should().Be(3);
    }

    /// <summary>Verifies cumulative snapshots replace prior call counts.</summary>
    [Fact]
    public async Task RunAsync_CumulativeSnapshots_UsesLatestCallCountForWrapUp()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (request, _) => Task.FromResult(Response("reply_received", Snapshot(request.Message switch
        {
            "initial" => 1,
            var continuation when continuation == AutomationRunnerPrompts.CreateContinuationPrompt(2) => 2,
            _ => 3
        })));
        fixture.Client.Close = (_, _) => Task.FromResult(new CloseSessionResponse("session", "closed", Snapshot(3, complete: true)));

        var exit = await fixture.Runner.RunAsync(CancellationToken.None);

        exit.Should().Be(0);
        fixture.Writer.Report!.Tasks.Single().ProtocolCompletion.Should().BeTrue();
        fixture.Writer.Report.Tasks.Single().Metrics.ModelCallsMade.Should().Be(3);
    }

    /// <summary>Verifies a task failure allows later tasks to execute.</summary>
    [Fact]
    public async Task RunAsync_TaskFailure_ContinuesWithLaterTask()
    {
        var fixture = this.CreateFixture("first", "second");
        fixture.Client.Submit = (_, _) => Task.FromResult(Response("failed", Snapshot(1, complete: true), open: false));

        await fixture.Runner.RunAsync(CancellationToken.None);

        fixture.Writer.Report!.Tasks.Select(task => task.TaskId).Should().Equal("first", "second");
        fixture.Writer.Report.Partial.Should().BeTrue();
        fixture.Writer.Report.UnrunTaskIds.Should().BeEmpty();
    }

    /// <summary>Verifies cancellation during opening preserves the active task and unrun IDs.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileOpening_PreservesStartedTaskAndRemainingIds()
    {
        var fixture = this.CreateFixture("active", "unrun");
        using var cancellation = new CancellationTokenSource();
        fixture.Client.Open = (_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        var exit = await fixture.Runner.RunAsync(cancellation.Token);

        exit.Should().Be(1);
        fixture.Writer.Report!.Tasks.Single().TaskId.Should().Be("active");
        fixture.Writer.Report.Tasks.Single().Outcome.Should().Be("cancelled");
        fixture.Writer.Report.Tasks.Single().MeasurementsComplete.Should().BeFalse();
        fixture.Writer.Report.UnrunTaskIds.Should().Equal("unrun");
    }

    /// <summary>Verifies a protocol cancellation during opening records a cancelled task.</summary>
    [Fact]
    public async Task RunAsync_CancelledOpeningReturnsProtocolError_ClassifiesTaskAsCancelled()
    {
        var fixture = this.CreateFixture("active", "unrun");
        using var cancellation = new CancellationTokenSource();
        fixture.Client.Open = (_, _) =>
        {
            cancellation.Cancel();
            throw new CodexplorerAutomationProtocolException("request", "cancelled", "cancelled");
        };

        await fixture.Runner.RunAsync(cancellation.Token);

        fixture.Writer.Report!.Tasks.Single().Outcome.Should().Be("cancelled");
    }

    /// <summary>Verifies cancellation preserves the terminal provider-attempt snapshot.</summary>
    [Fact]
    public async Task RunAsync_CancellationReturnsTerminalSnapshot_PreservesCancelledAttempt()
    {
        var fixture = this.CreateFixture("active", "unrun");
        using var cancellation = new CancellationTokenSource();
        var snapshot = Snapshot(2, complete: true);
        snapshot = snapshot with
        {
            ProviderCalls = snapshot.ProviderCalls.Select((call, index) => index == 1 ? call with { Status = "cancelled" } : call).ToArray()
        };
        fixture.Client.Submit = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(Response("cancelled", snapshot, open: false));
        };

        await fixture.Runner.RunAsync(cancellation.Token);

        var task = fixture.Writer.Report!.Tasks.Single();
        task.Outcome.Should().Be("cancelled");
        task.MeasurementsComplete.Should().BeTrue();
        task.Metrics.ModelCallsMade.Should().Be(2);
        task.Metrics.CompletedModelTurns.Should().Be(1);
        fixture.Writer.Report.UnrunTaskIds.Should().Equal("unrun");
    }

    /// <summary>Verifies an empty helper response retains its reported usage.</summary>
    [Fact]
    public async Task RunAsync_EmptyHelperResponse_RecordsUsageBeforeFailing()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (_, _) => Task.FromResult(Response("reply_received", Snapshot(1), asksRunner: true));
        fixture.Helper.Response = new RunnerHelperAiResult(null, new UsageMeasurement { InputTokens = 17, OutputTokens = 8 });

        await fixture.Runner.RunAsync(CancellationToken.None);

        var task = fixture.Writer.Report!.Tasks.Single();
        task.Outcome.Should().Be("failed");
        task.Metrics.HelperCalls.Should().Be(1);
        task.Metrics.HelperInputTokens.Should().Be(17);
        task.Metrics.HelperOutputTokens.Should().Be(8);
    }

    /// <summary>Verifies fatal transport failure retains the active task and stops later tasks.</summary>
    [Fact]
    public async Task RunAsync_FatalTransportAfterSnapshot_RetainsActiveTaskAndStopsRun()
    {
        var fixture = this.CreateFixture("active", "unrun");
        fixture.Client.Submit = (request, _) => request.Message == "initial"
            ? Task.FromResult(Response("reply_received", Snapshot(1)))
            : throw new CodexplorerAutomationTransportException("Disconnected");
        fixture.Client.Close = (_, _) => throw new CodexplorerAutomationTransportException("Disconnected");

        await fixture.Runner.RunAsync(CancellationToken.None);

        var task = fixture.Writer.Report!.Tasks.Single();
        task.Metrics.ModelCallsMade.Should().Be(1);
        task.MeasurementsComplete.Should().BeFalse();
        fixture.Writer.Report.UnrunTaskIds.Should().Equal("unrun");
    }

    /// <summary>Verifies a failed disposal cross-check marks the task failed.</summary>
    [Fact]
    public async Task RunAsync_SummaryCrossCheckFailsOnDisposal_FailsTask()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (request, _) => Task.FromResult(Response("reply_received", Snapshot(request.Message == "initial" ? 2 : 3)));
        fixture.Client.Close = (_, _) => Task.FromResult(new CloseSessionResponse("session", "closed",
            Snapshot(3, complete: true) with { SummaryCrossCheck = "mismatched" }));

        var exit = await fixture.Runner.RunAsync(CancellationToken.None);

        exit.Should().Be(1);
        fixture.Writer.Report!.Tasks.Single().Outcome.Should().Be("failed");
        fixture.Writer.Report.Tasks.Single().ProtocolCompletion.Should().BeFalse();
        fixture.Writer.Report.Partial.Should().BeTrue();
    }

    /// <summary>Verifies a close failure after wrap-up produces a consistent partial report.</summary>
    /// <param name="cancelled">Whether cleanup fails because cancellation was requested.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CloseFailsAfterWrapUp_ReportsFailureWithoutProtocolCompletion(bool cancelled)
    {
        var fixture = this.CreateFixture("active", "unrun");
        using var cancellation = new CancellationTokenSource();
        fixture.Client.Submit = (request, _) => Task.FromResult(Response("reply_received", Snapshot(request.Message == "initial" ? 2 : 3)));
        fixture.Client.Close = (_, _) =>
        {
            if (cancelled)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            throw new CodexplorerAutomationTransportException("Disconnected after wrap-up");
        };

        var exit = await fixture.Runner.RunAsync(cancellation.Token);
        var report = fixture.Writer.Report!;

        exit.Should().Be(1);
        report.Tasks.Single().Outcome.Should().Be(cancelled ? "cancelled" : "failed");
        report.Tasks.Single().ProtocolCompletion.Should().BeFalse();
        report.Totals.ProtocolCompletedTaskCount.Should().Be(0);
        report.Partial.Should().BeTrue();
        report.UnrunTaskIds.Should().Equal("unrun");
        ReportValidator.Validate(report).Should().BeEmpty();
    }

    /// <summary>Verifies report-writing failure returns a nonzero exit code.</summary>
    [Fact]
    public async Task RunAsync_WriterFailure_ReturnsNonzero()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (_, _) => Task.FromResult(Response("failed", Snapshot(0, complete: true), open: false));
        fixture.Writer.Fail = true;

        var exit = await fixture.Runner.RunAsync(CancellationToken.None);

        exit.Should().Be(1);
    }

    /// <summary>Verifies the reply that ended the task is written in full to the capture folder of its session directory.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_CaptureOn_WritesFinalAnswerFromLastReply()
    {
        var fixture = this.CreateFixture();
        fixture.Client.Submit = (request, _) => Task.FromResult(request.Message == "initial"
            ? Response("reply_received", Snapshot(2)) with { AssistantText = "interim reply" }
            : Response("reply_received", Snapshot(3)) with { AssistantText = "final reply" });
        fixture.Client.Close = (_, _) => Task.FromResult(new CloseSessionResponse("session", "closed", Snapshot(3, complete: true)));

        await fixture.Runner.RunAsync(CancellationToken.None);

        var finalAnswerPath = Path.Combine(this._outputDirectory, "20261008-141502-treatment", "task", "capture", "final-answer.md");
        (await File.ReadAllTextAsync(finalAnswerPath)).Should().Be("final reply");
        fixture.Client.OpenRequest!.Capture.Should().BeTrue();
    }

    /// <summary>Verifies a run with capture off asks for no capture and writes no final answer.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_CaptureOff_WritesNoFinalAnswer()
    {
        var fixture = new Fixture("treatment", [], this._outputDirectory, capture: false);
        fixture.Client.Submit = (_, _) =>
            Task.FromResult(Response("failed", Snapshot(1, complete: true), open: false) with { AssistantText = "partial reply" });

        await fixture.Runner.RunAsync(CancellationToken.None);

        fixture.Client.OpenRequest!.Capture.Should().BeFalse();
        Directory.Exists(Path.Combine(this._outputDirectory, "20261008-141502-treatment", "task", "capture")).Should().BeFalse();
    }

    private Fixture CreateFixture(params string[] ids) => new("treatment", ids, this._outputDirectory);

    private Fixture CreateFixture(string arm, string[] ids) => new(arm, ids, this._outputDirectory);

    private static SubmitResponse Response(string outcome, SessionMeasurements measurements, bool asksRunner = false, bool open = true) =>
        new("session", outcome, null, false, measurements.ProviderCalls.Count, null, open, asksRunner,
            asksRunner ? "question" : null, "session.log", null, null, measurements);

    private static SessionMeasurements Snapshot(int calls, bool complete = false) => new()
    {
        ModelCallBudget = 3, Complete = complete, SummaryCrossCheck = complete ? "matched" : "pending",
        PrepareRecords = Enumerable.Range(1, calls).Select(index => new PrepareMeasurement
        {
            Index = index, Turn = index, Status = "completed", Outcome = "Ready", TokensBefore = 10, TokensAfter = 10
        }).ToArray(),
        ProviderCalls = Enumerable.Range(1, calls).Select(index => new ProviderCallMeasurement
        {
            TranscriptIndex = index, PrepareIndex = index, Status = "completed", InputTokens = 10, OutputTokens = 2
        }).ToArray()
    };

    private sealed class Fixture
    {
        /// <summary>Initializes a new instance of the <see cref="Fixture" /> class.</summary>
        /// <param name="arm">The treatment or control arm.</param>
        /// <param name="ids">The task IDs, or empty to use a single default task.</param>
        /// <param name="outputDirectory">The directory that receives the run folder.</param>
        /// <param name="capture">Whether sessions capture their model calls.</param>
        public Fixture(string arm, string[] ids, string outputDirectory, bool capture = true)
        {
            var budget = new TurnBudgetProfile { MaxTurns = 3, WrapUpWindow = 1 };
            var options = Options.Create(new CodexplorerAutomationOptions
            {
                ManifestPath = null,
                OutputDirectory = outputDirectory,
                Capture = capture,
                Arm = arm,
                TurnBudgets = new AutomationTurnBudgetOptions { Small = budget, Medium = budget, Large = budget },
                Tasks = (ids.Length == 0 ? new[] { "task" } : ids).Select(id => new AutomationTaskDefinition
                {
                    TaskId = id, Title = "Task", RepositoryUrl = "https://github.com/example/repo",
                    InitialPrompt = "initial"
                }).ToArray()
            });
            this.Runner = new AutomationRunner(new FakeTransport(), this.Client, new FakeManifest(options.Value.Tasks), this.Helper,
                new FakeIdentity(), new ReportAggregator(), this.Writer, options, NullLogger<AutomationRunner>.Instance, new FixedTimeProvider());
        }

        /// <summary>Gets the configurable protocol boundary.</summary>
        public FakeClient Client { get; } = new();
        /// <summary>Gets the configurable helper boundary.</summary>
        public FakeHelper Helper { get; } = new();
        /// <summary>Gets the captured report boundary.</summary>
        public FakeWriter Writer { get; } = new();
        /// <summary>Gets the runner under test.</summary>
        public AutomationRunner Runner { get; }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 14, 15, 2, TimeSpan.Zero);
    }

    private sealed class FakeManifest(IReadOnlyList<AutomationTaskDefinition> tasks) : IAutomationTaskManifestLoader
    {
        /// <inheritdoc />
        public IReadOnlyList<AutomationTaskDefinition> LoadTasks() => tasks;
        /// <inheritdoc />
        public AutomationManifestSnapshot LoadSnapshot() => new(tasks, "inline", new string('a', 64), "inline");
    }

    private sealed class FakeIdentity : IRepositoryIdentityReader
    {
        /// <inheritdoc />
        public Task<RepositoryIdentity> ReadAsync(string? repositoryPath, CancellationToken ct) =>
            Task.FromResult(new RepositoryIdentity(new string('a', 40), false));
    }

    private sealed class FakeWriter : IRunReportWriter
    {
        /// <summary>Gets the report captured during finalization.</summary>
        public RunReport? Report { get; private set; }
        /// <summary>Gets or sets whether report writing fails.</summary>
        public bool Fail { get; set; }
        /// <summary>Gets the directory the report was written to.</summary>
        public string? OutputDirectory { get; private set; }
        /// <inheritdoc />
        public Task WriteAsync(RunReport report, string outputDirectory)
        {
            this.Report = report;
            this.OutputDirectory = outputDirectory;
            return this.Fail ? Task.FromException(new IOException("Cannot write")) : Task.CompletedTask;
        }
    }

    private sealed class FakeHelper : IRunnerHelperAi
    {
        /// <summary>Gets or sets the helper response.</summary>
        public RunnerHelperAiResult? Response { get; set; }
        /// <inheritdoc />
        public Task<RunnerHelperAiResult> AnswerAsync(RunnerHelperAiRequest request, CancellationToken ct) =>
            Task.FromResult(this.Response ?? throw new InvalidOperationException("Unexpected helper call"));
    }

    private sealed class FakeTransport : IAutomationProtocolTransport
    {
        /// <inheritdoc />
        public int? ProcessId => null;
        /// <inheritdoc />
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        /// <inheritdoc />
        public Task<AutomationResponseEnvelope> SendAsync(AutomationRequestEnvelope request, CancellationToken ct) =>
            throw new NotSupportedException();
        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeClient : ICodexplorerAutomationClient
    {
        /// <summary>Gets or sets the session-opening operation.</summary>
        public Func<OpenSessionRequest, CancellationToken, Task<OpenSessionResponse>>? Open { get; set; }
        /// <summary>Gets or sets the submission operation.</summary>
        public Func<SubmitRequest, CancellationToken, Task<SubmitResponse>> Submit { get; set; } = (_, _) => throw new NotSupportedException();
        /// <summary>Gets or sets the session-closing operation.</summary>
        public Func<CloseSessionRequest, CancellationToken, Task<CloseSessionResponse>> Close { get; set; } =
            (_, _) => Task.FromResult(new CloseSessionResponse("session", "closed", Snapshot(1, complete: true)));
        /// <summary>Gets the most recent session-opening request.</summary>
        public OpenSessionRequest? OpenRequest { get; private set; }
        /// <inheritdoc />
        public Task<AutomationPingResult> PingAsync(CancellationToken ct) => Task.FromResult(new AutomationPingResult("ok", 1,
            new EffectiveSettings
            {
                AgentModel = "agent", SummarizerModel = "summary", ContextWindowTokens = 16000,
                MaxOutputTokens = 512, ExchangeMaxTurns = 10
            }));
        /// <inheritdoc />
        public Task<OpenSessionResponse> OpenSessionAsync(OpenSessionRequest request, CancellationToken ct)
        {
            this.OpenRequest = request;
            return this.Open?.Invoke(request, ct) ?? Task.FromResult(new OpenSessionResponse("session",
                new AutomationWorkspace("name", "owner/repo", "/workspace", DateTime.UnixEpoch, 0), "session.log"));
        }

        /// <inheritdoc />
        public Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken ct) => this.Submit(request, ct);
        /// <inheritdoc />
        public Task<CloseSessionResponse> CloseSessionAsync(CloseSessionRequest request, CancellationToken ct) => this.Close(request, ct);
    }
}
