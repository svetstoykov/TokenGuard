using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Codexplorer.Tools;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace Codexplorer.Sessions;

/// <summary>
///     Records the model calls of one session as files in its <c>capture</c> folder.
/// </summary>
/// <remarks>
///     <para>
///         The tool schemas are written once to <c>tools.json</c> when the capture is created. Each model call adds one
///         line to <c>exchanges.jsonl</c>, flushed as it is written, so a failed or cancelled session keeps every call
///         up to that point.
///     </para>
///     <para>
///         Only conversation content is recorded. Nothing from the HTTP layer reaches the files, so they hold no
///         credential or header.
///     </para>
/// </remarks>
internal sealed class JsonlSessionCapture : ISessionCapture
{
    private static readonly JsonSerializerOptions LineOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ToolsOptions = new(LineOptions) { WriteIndented = true };

    private readonly StreamWriter _writer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="JsonlSessionCapture" /> class and writes the tool schemas.
    /// </summary>
    /// <param name="captureDirectory">The absolute path of the capture folder, which is created when absent.</param>
    /// <param name="tools">The tool schemas published to the model for the session. Cannot be <see langword="null" />.</param>
    public JsonlSessionCapture(string captureDirectory, IReadOnlyList<ToolSchema> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureDirectory);
        ArgumentNullException.ThrowIfNull(tools);

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Directory.CreateDirectory(captureDirectory);
        File.WriteAllText(Path.Combine(captureDirectory, "tools.json"), JsonSerializer.Serialize(tools, ToolsOptions), encoding);
        this._writer = new StreamWriter(
            new FileStream(Path.Combine(captureDirectory, "exchanges.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), encoding);
    }

    /// <inheritdoc />
    public async Task WriteExchangeAsync(
        int modelCall, IReadOnlyList<ContextMessage> messages, string status, CapturedResponse? response, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        var line = new ExchangeLine(modelCall, DateTime.UtcNow, status, messages.Select(CreateMessageLine).ToArray(), response);
        await this._writer.WriteLineAsync(JsonSerializer.Serialize(line, LineOptions).AsMemory(), ct).ConfigureAwait(false);
        await this._writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => this._writer.DisposeAsync();

    private static MessageLine CreateMessageLine(ContextMessage message)
    {
        return new MessageLine(
            message.Role.ToString(), message.State.ToString(), message.IsPinned, message.TokenCount,
            message.Segments.Select(CreateSegmentLine).ToArray());
    }

    private static SegmentLine CreateSegmentLine(ContentSegment segment)
    {
        return segment switch
        {
            ToolUseContent toolUse => new SegmentLine(nameof(ToolUseContent), toolUse.ToolCallId, toolUse.ToolName, toolUse.Content),
            ToolResultContent toolResult => new SegmentLine(
                nameof(ToolResultContent), toolResult.ToolCallId, toolResult.ToolName, toolResult.Content),
            _ => new SegmentLine(segment.GetType().Name, null, null, segment.Content)
        };
    }

    private sealed record ExchangeLine(
        int ModelCall, DateTime TimestampUtc, string Status, IReadOnlyList<MessageLine> Messages, CapturedResponse? Response);

    private sealed record MessageLine(string Role, string State, bool IsPinned, int? TokenCount, IReadOnlyList<SegmentLine> Segments);

    private sealed record SegmentLine(string Type, string? ToolCallId, string? ToolName, string Content);
}
