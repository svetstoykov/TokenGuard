using System.Globalization;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Models.Content;

/// <summary>
/// Shortens a tool result so that its estimated tokens fit a limit.
/// </summary>
/// <remarks>
/// <para>
/// The shortened text keeps whole lines from the start and the end of the result, filled greedily up to about half of the
/// limit each, and puts a notice where the omitted middle was. The notice counts toward the limit.
/// </para>
/// <para>
/// Text with one line, or whose first or last line alone is longer than its half, is cut by characters instead, at rune
/// boundaries so that a surrogate pair is never split. The notice then counts characters (UTF-16 code units).
/// </para>
/// <para>
/// Every estimate is made by the supplied counter on a tool result message, so the cut follows that counter's scale. When
/// the limit is smaller than the notice itself, the shortened text is the notice alone and its estimate is above the limit.
/// </para>
/// </remarks>
internal static class ToolResultShortener
{
    private const string NoticeFormat =
        "[Tool result shortened to fit the context: {0} of {1} {2} omitted here. Call the tool again with a narrower request if you need them.]";

    private const string LinesUnit = "lines";
    private const string CharactersUnit = "characters";

    /// <summary>
    /// Returns a copy of a tool result whose content is shortened to fit a limit on its estimated tokens.
    /// </summary>
    /// <param name="result">The tool result whose estimate is above <paramref name="maxTokens"/>.</param>
    /// <param name="maxTokens">The largest estimated token count of the returned tool result message.</param>
    /// <param name="counter">The token counter that estimates the tool result messages.</param>
    /// <returns>A tool result with the same tool call ID and tool name and the shortened content.</returns>
    internal static ToolResultContent Shorten(ToolResultContent result, int maxTokens, ITokenCounter counter)
    {
        var lines = SplitLines(result.Content);
        var text = (lines.Count > 1 ? ShortenByLines(result, lines, maxTokens, counter) : null)
            ?? ShortenByCharacters(result, maxTokens, counter)
            ?? NoticeAlone(result.Content, lines.Count);

        return new ToolResultContent(result.ToolCallId, result.ToolName, text);
    }

    /// <summary>
    /// Selects whole head and tail lines after reserving space for the notice, then checks the assembled estimate.
    /// </summary>
    /// <param name="result">The original result and its metadata.</param>
    /// <param name="lines">The ranges of complete lines, including their terminators.</param>
    /// <param name="maxTokens">The largest stored estimate.</param>
    /// <param name="counter">The context's token counter.</param>
    /// <returns>The shortened text, or <see langword="null"/> when a head or tail line cannot fit.</returns>
    private static string? ShortenByLines(ToolResultContent result, List<Range> lines, int maxTokens, ITokenCounter counter)
    {
        var content = result.Content;
        var terminator = content[lines[0]].EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var emptyTokens = Count(result, string.Empty, counter);
        var costs = lines.Select(line => Count(result, content[line], counter) - emptyTokens).ToArray();
        var available = maxTokens - Count(result, Notice(lines.Count, lines.Count, LinesUnit) + terminator, counter);

        while (available > 0)
        {
            var (head, tail) = SelectLines(costs, available);
            if (head == 0 || tail == 0)
                return null;

            var omitted = lines.Count - head - tail;
            var text = string.Concat(
                content.AsSpan(0, lines[head - 1].End.Value), Notice(omitted, lines.Count, LinesUnit), terminator,
                content.AsSpan(lines[lines.Count - tail].Start.Value));
            var tokens = Count(result, text, counter);
            if (tokens <= maxTokens)
                return text;

            available -= tokens - maxTokens;
        }

        return null;
    }

    /// <summary>
    /// Fills half the available tokens with head lines and the remainder with tail lines, leaving at least one line out.
    /// </summary>
    /// <param name="costs">The estimated content cost of each line, without the message overhead.</param>
    /// <param name="available">The tokens left after reserving the notice and message overhead.</param>
    /// <returns>The number of whole lines to keep at each end.</returns>
    private static (int Head, int Tail) SelectLines(int[] costs, int available)
    {
        var headTokens = 0;
        var head = 0;
        while (head < costs.Length - 1 && headTokens + costs[head] <= available / 2)
        {
            headTokens += costs[head];
            head++;
        }

        var tailTokens = 0;
        var tail = 0;
        while (head + tail < costs.Length - 1 && tailTokens + costs[costs.Length - 1 - tail] <= available - headTokens)
        {
            tailTokens += costs[costs.Length - 1 - tail];
            tail++;
        }

        return (head, tail);
    }

    /// <summary>
    /// Fits a head and tail around the character notice, moving each cut outside a surrogate pair.
    /// </summary>
    /// <param name="result">The original result and its metadata.</param>
    /// <param name="maxTokens">The largest stored estimate.</param>
    /// <param name="counter">The context's token counter.</param>
    /// <returns>The shortened text, or <see langword="null"/> when both ends cannot fit alongside the notice.</returns>
    private static string? ShortenByCharacters(ToolResultContent result, int maxTokens, ITokenCounter counter)
    {
        var content = result.Content;
        var emptyTokens = Count(result, string.Empty, counter);
        var available = maxTokens - Count(result, Notice(content.Length, content.Length, CharactersUnit), counter);

        while (available > 0)
        {
            var headLength = LongestPrefix(result, available / 2, emptyTokens, counter);
            var headTokens = Count(result, content[..headLength], counter) - emptyTokens;
            var tailStart = ShortestSuffixStart(result, headLength + 1, available - headTokens, emptyTokens, counter);
            if (headLength == 0 || tailStart == content.Length)
                return null;

            var text = string.Concat(
                content.AsSpan(0, headLength), Notice(tailStart - headLength, content.Length, CharactersUnit), content.AsSpan(tailStart));
            var tokens = Count(result, text, counter);
            if (tokens <= maxTokens)
                return text;

            available -= tokens - maxTokens;
        }

        return null;
    }

    /// <summary>
    /// Finds a prefix within the content budget by binary search and backs off a cut inside a surrogate pair.
    /// </summary>
    /// <param name="result">The original result and its metadata.</param>
    /// <param name="budget">The token budget for the prefix's content.</param>
    /// <param name="emptyTokens">The message overhead estimated with empty content.</param>
    /// <param name="counter">The context's token counter.</param>
    /// <returns>The number of UTF-16 code units in the prefix.</returns>
    private static int LongestPrefix(ToolResultContent result, int budget, int emptyTokens, ITokenCounter counter)
    {
        var content = result.Content;
        var low = 0;
        var high = content.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (Count(result, content[..middle], counter) - emptyTokens <= budget)
                low = middle;
            else
                high = middle - 1;
        }

        return IsInsideSurrogatePair(content, low) ? low - 1 : low;
    }

    /// <summary>
    /// Finds the earliest suffix within the content budget and advances a cut inside a surrogate pair.
    /// </summary>
    /// <param name="result">The original result and its metadata.</param>
    /// <param name="earliestStart">The earliest permitted suffix start, after the kept prefix.</param>
    /// <param name="budget">The token budget for the suffix's content.</param>
    /// <param name="emptyTokens">The message overhead estimated with empty content.</param>
    /// <param name="counter">The context's token counter.</param>
    /// <returns>The UTF-16 index of the suffix's first code unit.</returns>
    private static int ShortestSuffixStart(ToolResultContent result, int earliestStart, int budget, int emptyTokens, ITokenCounter counter)
    {
        var content = result.Content;
        var low = Math.Min(earliestStart, content.Length);
        var high = content.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (Count(result, content[middle..], counter) - emptyTokens <= budget)
                high = middle;
            else
                low = middle + 1;
        }

        return IsInsideSurrogatePair(content, low) ? low + 1 : low;
    }

    private static bool IsInsideSurrogatePair(string content, int index) =>
        index > 0 && index < content.Length && char.IsLowSurrogate(content[index]) && char.IsHighSurrogate(content[index - 1]);

    private static string NoticeAlone(string content, int lineCount) =>
        lineCount > 1 ? Notice(lineCount, lineCount, LinesUnit) : Notice(content.Length, content.Length, CharactersUnit);

    private static string Notice(int omitted, int total, string unit) =>
        string.Format(CultureInfo.InvariantCulture, NoticeFormat, omitted, total, unit);

    private static int Count(ToolResultContent result, string content, ITokenCounter counter) =>
        counter.Count(ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent(result.ToolCallId, result.ToolName, content)));

    private static List<Range> SplitLines(string content)
    {
        var lines = new List<Range>();
        var start = 0;
        while (start < content.Length)
        {
            var newline = content.IndexOf('\n', start);
            var end = newline < 0 ? content.Length : newline + 1;
            lines.Add(start..end);
            start = end;
        }

        return lines;
    }
}
