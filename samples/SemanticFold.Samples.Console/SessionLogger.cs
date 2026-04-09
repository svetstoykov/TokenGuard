using System.Text;
using SemanticFold;
using SemanticFold.Enums;
using SemanticFold.Models;
using SemanticFold.Models.Content;

namespace SemanticFold.Samples.Console;

/// <summary>
/// Writes readable history snapshots for the console sample.
/// </summary>
public sealed class SessionLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly DateTimeOffset _startedAt;
    private int _snapshotSequence;

    /// <summary>
    /// Initializes a new logger instance and creates a timestamped log file.
    /// </summary>
    public SessionLogger()
    {
        this._startedAt = DateTimeOffset.Now;

        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        this.LogFilePath = Path.Combine(
            logDirectory,
            $"session-{this._startedAt:yyyyMMdd-HHmmss}.md");

        this._writer = new StreamWriter(this.LogFilePath, append: false, Encoding.UTF8)
        {
            AutoFlush = true,
        };

        this.WriteSessionHeader();
    }

    /// <summary>
    /// Gets the log file path for the current session.
    /// </summary>
    public string LogFilePath { get; }

    /// <summary>
    /// Logs the configured context budget and folding strategy.
    /// </summary>
    /// <param name="budget">The active context budget.</param>
    /// <param name="strategyName">The compaction strategy name.</param>
    public void LogBudgetInfo(ContextBudget budget, string strategyName)
    {
        this.WriteSection(
            "Configuration",
            [
                $"- Budget max: `{budget.MaxTokens}`",
                $"- Compaction trigger: `{budget.CompactionTriggerTokens}`",
                $"- Reserved tokens: `{budget.ReservedTokens}`",
                $"- Strategy: `{Sanitize(strategyName)}`",
            ]);
    }

    /// <summary>
    /// Logs a complete snapshot of the current message history before prepare runs.
    /// </summary>
    /// <param name="history">The full uncompacted history.</param>
    public void LogHistoryBeforePrepare(IReadOnlyList<Message> history)
    {
        this.LogSnapshot("HISTORY", "Before prepare", history);
    }

    /// <summary>
    /// Logs a complete snapshot of the prepared messages returned by the engine.
    /// </summary>
    /// <param name="preparedMessages">The prepared messages.</param>
    /// <param name="budget">The active context budget.</param>
    public void LogPreparedMessages(IReadOnlyList<Message> preparedMessages, ContextBudget budget)
    {
        var totalTokens = preparedMessages.Sum(m => m.TokenCount ?? 0);
        var maskedCount = preparedMessages.Count(m => m.State == CompactionState.Masked);
        var summarizedCount = preparedMessages.Count(m => m.State == CompactionState.Summarized);
        var status = maskedCount > 0 || summarizedCount > 0 ? "compacted" : "unchanged";

        this.LogSnapshot(
            "PREPARE",
            $"Prepared for model | tokens={totalTokens}/{budget.MaxTokens} | status={status} | masked={maskedCount} | summarized={summarizedCount}",
            preparedMessages);
    }

    /// <summary>
    /// Logs a newly appended history message.
    /// </summary>
    /// <param name="message">The appended message.</param>
    /// <param name="label">The event label.</param>
    public void LogMessageAdded(Message message, string label)
    {
        this.WriteSection(
            $"Event {Sanitize(label)}",
            [FormatMessageBullet(message, 0)]);
    }

    /// <summary>
    /// Logs a model response that was recorded in history.
    /// </summary>
    /// <param name="message">The recorded model message.</param>
    /// <param name="inputTokens">The model input tokens reported by the provider.</param>
    /// <param name="responseKind">The response kind label.</param>
    public void LogModelResponse(Message message, int? inputTokens, string responseKind)
    {
        this.WriteSection(
            $"Model {Sanitize(responseKind)}",
            [
                $"- Provider input tokens: `{FormatTokenCount(inputTokens)}`",
                FormatMessageBullet(message, 0),
            ]);
    }

    /// <summary>
    /// Logs a tool execution result that was recorded in history.
    /// </summary>
    /// <param name="message">The recorded tool result message.</param>
    public void LogToolResultRecorded(Message message)
    {
        this.WriteSection(
            "Tool Result",
            [FormatMessageBullet(message, 0)]);
    }

    /// <summary>
    /// Logs an operation error.
    /// </summary>
    /// <param name="operation">The failed operation name.</param>
    /// <param name="exception">The exception that was thrown.</param>
    public void LogError(string operation, Exception exception)
    {
        this.WriteSection(
            "Error",
            [
                $"- Operation: `{Sanitize(operation)}`",
                $"- Exception: `{exception.GetType().Name}`",
                $"- Message: {Sanitize(exception.Message)}",
            ]);
    }

    /// <summary>
    /// Logs a final session summary.
    /// </summary>
    /// <param name="historyCount">The final history count.</param>
    /// <param name="totalCompactionCount">The total number of compaction events.</param>
    /// <param name="duration">The session duration.</param>
    /// <param name="finalTokenCount">The final history token total.</param>
    public void LogSessionSummary(int historyCount, int totalCompactionCount, TimeSpan duration, int finalTokenCount)
    {
        this.WriteSection(
            "Session Summary",
            [
                $"- History messages: `{historyCount}`",
                $"- Compacted prepares: `{totalCompactionCount}`",
                $"- Final tokens: `{finalTokenCount}`",
                $"- Duration: `{duration:hh\\:mm\\:ss}`",
            ]);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this._writer.WriteLine("---");
        this._writer.WriteLine();
        this._writer.WriteLine($"_Session closed: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}_");
        this._writer.Dispose();
    }

    private void LogSnapshot(string category, string title, IReadOnlyList<Message> messages)
    {
        this._snapshotSequence++;
        var totalTokens = messages.Sum(m => m.TokenCount ?? 0);

        var lines = new List<string>
        {
            $"- Kind: `{category.ToLowerInvariant()}`",
            $"- Title: {Sanitize(title)}",
            $"- Snapshot: `{this._snapshotSequence}`",
            $"- Messages: `{messages.Count}`",
            $"- Tokens: `{totalTokens}`",
            string.Empty,
        };

        for (var i = 0; i < messages.Count; i++)
        {
            lines.Add(FormatMessageBullet(messages[i], i));
        }

        this.WriteSection($"Snapshot {this._snapshotSequence}", lines);
    }

    private void WriteSessionHeader()
    {
        this._writer.WriteLine("# SemanticFold Session Log");
        this._writer.WriteLine();
        this._writer.WriteLine($"- Started: `{this._startedAt:yyyy-MM-dd HH:mm:ss zzz}`");
        this._writer.WriteLine($"- File: `{Path.GetFileName(this.LogFilePath)}`");
        this._writer.WriteLine();
        this._writer.WriteLine("---");
        this._writer.WriteLine();
    }

    private void WriteSection(string title, IReadOnlyList<string> lines)
    {
        this._writer.WriteLine($"## {Sanitize(title)}");
        this._writer.WriteLine();

        foreach (var line in lines)
        {
            this._writer.WriteLine(line);
        }

        this._writer.WriteLine();
        this._writer.WriteLine("---");
        this._writer.WriteLine();
    }

    private static string FormatMessageBullet(Message message, int index)
        => $"- [{index:00}] role=`{RoleCode(message.Role)}` state=`{StateCode(message.State)}` tokens=`{FormatTokenCount(message.TokenCount)}` content=\"{Shorten(DescribeMessage(message), 160)}\"";

    private static string DescribeMessage(Message message)
    {
        var parts = message.Content.Select(DescribeContentBlock).Where(part => !string.IsNullOrWhiteSpace(part));
        var description = string.Join(" | ", parts);
        return string.IsNullOrWhiteSpace(description) ? "empty" : Sanitize(description);
    }

    private static string DescribeContentBlock(ContentBlock block)
    {
        return block switch
        {
            TextContent text => text.Text,
            ToolUseContent toolUse => $"tool-call {toolUse.ToolName} args={Shorten(toolUse.ArgumentsJson, 80)}",
            ToolResultContent toolResult => $"tool-result {toolResult.ToolName} chars={toolResult.Content.Length} preview={Shorten(toolResult.Content, 80)}",
            _ => block.GetType().Name,
        };
    }

    private static string FormatTokenCount(int? tokenCount)
        => tokenCount?.ToString() ?? "n/a";

    private static string Shorten(string value, int maxLength)
    {
        var sanitized = Sanitize(value);
        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..(maxLength - 3)] + "...";
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string StateCode(CompactionState state)
        => state switch
        {
            CompactionState.Original => "original",
            CompactionState.Masked => "masked",
            CompactionState.Summarized => "summarized",
            _ => "unknown",
        };

    private static string RoleCode(MessageRole role)
        => role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Model => "model",
            MessageRole.Tool => "tool",
            _ => "unknown",
        };
}
