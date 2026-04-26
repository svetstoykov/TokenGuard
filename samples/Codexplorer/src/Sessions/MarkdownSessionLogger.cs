using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Codexplorer.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Persists one live session transcript as markdown while publishing the same events to runtime consumers.
/// </summary>
/// <remarks>
/// This implementation optimizes for durability and readability rather than throughput. Every append is serialized
/// through one write gate, flushed immediately, and mirrored to an async event stream so a human-readable log
/// and console rendering can stay in lockstep.
/// </remarks>
public sealed class MarkdownSessionLogger : ISessionLogger
{
    private const int ToolResultContentCap = 200;
    private const int DefaultEventContentCap = 600;
    private const int FinalAnswerContentCap = 400;
    private const int ExceptionContentCap = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Channel<SessionEvent> _events = Channel.CreateUnbounded<SessionEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    
    private readonly StreamWriter _writer;
    private bool _isClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownSessionLogger"/> class.
    /// </summary>
    /// <param name="logFilePath">The absolute transcript file path to create.</param>
    /// <param name="startedAtUtc">The UTC timestamp used for the initial session header.</param>
    /// <param name="workspace">The workspace targeted by the session.</param>
    /// <param name="sessionLabel">The human-readable session label.</param>
    /// <param name="modelName">The configured model name.</param>
    /// <param name="budget">The configured Codexplorer budget.</param>
    public MarkdownSessionLogger(
        string logFilePath,
        DateTime startedAtUtc,
        WorkspaceModel workspace,
        string sessionLabel,
        string modelName,
        BudgetOptions budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionLabel);
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

            var startedEvent = new SessionStartedEvent(startedAtUtc, workspace, sessionLabel, budget, modelName);
            this.WriteInitialHeader(startedEvent);
            this._events.Writer.TryWrite(startedEvent);
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
    public IAsyncEnumerable<SessionEvent> Events => this._events.Reader.ReadAllAsync();

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

            this._events.Writer.TryWrite(evt);

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
        this._events.Writer.TryComplete();
    }

    private string RenderEvent(SessionEvent evt)
    {
        return evt switch
        {
            SessionStartedEvent started => this.RenderStartedEvent(started),
            UserPromptEvent userPrompt => RenderUserPromptEvent(userPrompt),
            PreparedContextEvent prepared => RenderPreparedContextEvent(prepared),
            ModelRequestedEvent requested => RenderModelRequestedEvent(requested),
            ModelRespondedEvent responded => RenderModelRespondedEvent(responded),
            ToolCalledEvent toolCalled => RenderToolCalledEvent(toolCalled),
            ToolCompletedEvent toolCompleted => RenderToolCompletedEvent(toolCompleted),
            AssistantReplyEvent assistantReply => RenderAssistantReplyEvent(assistantReply),
            ExchangeOutcomeEvent exchangeOutcome => RenderExchangeOutcomeEvent(exchangeOutcome),
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
        builder.AppendLine($"- Session: `{evt.SessionLabel}`");
        builder.AppendLine();
        builder.AppendLine("## Session Started");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string RenderUserPromptEvent(UserPromptEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"## User Message {evt.ExchangeIndex + 1}");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(evt.Content, DefaultEventContentCap));
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

        if (evt.MessagesDropped > 0)
        {
            builder.AppendLine($"- MessagesDropped: {evt.MessagesDropped}");
        }

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

    private static string RenderAssistantReplyEvent(AssistantReplyEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"## Assistant Reply {evt.ExchangeIndex + 1}");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(TruncateContent(evt.Content, FinalAnswerContentCap));
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderExchangeOutcomeEvent(ExchangeOutcomeEvent evt)
    {
        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"### Exchange Outcome {evt.ExchangeIndex + 1}");
        builder.AppendLine();
        builder.AppendLine($"- TimestampUtc: {FormatTimestamp(evt.TimestampUtc)}");
        builder.AppendLine($"- Outcome: {evt.Outcome}");
        builder.AppendLine($"- Details: {evt.Details ?? "n/a"}");
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
                content = TruncateContent(toolResult.Content, ToolResultContentCap)
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

    private static bool IsTerminal(SessionEvent evt) => evt is SessionCancelledEvent or SessionFailedEvent;
}
