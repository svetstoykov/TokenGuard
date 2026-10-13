using System.Text;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Core.Summarization;

/// <summary>
/// Formats summarization prompts and transcripts for LLM-backed history compaction.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationSummaryFormatter"/> keeps prompt shaping deterministic and provider-agnostic. It renders the
/// compact transcript consumed by <see cref="ILlmSummarizer"/> implementations and applies size-aware tool-result
/// formatting so large observations do not dominate the summarization input budget.
/// </para>
/// <para>
/// Tool results are bucketed into three classes using an injected <see cref="ITokenCounter"/> estimate: small payloads
/// stay verbatim, medium payloads become stable excerpts, and very large payloads collapse to metadata only. This
/// preserves high-signal results while preventing prompt growth from scaling linearly with tool output size.
/// </para>
/// <para>
/// A user prompt is built to a request token budget. When the transcript is above it, long text segments are shortened
/// the same way from the oldest message on, and then the oldest exchanges are left out and counted in one line at the
/// top of the transcript.
/// </para>
/// </remarks>
internal sealed class ConversationSummaryFormatter : IConversationSummaryFormatter
{
    private static readonly string[] SalientKeywords =
    [
        "error",
        "warning",
        "exception",
        "failed",
        "failure",
        "fatal",
        "trace",
        "stack",
        "not found",
        "denied"
    ];

    private static readonly string[] SalientProgrammingLanguages =
    [
        "python",
        "javascript",
        "js",
        "typescript",
        "ts",
        "java",
        "c#",
        "csharp",
        "c++",
        "cpp",
        "golang",
        "rust",
        "php",
        "ruby"
    ];

    internal static IConversationSummaryFormatter Default { get; } =
        new ConversationSummaryFormatter(new EstimatedTokenCounter());

    private readonly ITokenCounter _tokenCounter;
    private readonly ConversationSummaryFormattingOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationSummaryFormatter"/> class with default thresholds.
    /// </summary>
    /// <param name="tokenCounter">The token counter used to classify tool result sizes.</param>
    public ConversationSummaryFormatter(ITokenCounter tokenCounter)
        : this(tokenCounter, ConversationSummaryFormattingOptions.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationSummaryFormatter"/> class.
    /// </summary>
    /// <param name="tokenCounter">The token counter used to classify tool result sizes.</param>
    /// <param name="options">The thresholds and excerpt sizes used while rendering tool results.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tokenCounter"/> is <see langword="null"/>.
    /// </exception>
    public ConversationSummaryFormatter(ITokenCounter tokenCounter, ConversationSummaryFormattingOptions options)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);

        this._tokenCounter = tokenCounter;
        this._options = options;
    }

    /// <inheritdoc />
    public string BuildUserPrompt(IReadOnlyList<ContextMessage> messages, int targetTokens, int requestTokenBudget)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (targetTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTokens), "targetTokens must be greater than zero.");
        }

        var frame = $"""
                     Limit: <= {targetTokens} tokens.

                     Transcript:

                     """;
        var transcriptBudget = (long)requestTokenBudget - this.CountText(ConversationSummaryPrompt.SystemPrompt) - this.CountText(frame);

        return frame + this.FormatTranscript(messages, transcriptBudget);
    }

    /// <inheritdoc />
    public string FormatTranscript(IReadOnlyList<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return this.FormatTranscript(messages, long.MaxValue);
    }

    /// <summary>
    /// Formats the transcript and shortens it until its estimated size is within a token budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A transcript above the budget is reduced in two steps, and each step stops as soon as the transcript fits. First,
    /// text segments are shortened from the oldest message on, the way tool results are: an excerpt above the full-size
    /// threshold and metadata only above the excerpt threshold. Then whole exchanges are left out from the oldest on, and
    /// one line at the top says how many. An exchange is a user message and everything up to the next user message.
    /// </para>
    /// <para>
    /// The newest exchange always stays, so the transcript is above the budget when that exchange alone is. The size is
    /// the sum of the per-message counts, which is at or above the count of the joined transcript for a counter that
    /// adds a fixed cost per message.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered source messages. Cannot be <see langword="null"/>.</param>
    /// <param name="tokenBudget">The largest transcript size in tokens.</param>
    /// <returns>The compact transcript.</returns>
    private string FormatTranscript(IReadOnlyList<ContextMessage> messages, long tokenBudget)
    {
        if (messages.Count == 0)
        {
            return "(empty)";
        }

        var entries = new string[messages.Count];
        var costs = new int[messages.Count];
        long total = 0;

        for (var i = 0; i < messages.Count; i++)
        {
            entries[i] = this.FormatMessage(i, messages[i], shortenLongText: false);
            costs[i] = this.CountText(entries[i]);
            total += costs[i];
        }

        for (var i = 0; i < messages.Count && total > tokenBudget; i++)
        {
            if (!messages[i].Segments.Any(static segment => segment is TextContent))
            {
                continue;
            }

            var shortened = this.FormatMessage(i, messages[i], shortenLongText: true);
            var shortenedCost = this.CountText(shortened);

            if (shortenedCost < costs[i])
            {
                total += shortenedCost - costs[i];
                entries[i] = shortened;
                costs[i] = shortenedCost;
            }
        }

        var firstKept = 0;
        var omittedExchanges = 0;
        var omissionLine = string.Empty;

        while (total + this.CountText(omissionLine) > tokenBudget)
        {
            var nextExchange = FindNextExchangeStart(messages, firstKept);

            if (nextExchange == messages.Count)
            {
                break;
            }

            for (var i = firstKept; i < nextExchange; i++)
            {
                total -= costs[i];
            }

            firstKept = nextExchange;
            omittedExchanges++;
            omissionLine = FormatOmissionLine(omittedExchanges);
        }

        StringBuilder builder = new(omissionLine);

        for (var i = firstKept; i < messages.Count; i++)
        {
            builder.Append(entries[i]);
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Formats one message as its header line, its segments, and the blank line that separates it from the next message.
    /// </summary>
    private string FormatMessage(int index, ContextMessage message, bool shortenLongText)
    {
        StringBuilder builder = new();
        builder.Append('[')
            .Append(index + 1)
            .Append('|')
            .Append(FormatRole(message.Role))
            .AppendLine("]");

        foreach (var segment in message.Segments)
        {
            this.AppendSegment(builder, segment, shortenLongText);
        }

        return builder.AppendLine().ToString();
    }

    private int CountText(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : this._tokenCounter.Count(ContextMessage.FromText(MessageRole.User, text));

    private void AppendSegment(StringBuilder builder, ContentSegment segment, bool shortenLongText)
    {
        switch (segment)
        {
            case TextContent text:
                this.AppendText(builder, text, shortenLongText);
                break;
            case ToolUseContent toolUse:
                builder.Append("u:")
                    .Append(toolUse.ToolName)
                    .Append('|')
                    .AppendLine(toolUse.ToolCallId);
                break;
            case ToolResultContent toolResult:
                this.AppendToolResult(builder, toolResult);
                break;
            default:
                builder.Append("c:")
                    .Append(segment.GetType().Name)
                    .Append('|')
                    .AppendLine(segment.Content);
                break;
        }
    }

    private void AppendText(StringBuilder builder, TextContent text, bool shortenLongText)
    {
        builder.Append("t:");

        if (shortenLongText)
        {
            var metadata = this.Measure(text.Content, this._tokenCounter.Count(ContextMessage.FromContent(MessageRole.User, text)));

            if (metadata.EstimatedTokens > this._options.FullToolResultMaxTokens)
            {
                this.AppendShortened(builder, metadata);
                return;
            }
        }

        builder.AppendLine(text.Content);
    }

    private void AppendToolResult(StringBuilder builder, ToolResultContent toolResult)
    {
        var metadata = this.Measure(toolResult.Content, this._tokenCounter.Count(ContextMessage.FromContent(MessageRole.Tool, toolResult)));

        builder.Append("r:")
            .Append(toolResult.ToolName)
            .Append('|')
            .Append(toolResult.ToolCallId)
            .Append('|');

        if (metadata.EstimatedTokens <= this._options.FullToolResultMaxTokens)
        {
            builder.AppendLine("full");

            if (!string.IsNullOrEmpty(toolResult.Content))
            {
                builder.AppendLine(toolResult.Content);
            }

            return;
        }

        this.AppendShortened(builder, metadata);
    }

    /// <summary>
    /// Appends the shortened form of a payload: an excerpt up to the excerpt threshold, and metadata only above it.
    /// </summary>
    private void AppendShortened(StringBuilder builder, ContentMetadata metadata)
    {
        if (metadata.EstimatedTokens <= this._options.ExcerptToolResultMaxTokens)
        {
            builder.Append("excerpt(tokens=")
                .Append(metadata.EstimatedTokens)
                .Append(",lines=")
                .Append(metadata.LineCount)
                .Append(",kind=")
                .Append(metadata.Kind)
                .AppendLine(")");

            this.AppendExcerpt(builder, metadata.Lines);
            return;
        }

        builder.Append("meta(tokens=")
            .Append(metadata.EstimatedTokens)
            .Append(",lines=")
            .Append(metadata.LineCount)
            .Append(",chars=")
            .Append(metadata.CharacterCount)
            .Append(",kind=")
            .Append(metadata.Kind)
            .AppendLine(",truncated=true)");
    }

    private void AppendExcerpt(StringBuilder builder, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var headCount = Math.Min(this._options.ExcerptHeadLineCount, lines.Count);
        var tailStart = Math.Max(headCount, lines.Count - this._options.ExcerptTailLineCount);

        AppendSection(builder, "head", lines.Take(headCount));

        var salientLines = SelectSalientLines(lines, headCount, tailStart, this._options.ExcerptSalientLineCount);

        if (salientLines.Count > 0)
        {
            AppendSection(builder, "salient", salientLines);
        }

        if (tailStart < lines.Count)
        {
            AppendSection(builder, "tail", lines.Skip(tailStart));
        }
    }

    private ContentMetadata Measure(string content, int tokenCount)
    {
        var lines = SplitLines(content);

        return new ContentMetadata(tokenCount, lines.Count, content.Length, GuessContentKind(lines), lines);
    }

    /// <summary>
    /// Finds where the exchange after the one starting at <paramref name="start"/> begins.
    /// </summary>
    /// <param name="messages">The ordered source messages.</param>
    /// <param name="start">The index of the first message of the current exchange.</param>
    /// <returns>The index of the next user message, or the message count when the exchange is the newest one.</returns>
    private static int FindNextExchangeStart(IReadOnlyList<ContextMessage> messages, int start)
    {
        for (var i = start + 1; i < messages.Count; i++)
        {
            if (messages[i].Role == MessageRole.User)
            {
                return i;
            }
        }

        return messages.Count;
    }

    private static string FormatOmissionLine(int omittedExchanges) =>
        $"[{omittedExchanges} earlier {(omittedExchanges == 1 ? "exchange" : "exchanges")} omitted]{Environment.NewLine}{Environment.NewLine}";

    private static List<string> SplitLines(string content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
    }

    private static string GuessContentKind(IReadOnlyList<string> lines)
    {
        var firstNonEmptyLine = lines.FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))?.Trim();

        if (firstNonEmptyLine is null)
        {
            return "text";
        }

        if ((firstNonEmptyLine.StartsWith('{') && firstNonEmptyLine.EndsWith('}'))
            || (firstNonEmptyLine.StartsWith('[') && firstNonEmptyLine.EndsWith(']')))
        {
            return "json";
        }

        if (lines.Any(static line =>
                line.StartsWith("diff --git", StringComparison.Ordinal)
                || line.StartsWith("@@", StringComparison.Ordinal)
                || line.StartsWith("+++", StringComparison.Ordinal)
                || line.StartsWith("---", StringComparison.Ordinal)))
        {
            return "diff";
        }

        if (lines.Any(static line => line.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("TRACE", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("DEBUG", StringComparison.OrdinalIgnoreCase)))
        {
            return "log";
        }

        return "text";
    }

    private static List<string> SelectSalientLines(
        IReadOnlyList<string> lines,
        int headCount,
        int tailStart,
        int maxCount)
    {
        List<string> results = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (var i = headCount; i < tailStart && results.Count < maxCount; i++)
        {
            var line = lines[i];

            if (!IsSalientLine(line) || !seen.Add(line))
            {
                continue;
            }

            results.Add(line);
        }

        return results;
    }

    private static bool IsSalientLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();

        if (trimmed.StartsWith("diff --git", StringComparison.Ordinal)
            || trimmed.StartsWith("@@", StringComparison.Ordinal)
            || trimmed.StartsWith("+++", StringComparison.Ordinal)
            || trimmed.StartsWith("---", StringComparison.Ordinal)
            || trimmed.StartsWith("at ", StringComparison.Ordinal)
            || trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.Contains("\":", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var keyword in SalientKeywords)
        {
            if (trimmed.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var language in SalientProgrammingLanguages)
        {
            if (ContainsKeyword(trimmed, language))
            {
                return true;
            }
        }

        return trimmed.Contains(".cs:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(".json", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        var index = 0;

        while (index < text.Length)
        {
            index = text.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                return false;
            }

            var start = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endIndex = index + keyword.Length;
            var end = endIndex == text.Length || !char.IsLetterOrDigit(text[endIndex]);

            if (start && end)
            {
                return true;
            }

            index = endIndex;
        }

        return false;
    }

    private static void AppendSection(StringBuilder builder, string label, IEnumerable<string> lines)
    {
        builder.Append(label).AppendLine(":");

        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }
    }

    private static string FormatRole(MessageRole role)
    {
        return role switch
        {
            MessageRole.System => "sys",
            MessageRole.User => "user",
            MessageRole.Model => "model",
            MessageRole.Tool => "tool",
            _ => role.ToString().ToLowerInvariant(),
        };
    }

    private sealed record ContentMetadata(
        int EstimatedTokens,
        int LineCount,
        int CharacterCount,
        string Kind,
        IReadOnlyList<string> Lines);
}
