using System.Text.RegularExpressions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core.TokenCounting;

/// <summary>
/// Provides TokenGuard's default heuristic <see cref="ITokenCounter"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EstimatedTokenCounter"/> is used internally by TokenGuard's built-in configuration and factory paths.
/// It stays dependency-free while producing estimates that are more stable for
/// prose, code, JSON, punctuation, and mixed Unicode text than a plain character-count heuristic.
/// </para>
/// <para>
/// The implementation uses a lightweight regex pre-tokenization pass inspired by GPT-style tokenizers, then applies
/// simple heuristics for words, digit groups, punctuation clusters, whitespace runs, and non-ASCII content. It
/// intentionally does not load vocabularies or perform BPE merges.
/// </para>
/// <para>
/// Each counted <see cref="ContextMessage"/> includes a fixed framing overhead, and tool-call segments add their own
/// JSON-wrapping overhead, so TokenGuard budgets remain closer to real chat payload costs than segment-only counting
/// would provide. Applications that require exact provider accounting can still bypass the built-in configuration path
/// and construct <see cref="ConversationContext"/> directly with a provider-backed <see cref="ITokenCounter"/>.
/// </para>
/// </remarks>
internal sealed class EstimatedTokenCounter : ITokenCounter
{
    private const int MessageOverhead = 4;
    private const int ToolUseOverhead = 10;
    private const int ToolResultOverhead = 7;

    private static readonly Regex PieceRegex = new(
        @"'s|'t|'re|'ve|'m|'ll|'d|[A-Za-z]+|\d{1,3}|[^\sA-Za-z\d]+|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public int Count(ContextMessage contextMessage)
    {
        ArgumentNullException.ThrowIfNull(contextMessage);

        if (contextMessage.TokenCount is > 0)
        {
            return contextMessage.TokenCount.Value;
        }

        var total = MessageOverhead;

        foreach (var segment in contextMessage.Segments)
        {
            total += CountSegment(segment);
        }

        return total;
    }

    /// <inheritdoc />
    public int Count(IEnumerable<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages.Sum(this.Count);
    }

    private static int CountSegment(ContentSegment segment) =>
        segment switch
        {
            TextContent text => CountText(text.Content),
            ToolUseContent toolUse => ToolUseOverhead
                + CountText(toolUse.ToolCallId)
                + CountText(toolUse.ToolName)
                + CountText(toolUse.Content),
            ToolResultContent toolResult => ToolResultOverhead
                + CountText(toolResult.ToolCallId)
                + CountText(toolResult.ToolName)
                + CountText(toolResult.Content),
            _ => CountText(segment.Content)
        };

    private static int CountText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;

        foreach (Match match in PieceRegex.Matches(text))
        {
            var piece = match.Value;

            if (string.IsNullOrEmpty(piece))
            {
                continue;
            }

            if (IsWhitespace(piece))
            {
                count += CountWhitespace(piece);
                continue;
            }

            if (IsAsciiLetters(piece))
            {
                count += CountAsciiWord(piece);
                continue;
            }

            if (IsDigits(piece))
            {
                count += CeilingDiv(piece.Length, 3);
                continue;
            }

            if (IsAsciiPunctuationOrSymbol(piece))
            {
                count += CountAsciiPunctuationOrSymbol(piece);
                continue;
            }

            count += CountNonAsciiOrMixed(piece);
        }

        return Math.Max(1, count);
    }

    private static int CountAsciiWord(string piece)
    {
        if (piece.Length <= 7)
        {
            return 1;
        }

        return CeilingDiv(piece.Length, 4);
    }

    private static int CountWhitespace(string piece)
    {
        var newlines = 0;
        var nonNewline = 0;

        foreach (var c in piece)
        {
            if (c is '\n' or '\r')
            {
                newlines++;
            }
            else
            {
                nonNewline++;
            }
        }

        // A lone space between words almost always merges into the adjacent word token.
        if (newlines == 0 && nonNewline <= 1)
        {
            return 0;
        }

        if (nonNewline == 0)
        {
            return newlines;
        }

        // Indentation runs: roughly one token per 4 whitespace chars (mirrors cl100k's "    " merges).
        return newlines + Math.Max(1, CeilingDiv(nonNewline, 4));
    }

    private static int CountAsciiPunctuationOrSymbol(string piece)
    {
        if (piece.Length == 1)
        {
            return 1;
        }

        if (AllSameChar(piece))
        {
            return Math.Max(1, CeilingDiv(piece.Length, 3));
        }

        return Math.Max(1, CeilingDiv(piece.Length, 2));
    }

    private static int CountNonAsciiOrMixed(string piece)
    {
        var count = 0;

        foreach (var c in piece)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (c <= 127)
            {
                count++;
                continue;
            }

            if (IsCjk(c))
            {
                count++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                // One half of a surrogate pair; the full pair lands at ~2 tokens for typical emoji.
                count++;
                continue;
            }

            // Cyrillic, Greek, Arabic, Hebrew, accented Latin, etc. typically cost ~2 tokens per char in cl100k.
            count += 2;
        }

        return Math.Max(1, count);
    }

    private static bool IsWhitespace(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetters(string value)
    {
        foreach (var c in value)
        {
            if (c > 127 || !char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDigits(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiPunctuationOrSymbol(string value)
    {
        foreach (var c in value)
        {
            if (c > 127)
            {
                return false;
            }

            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllSameChar(string value)
    {
        if (value.Length <= 1)
        {
            return true;
        }

        var first = value[0];

        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] != first)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u4DBF'
        or >= '\u4E00' and <= '\u9FFF'
        or >= '\uF900' and <= '\uFAFF';

    private static int CeilingDiv(int value, int divisor)
    {
        if (value <= 0)
        {
            return 0;
        }

        return (value + divisor - 1) / divisor;
    }
}
