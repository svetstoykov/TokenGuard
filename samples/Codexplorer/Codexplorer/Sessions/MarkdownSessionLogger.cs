using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Codexplorer.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Persists one query transcript as markdown while publishing the same events to runtime consumers.
/// </summary>
/// <remarks>
/// This implementation optimizes for durability and readability rather than throughput. Every append is serialized
/// through one write gate, flushed immediately, and mirrored to a replayable async event stream so a human-readable log
/// and console rendering can stay in lockstep.
/// </remarks>
public sealed class MarkdownSessionLogger : ISessionLogger
{
    private const int ToolResultContentCap = 4000;
    private const int DefaultEventContentCap = 12000;
    private const int FinalAnswerContentCap = 8000;
    private const int ExceptionContentCap = 16000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ReplayableSessionEventStream _events = new();
    private readonly StreamWriter _writer;
    private bool _isClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownSessionLogger"/> class.
    /// </summary>
    /// <param name="logFilePath">The absolute transcript file path to create.</param>
    /// <param name="startedAtUtc">The UTC timestamp used for the initial session header.</param>
    /// <param name="workspace">The workspace targeted by the query.</param>
    /// <param name="userQuery">The original user query.</param>
    /// <param name="modelName">The configured model name.</param>
    /// <param name="budget">The configured Codexplorer budget.</param>
    public MarkdownSessionLogger(
        string logFilePath,
        DateTime startedAtUtc,
        WorkspaceModel workspace,
        string userQuery,
        string modelName,
        BudgetOptions budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(budget);

        var directoryPath = Path.GetDirectoryName(logFilePath);

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        this.LogFilePath = Path.GetFullPath(logFilePath);

        try
        {
            this._writer = new StreamWriter(
                new FileStream(
                    this.LogFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var startedEvent = new SessionStartedEvent(startedAtUtc, workspace, userQuery, budget, modelName);
            this.WriteInitialHeader(startedEvent);
            this._events.Publish(startedEvent);
        }
        catch
        {
            this._writer?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public string LogFilePath { get; }

    /// <inheritdoc />
    public IAsyncEnumerable<SessionEvent> Events => this._events;

    /// <inheritdoc />
    public Task AppendAsync(SessionEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return this.AppendCoreAsync(evt, closeAfterWrite: IsTerminal(evt), ct);
    }

    /// <inheritdoc />
    public Task EndAsync(SessionEndedEvent summary, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return this.AppendCoreAsync(summary, closeAfterWrite: true, ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this._writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await this.CloseWriterAsync().ConfigureAwait(false);
        }
        finally
        {
            this._writeGate.Release();
            this._writeGate.Dispose();
        }
    }

    private void WriteInitialHeader(SessionStartedEvent evt)
    {
        this._writer.Write(this.RenderStartedEvent(evt));
        this._writer.Flush();
    }

    private async Task AppendCoreAsync(SessionEvent evt, bool closeAfterWrite, CancellationToken ct)
    {
        await this._writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (this._isClosed)
            {
                throw new InvalidOperationException("Session logger is already closed.");
            }

            var markdown = this.RenderEvent(evt);
            await this._writer.WriteAsync(markdown.AsMemory(), ct).ConfigureAwait(false);
            await this._writer.FlushAsync(ct).ConfigureAwait(false);

            this._events.Publish(evt);

            if (closeAfterWrite)
            {
                await this.CloseWriterAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            this._writeGate.Release();
        }
    }

    private async Task CloseWriterAsync()
    {
        if (this._isClosed)
        {
            return;
        }

        this._isClosed = true;

        await this._writer.FlushAsync().ConfigureAwait(false);
        await this._writer.DisposeAsync().ConfigureAwait(false);
        this._events.Complete();
    }

    private string RenderEvent(SessionEvent evt)
    {
        return evt switch
        {
            SessionStartedEvent started => this.RenderStartedEvent(started),
            PreparedContextEvent prepared => RenderPreparedContextEvent(prepared),
            ModelRequestedEvent requested => RenderModelRequestedEvent(requested),
            ModelRespondedEvent responded => RenderModelRespondedEvent(responded),
            ToolCalledEvent toolCalled => RenderToolCalledEvent(toolCalled),
            ToolCompletedEvent toolCompleted => RenderToolCompletedEvent(toolCompleted),
            FinalAnswerEvent finalAnswer => RenderFinalAnswerEvent(finalAnswer),
            SessionEndedEvent ended => RenderSessionEndedEvent(ended),
            SessionCancelledEvent cancelled => RenderSessionCancelledEvent(cancelled),
            SessionFailedEvent failed => RenderSessionFailedEvent(failed),
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt, "Unsupported session event type.")
        };
    }

    private string RenderStartedEvent(SessionStartedEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Session Transcript");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- Workspace: `{evt.Workspace.OwnerRepo}`");
        builder.AppendLine($"- WorkspacePath: `{evt.Workspace.LocalPath}`");
        builder.AppendLine($"- Model: `{evt.ModelName}`");
        builder.AppendLine(
            $"- Budget: `ContextWindowTokens={evt.Budget.ContextWindowTokens}, SoftThresholdRatio={evt.Budget.SoftThresholdRatio}, HardThresholdRatio={evt.Budget.HardThresholdRatio}, WindowSize={evt.Budget.WindowSize}`");
        builder.AppendLine();
        builder.AppendLine("## User Query");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(evt.UserQuery, DefaultEventContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderPreparedContextEvent(PreparedContextEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"## PrepareResult (turn {evt.TurnIndex})");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- TokensBeforeCompaction: {evt.TokensBeforeCompaction}");
        builder.AppendLine($"- TokensAfterCompaction: {evt.TokensAfterCompaction}");
        builder.AppendLine($"- Outcome: {evt.Outcome}");
        builder.AppendLine($"- MessagesCompacted: {evt.MessagesCompacted}");
        builder.AppendLine($"- DegradationReason: {evt.DegradationReason ?? "n/a"}");
        return builder.ToString();
    }

    private static string RenderModelRequestedEvent(ModelRequestedEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"## Model request (turn {evt.TurnIndex})");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(TruncateContent(SerializeMessages(evt.OutboundMessages), DefaultEventContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderModelRespondedEvent(ModelRespondedEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"## Model response (turn {evt.TurnIndex})");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- InputTokensReported: {FormatNullable(evt.InputTokensReported)}");
        builder.AppendLine($"- OutputTokensReported: {FormatNullable(evt.OutputTokensReported)}");
        builder.AppendLine($"- TotalTokensReported: {FormatNullable(evt.TotalTokensReported)}");
        builder.AppendLine();
        builder.AppendLine("### Assistant Content");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(evt.AssistantContent, DefaultEventContentCap));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("### Tool Calls Issued");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(TruncateContent(JsonSerializer.Serialize(evt.ToolCallsIssued, JsonOptions), DefaultEventContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderToolCalledEvent(ToolCalledEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"### Tool call: {evt.ToolName}");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine("#### Arguments");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(TruncateContent(PrettyPrintJson(evt.ArgumentsJson), DefaultEventContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderToolCompletedEvent(ToolCompletedEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"- CompletedUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- Duration: {evt.Duration}");
        builder.AppendLine("#### Result");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(evt.ResultContent, ToolResultContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderFinalAnswerEvent(FinalAnswerEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine("## Final Answer");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(evt.Content, FinalAnswerContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderSessionEndedEvent(SessionEndedEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- TotalTurns: {evt.TotalTurns}");
        builder.AppendLine($"- TotalReportedTokens: {FormatNullable(evt.TotalReportedTokens)}");
        builder.AppendLine($"- TerminalOutcome: {evt.TerminalOutcome}");
        return builder.ToString();
    }

    private static string RenderSessionCancelledEvent(SessionCancelledEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine("## Session Cancelled");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- TurnIndex: {evt.TurnIndex}");
        builder.AppendLine($"- PartialReason: {evt.PartialReason}");
        return builder.ToString();
    }

    private static string RenderSessionFailedEvent(SessionFailedEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine("## Session Failed");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(
            $"Type: {evt.ExceptionType}{Environment.NewLine}Message: {evt.Message}{Environment.NewLine}StackTrace:{Environment.NewLine}{evt.StackTrace}",
            ExceptionContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string SerializeMessages(IReadOnlyList<ContextMessage> messages)
    {
        var projection = messages.Select(message => new
        {
            role = message.Role.ToString(),
            state = message.State.ToString(),
            isPinned = message.IsPinned,
            timestampUtc = message.Timestamp.UtcDateTime,
            tokenCount = message.TokenCount,
            segments = message.Segments.Select(CreateSegmentProjection).ToArray()
        });

        return JsonSerializer.Serialize(projection, JsonOptions);
    }

    private static object CreateSegmentProjection(ContentSegment segment)
    {
        return segment switch
        {
            ToolUseContent toolUse => new
            {
                type = nameof(ToolUseContent),
                toolUse.ToolCallId,
                toolUse.ToolName,
                content = toolUse.Content
            },
            ToolResultContent toolResult => new
            {
                type = nameof(ToolResultContent),
                toolResult.ToolCallId,
                toolResult.ToolName,
                content = toolResult.Content
            },
            TextContent text => new
            {
                type = nameof(TextContent),
                content = text.Content
            },
            _ => new
            {
                type = segment.GetType().Name,
                content = segment.Content
            }
        };
    }

    private static string PrettyPrintJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    private static string TruncateContent(string? content, int cap)
    {
        var effectiveContent = content ?? string.Empty;

        if (effectiveContent.Length <= cap)
        {
            return effectiveContent;
        }

        return effectiveContent[..cap] + Environment.NewLine + $"[... truncated: event content exceeded {cap} chars ...]";
    }

    private static string FormatNullable(int? value) => value?.ToString() ?? "n/a";

    private static string FormatTimestamp(DateTime timestampUtc) => timestampUtc.ToString("O");

    private static bool IsTerminal(SessionEvent evt)
    {
        return evt is SessionCancelledEvent or SessionFailedEvent;
    }

    private sealed class ReplayableSessionEventStream : IAsyncEnumerable<SessionEvent>
    {
        private readonly object _gate = new();
        private readonly List<SessionEvent> _history = [];
        private readonly HashSet<Channel<SessionEvent>> _subscribers = [];
        private bool _isCompleted;

        public IAsyncEnumerator<SessionEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Channel<SessionEvent>? channel = null;
            SessionEvent[] snapshot;
            var completed = false;

            lock (this._gate)
            {
                snapshot = [.. this._history];
                completed = this._isCompleted;

                if (!completed)
                {
                    channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false
                    });

                    this._subscribers.Add(channel);
                }
            }

            return this.EnumerateAsync(snapshot, channel, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }

        public void Publish(SessionEvent evt)
        {
            ArgumentNullException.ThrowIfNull(evt);

            Channel<SessionEvent>[] subscribers;

            lock (this._gate)
            {
                this._history.Add(evt);
                subscribers = [.. this._subscribers];
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(evt);
            }
        }

        public void Complete()
        {
            Channel<SessionEvent>[] subscribers;

            lock (this._gate)
            {
                if (this._isCompleted)
                {
                    return;
                }

                this._isCompleted = true;
                subscribers = [.. this._subscribers];
                this._subscribers.Clear();
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryComplete();
            }
        }

        private async IAsyncEnumerable<SessionEvent> EnumerateAsync(
            IReadOnlyList<SessionEvent> snapshot,
            Channel<SessionEvent>? channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var evt in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return evt;
            }

            if (channel is null)
            {
                yield break;
            }

            try
            {
                await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return evt;
                }
            }
            finally
            {
                this.Unsubscribe(channel);
            }
        }

        private void Unsubscribe(Channel<SessionEvent> channel)
        {
            lock (this._gate)
            {
                this._subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}
