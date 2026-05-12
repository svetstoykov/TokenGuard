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
    public string BuildUserPrompt(IReadOnlyList<ContextMessage> messages, int targetTokens)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (targetTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTokens), "targetTokens must be greater than zero.");
        }

        return $"""
                Limit: <= {targetTokens} tokens.

                Transcript:
                {this.FormatTranscript(messages)}
                """;
    }

    /// <inheritdoc />
    public string FormatTranscript(IReadOnlyList<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return "(empty)";
        }

        StringBuilder builder = new();

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            builder.Append('[')
                .Append(i + 1)
                .Append('|')
                .Append(FormatRole(message.Role))
                .AppendLine("]");

            foreach (var segment in message.Segments)
            {
                this.AppendSegment(builder, segment);
            }

            if (i < messages.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private void AppendSegment(StringBuilder builder, ContentSegment segment)
    {
        switch (segment)
        {
            case TextContent text:
                builder.Append("t:").AppendLine(text.Content);
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

    private void AppendToolResult(StringBuilder builder, ToolResultContent toolResult)
    {
        var metadata = this.MeasureToolResult(toolResult);

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

    private ToolResultMetadata MeasureToolResult(ToolResultContent toolResult)
    {
        var lines = SplitLines(toolResult.Content);
        var tokenCount = this._tokenCounter.Count(ContextMessage.FromContent(MessageRole.Tool, toolResult));

        return new ToolResultMetadata(
            tokenCount,
            lines.Count,
            toolResult.Content.Length,
            GuessContentKind(lines),
            lines);
    }

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

    private sealed record ToolResultMetadata(
        int EstimatedTokens,
        int LineCount,
        int CharacterCount,
        string Kind,
        IReadOnlyList<string> Lines);
}
