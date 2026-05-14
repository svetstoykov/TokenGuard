using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core.TokenCounting;

/// <summary>
/// Provides TokenGuard's default heuristic <see cref="ITokenCounter"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EstimatedTokenCounter"/> is used internally by TokenGuard's built-in configuration and factory paths.
/// It stays dependency-free while producing estimates that are more stable for prose, code, JSON, punctuation, mixed
/// Unicode text, and tool payloads than a plain character-count heuristic.
/// </para>
/// <para>
/// The implementation uses Unicode-aware scanning, lightweight content-shape detection, and structural accounting for
/// JSON and tool segments. It intentionally does not load provider vocabularies or perform BPE merges.
/// </para>
/// <para>
/// Each counted <see cref="ContextMessage"/> includes a fixed framing overhead, and tool-call segments add structural
/// wrapper cost so TokenGuard budgets remain closer to real chat payload costs than segment-only counting would
/// provide. Applications that require different creation flows can bypass dependency injection and construct
/// <see cref="ConversationContextFactory"/> directly, but the built-in factory path always uses this heuristic counter.
/// </para>
/// </remarks>
internal sealed class EstimatedTokenCounter(TokenCountSafetyMode safetyMode = TokenCountSafetyMode.Safe) 
    : ITokenCounter
{
    private const int MessageOverhead = 4;
    private const int ToolEnvelopeOverhead = 6;
    private const int ToolFieldOverhead = 1;
    private const int JsonObjectOverhead = 1;
    private const int JsonArrayOverhead = 1;
    private const int JsonStringOverhead = 1;

    /// <inheritdoc />
    public int Count(ContextMessage contextMessage)
    {
        ArgumentNullException.ThrowIfNull(contextMessage);

        if (contextMessage.TokenCount is > 0)
        {
            return ApplySafetyMargin(contextMessage.TokenCount.Value, safetyMode);
        }

        var total = MessageOverhead;

        foreach (var segment in contextMessage.Segments)
        {
            total += CountSegment(segment);
        }

        return ApplySafetyMargin(total, safetyMode);
    }

    /// <inheritdoc />
    public int Count(IEnumerable<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages.Sum(this.Count);
    }

    private static int ApplySafetyMargin(int count, TokenCountSafetyMode safetyMode)
    {
        var multiplier = safetyMode switch
        {
            TokenCountSafetyMode.Balanced => 1.00,
            TokenCountSafetyMode.Safe => 1.05,
            TokenCountSafetyMode.Conservative => 1.10,
            _ => 1.05,
        };

        return (int)Math.Ceiling(count * multiplier);
    }

    private static int CountSegment(ContentSegment segment) =>
        segment switch
        {
            TextContent text => CountText(text.Content),
            ToolUseContent toolUse => CountToolEnvelope(
                new ToolField(toolUse.ToolCallId, IsStructured: false),
                new ToolField(toolUse.ToolName, IsStructured: false),
                new ToolField(toolUse.Content, IsStructured: true)),
            ToolResultContent toolResult => CountToolEnvelope(
                new ToolField(toolResult.ToolCallId, IsStructured: false),
                new ToolField(toolResult.ToolName, IsStructured: false),
                new ToolField(toolResult.Content, IsStructured: LooksLikeJson(toolResult.Content.AsSpan().Trim()))),
            _ => CountText(segment.Content)
        };

    private static int CountToolEnvelope(params ToolField[] fields)
    {
        var total = ToolEnvelopeOverhead;

        foreach (var field in fields)
        {
            total += ToolFieldOverhead;
            total += field.IsStructured ? CountStructuredPayload(field.Value) : CountQuotedString(field.Value);
        }

        return total;
    }

    private static int CountText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var trimmed = text.AsSpan().Trim();
        if (LooksLikeJson(trimmed) && TryCountJson(trimmed.ToString(), out var jsonCount))
        {
            return Math.Max(1, jsonCount);
        }

        return Math.Max(1, CountGeneralText(text));
    }

    private static int CountStructuredPayload(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var trimmed = text.AsSpan().Trim();
        if (LooksLikeJson(trimmed) && TryCountJson(trimmed.ToString(), out var jsonCount))
        {
            return Math.Max(1, jsonCount);
        }

        return CountQuotedString(text);
    }

    private static int CountQuotedString(string text) =>
        JsonStringOverhead + CountJsonStringContent(text);

    private static int CountJsonString(string text)
    {
        if (IsShortAsciiJsonString(text))
        {
            return 1;
        }

        return Math.Max(1, CountJsonStringContent(text));
    }

    private static int CountJsonStringContent(string text) =>
        CountGeneralText(text) + CountEscapes(text);

    private static int CountEscapes(string text)
    {
        var escapeCount = 0;

        foreach (var c in text)
        {
            if (c is '"' or '\\' or '\n' or '\r' or '\t')
            {
                escapeCount++;
            }
        }

        return escapeCount == 0 ? 0 : Math.Max(1, CeilingDiv(escapeCount, 3));
    }

    private static int CountGeneralText(string text)
    {
        var count = 0;
        var start = 0;

        while (start < text.Length)
        {
            if (char.IsWhiteSpace(text[start]))
            {
                var end = start + 1;
                while (end < text.Length && char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                count += CountWhitespace(text.AsSpan(start, end - start));
                start = end;
                continue;
            }

            var chunkEnd = start + 1;
            while (chunkEnd < text.Length && !char.IsWhiteSpace(text[chunkEnd]))
            {
                chunkEnd++;
            }

            count += CountChunk(text.AsSpan(start, chunkEnd - start));
            start = chunkEnd;
        }

        return count;
    }

    private static int CountChunk(ReadOnlySpan<char> chunk)
    {
        if (chunk.IsEmpty)
        {
            return 0;
        }

        if (LooksLikeJson(chunk) && TryCountJson(chunk.ToString(), out var jsonCount))
        {
            return jsonCount;
        }

        if (LooksLikeUrl(chunk) || LooksLikePath(chunk))
        {
            return 1 + CountChunkCore(chunk);
        }

        return CountChunkCore(chunk);
    }

    private static int CountChunkCore(ReadOnlySpan<char> chunk)
    {
        var count = 0;
        var index = 0;

        while (index < chunk.Length)
        {
            if (TryCountAnsiEscapeSequence(chunk, index, out var ansiLength, out var ansiTokenCount))
            {
                count += ansiTokenCount;
                index += ansiLength;
                continue;
            }

            var rune = ReadRune(chunk, index, out var consumed);

            if (IsWordRune(rune))
            {
                var end = index + consumed;

                while (end < chunk.Length)
                {
                    var next = ReadRune(chunk, end, out var nextConsumed);
                    if (IsWordRune(next))
                    {
                        end += nextConsumed;
                        continue;
                    }

                    if (IsConnectorApostrophe(chunk, end))
                    {
                        end++;
                        continue;
                    }

                    if (IsLatinComponentBoundary(chunk, end))
                    {
                        end++;
                        continue;
                    }

                    break;
                }

                count += CountWordLike(chunk[index..end]);
                index = end;
                continue;
            }

            if (Rune.IsDigit(rune))
            {
                var digitCount = 1;
                var end = index + consumed;

                while (end < chunk.Length)
                {
                    var next = ReadRune(chunk, end, out var nextConsumed);
                    if (!Rune.IsDigit(next))
                    {
                        break;
                    }

                    digitCount++;
                    end += nextConsumed;
                }

                count += CeilingDiv(digitCount, 3);
                index = end;
                continue;
            }

            if (TryGetBpeContractionLength(chunk, index, out var contractionLength))
            {
                count++;
                index += contractionLength;
                continue;
            }

            var punctuationEnd = index + consumed;
            while (punctuationEnd < chunk.Length)
            {
                var next = ReadRune(chunk, punctuationEnd, out var nextConsumed);
                if (IsWordRune(next) || Rune.IsDigit(next))
                {
                    break;
                }

                punctuationEnd += nextConsumed;
            }

            count += CountPunctuationOrSymbol(chunk[index..punctuationEnd]);
            index = punctuationEnd;
        }

        return count;
    }

    private static int CountWordLike(ReadOnlySpan<char> piece)
    {
        var graphemeCount = CountTextElements(piece);
        if (graphemeCount == 0)
        {
            return 0;
        }

        return DetectScriptGroup(piece) switch
        {
            ScriptGroup.Latin => CountLatinWord(piece, graphemeCount),
            ScriptGroup.CyrillicOrGreek => Math.Max(1, CeilingDiv(graphemeCount, 3)),
            ScriptGroup.ArabicHebrew => Math.Max(1, CeilingDiv(graphemeCount, 2)),
            ScriptGroup.CjkOrKanaOrHangul => graphemeCount,
            ScriptGroup.Other => Math.Max(1, CeilingDiv(graphemeCount, 2)),
            _ => Math.Max(1, CeilingDiv(graphemeCount * 3, 2))
        };
    }

    private static int CountLatinWord(ReadOnlySpan<char> piece, int graphemeCount)
    {
        var chunked = graphemeCount <= 10 ? 1 : CeilingDiv(graphemeCount, 5);
        var components = CountLatinComponents(piece);

        return Math.Max(chunked, components);
    }

    private static int CountLatinComponents(ReadOnlySpan<char> piece)
    {
        if (piece.IsEmpty)
        {
            return 0;
        }

        var componentCount = 1;

        for (var index = 1; index < piece.Length; index++)
        {
            if (piece[index] is '\'' or '’')
            {
                if (index < piece.Length - 1 && !IsConnectorApostrophe(piece, index) && char.IsLetter(piece[index - 1]) && char.IsLetter(piece[index + 1]))
                {
                    componentCount++;
                }

                continue;
            }

            if (piece[index] is '_' or '-' or '.')
            {
                // CountWordLike only retains '.' for identifier-like segments because digit runs
                // are counted earlier in CountChunkCore before Latin-word accounting is reached.
                if (index < piece.Length - 1)
                {
                    componentCount++;
                }

                continue;
            }

            if (!char.IsLetter(piece[index]))
            {
                continue;
            }

            var previous = piece[index - 1];
            if (char.IsLower(previous) && char.IsUpper(piece[index]))
            {
                componentCount++;
                continue;
            }

            if (char.IsUpper(previous) && char.IsUpper(piece[index]) && index < piece.Length - 1 && char.IsLower(piece[index + 1]))
            {
                componentCount++;
            }
        }

        return componentCount;
    }

    private static int CountWhitespace(ReadOnlySpan<char> piece)
    {
        var newlines = 0;
        var nonNewline = 0;
        var indentationWidth = 0;
        var indentationOnly = true;

        for (var index = 0; index < piece.Length; index++)
        {
            var c = piece[index];

            if (c == '\r')
            {
                newlines++;

                if (index < piece.Length - 1 && piece[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            if (c == '\n')
            {
                newlines++;
            }
            else
            {
                nonNewline++;

                if (c == ' ')
                {
                    indentationWidth++;
                }
                else if (c == '\t')
                {
                    indentationWidth += 2;
                }
                else
                {
                    indentationOnly = false;
                }
            }
        }

        if (newlines == 0 && nonNewline <= 1)
        {
            return 0;
        }

        if (newlines >= 1 && indentationOnly)
        {
            var chargedNonNewline = Math.Max(0, indentationWidth - (newlines * 2));
            return newlines + (chargedNonNewline == 0 ? 0 : Math.Max(1, CeilingDiv(chargedNonNewline, 4)));
        }

        if (nonNewline == 0)
        {
            return newlines;
        }

        return newlines + Math.Max(1, CeilingDiv(nonNewline, 4));
    }

    private static int CountPunctuationOrSymbol(ReadOnlySpan<char> piece)
    {
        if (piece.IsEmpty)
        {
            return 0;
        }

        if (ContainsEmoji(piece))
        {
            return CountEmojiClusters(piece);
        }

        if (AllSameChar(piece))
        {
            if (piece[0] <= 127)
            {
                return CountAsciiPunctuationOrSymbol(piece);
            }

            return Math.Max(1, CeilingDiv(CountTextElements(piece), 4));
        }

        var asciiOnly = true;
        foreach (var c in piece)
        {
            if (c > 127)
            {
                asciiOnly = false;
                break;
            }
        }

        if (asciiOnly)
        {
            return CountAsciiPunctuationOrSymbol(piece);
        }

        var graphemeCount = CountTextElements(piece);
        if (ContainsStructuralUnicodeSymbols(piece))
        {
            return Math.Max(1, CeilingDiv(graphemeCount * 3, 4));
        }

        return Math.Max(1, CeilingDiv(graphemeCount, 2));
    }

    private static int CountAsciiPunctuationOrSymbol(ReadOnlySpan<char> piece)
    {
        if (piece.Length == 1)
        {
            return 1;
        }

        if (AllSameChar(piece))
        {
            return Math.Max(1, CeilingDiv(piece.Length, 3));
        }

        if (ContainsStructuralAscii(piece))
        {
            return Math.Max(1, CeilingDiv(piece.Length * 3, 4));
        }

        return Math.Max(1, CeilingDiv(piece.Length, 2));
    }

    private static bool ContainsStructuralAscii(ReadOnlySpan<char> piece)
    {
        foreach (var c in piece)
        {
            if (c is '{' or '}' or '[' or ']' or '(' or ')' or ':' or ',' or ';' or '=' or '"' or '`')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsStructuralUnicodeSymbols(ReadOnlySpan<char> piece)
    {
        var index = 0;
        while (index < piece.Length)
        {
            var rune = ReadRune(piece, index, out var consumed);
            if (rune.Value is not (>= 0x2500 and <= 0x257F or >= 0x2580 and <= 0x259F or >= 0x25A0 and <= 0x25FF))
            {
                return false;
            }

            index += consumed;
        }

        return true;
    }

    private static int CountEmojiClusters(ReadOnlySpan<char> piece)
    {
        var total = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(piece.ToString());

        while (enumerator.MoveNext())
        {
            var cluster = enumerator.GetTextElement();
            total += IsComplexEmojiCluster(cluster) ? 3 : 2;
        }

        return Math.Max(1, total);
    }

    private static bool IsComplexEmojiCluster(string cluster)
    {
        var hasJoiner = false;
        var runeCount = 0;

        foreach (var rune in cluster.EnumerateRunes())
        {
            runeCount++;
            if (rune.Value is 0x200D or >= 0x1F3FB and <= 0x1F3FF)
            {
                hasJoiner = true;
            }
        }

        return hasJoiner || runeCount > 2;
    }

    private static int CountTextElements(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        var count = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text.ToString());

        while (enumerator.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static bool TryCountAnsiEscapeSequence(ReadOnlySpan<char> text, int index, out int length, out int tokenCount)
    {
        length = 0;
        tokenCount = 0;

        if (!TryGetAnsiCsiPrefixLength(text, index, out var prefixLength))
        {
            return false;
        }

        var end = index + prefixLength;
        while (end < text.Length && text[end] is >= (char)0x20 and <= (char)0x3F)
        {
            end++;
        }

        if (end >= text.Length || text[end] is < (char)0x40 or > (char)0x7E)
        {
            return false;
        }

        length = end - index + 1;
        tokenCount = Math.Max(1, CeilingDiv(length, 4));
        return true;
    }

    private static bool TryGetAnsiCsiPrefixLength(ReadOnlySpan<char> text, int index, out int prefixLength)
    {
        prefixLength = 0;

        if (index >= text.Length)
        {
            return false;
        }

        if (text[index] == '\u001B')
        {
            if (index < text.Length - 1 && text[index + 1] == '[')
            {
                prefixLength = 2;
                return true;
            }

            return false;
        }

        if (MatchesAnsiPrefix(text, index, @"\x1b[") ||
            MatchesAnsiPrefix(text, index, @"\x1B[") ||
            MatchesAnsiPrefix(text, index, @"\033["))
        {
            prefixLength = 5;
            return true;
        }

        if (MatchesAnsiPrefix(text, index, "^[[")) 
        {
            prefixLength = 3;
            return true;
        }

        return false;
    }

    private static bool MatchesAnsiPrefix(ReadOnlySpan<char> text, int index, string prefix) =>
        index <= text.Length - prefix.Length
        && text.Slice(index, prefix.Length).SequenceEqual(prefix);

    private static bool TryCountJson(string text, out int count)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            count = CountJsonElement(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            count = 0;
            return false;
        }
    }

    private static int CountJsonElement(JsonElement element) =>
        CountJsonElement(element, parentObjectPropertyCount: 0, parentArrayItemCount: 0);

    private static int CountJsonElement(JsonElement element, int parentObjectPropertyCount, int parentArrayItemCount) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => CountJsonObject(element),
            JsonValueKind.Array => CountJsonArray(element),
            JsonValueKind.String => CountJsonValueString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => Math.Max(1, 1 + CeilingDiv(CountDigitsInNumber(element.GetRawText()), 3)),
            _ => 1
        };

    private static int CountJsonObject(JsonElement element)
    {
        var propertyCount = 0;
        foreach (var _ in element.EnumerateObject())
        {
            propertyCount++;
        }

        var total = JsonObjectOverhead;

        foreach (var property in element.EnumerateObject())
        {
            total += CountJsonString(property.Name);
            total += CountJsonElement(property.Value, parentObjectPropertyCount: propertyCount, parentArrayItemCount: 0);
        }

        return total + Math.Max(0, (propertyCount * 2) - 1);
    }

    private static int CountJsonArray(JsonElement element)
    {
        var total = JsonArrayOverhead;
        var itemCount = 0;

        foreach (var _ in element.EnumerateArray())
        {
            itemCount++;
        }

        var allCompactPrimitives = true;

        foreach (var item in element.EnumerateArray())
        {
            total += CountJsonElement(item, parentObjectPropertyCount: 0, parentArrayItemCount: itemCount);
            allCompactPrimitives &= IsCompactJsonArrayPrimitive(item);
        }

        if (allCompactPrimitives || itemCount > 1)
        {
            total += Math.Max(0, itemCount - 1);
        }

        return total;
    }

    private static int CountDigitsInNumber(string text)
    {
        var count = 0;

        foreach (var c in text)
        {
            if (char.IsDigit(c))
            {
                count++;
            }
        }

        return Math.Max(1, count);
    }

    private static bool LooksLikeJson(ReadOnlySpan<char> text)
    {
        if (text.Length < 2)
        {
            return false;
        }

        return (text[0], text[^1]) is ('{', '}') or ('[', ']');
    }

    private static bool LooksLikeUrl(ReadOnlySpan<char> text) =>
        text.Contains("://", StringComparison.Ordinal) || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePath(ReadOnlySpan<char> text)
    {
        if (text.Length < 2)
        {
            return false;
        }

        if (text.StartsWith("./", StringComparison.Ordinal) ||
            text.StartsWith("../", StringComparison.Ordinal) ||
            text.StartsWith("~/", StringComparison.Ordinal) ||
            text[0] == '/' ||
            text.Contains('\\'))
        {
            return true;
        }

        return text.Contains('/') && !text.Contains("://", StringComparison.Ordinal);
    }

    private static int CountJsonValueString(string text)
    {
        return CountJsonString(text);
    }

    private static bool IsShortAsciiJsonString(string text)
    {
        if (text.Length > 4)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c > 127 || c is '"' or '\\' or '\n' or '\r' or '\t')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCompactJsonArrayPrimitive(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => (element.GetString() ?? string.Empty).Length <= 4,
            JsonValueKind.Number => true,
            JsonValueKind.True => true,
            JsonValueKind.False => true,
            JsonValueKind.Null => true,
            _ => false
        };

    private static bool IsWordRune(Rune rune)
    {
        if (Rune.IsLetter(rune))
        {
            return true;
        }

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static bool IsConnectorApostrophe(ReadOnlySpan<char> text, int index)
    {
        if (index <= 0 || index >= text.Length - 1)
        {
            return false;
        }

        var value = text[index];
        if (value is not '\'' and not '’')
        {
            return false;
        }

        if (!char.IsLetter(text[index - 1]) || !char.IsLetter(text[index + 1]))
        {
            return false;
        }

        var suffix = GetApostropheSuffix(text, index);
        if (!IsBpeContraction(suffix))
        {
            return true;
        }

        return suffix.Equals("s", StringComparison.OrdinalIgnoreCase)
            && !IsCommonApostropheSContractionStem(text[..index]);
    }

    private static bool IsLatinComponentBoundary(ReadOnlySpan<char> text, int index) =>
        index > 0
        && index < text.Length - 1
        && text[index] is '_' or '-' or '.'
        && char.IsLetter(text[index - 1])
        && char.IsLetter(text[index + 1]);

    private static bool TryGetBpeContractionLength(ReadOnlySpan<char> text, int index, out int length)
    {
        length = 0;

        if (index <= 0 || index >= text.Length - 1)
        {
            return false;
        }

        if (text[index] is not '\'' and not '’')
        {
            return false;
        }

        if (!char.IsLetter(text[index - 1]))
        {
            return false;
        }

        var suffix = GetApostropheSuffix(text, index);
        if (suffix.IsEmpty || !IsBpeContraction(suffix) || IsConnectorApostrophe(text, index))
        {
            return false;
        }

        length = 1 + suffix.Length;
        return true;
    }

    private static ReadOnlySpan<char> GetApostropheSuffix(ReadOnlySpan<char> text, int index)
    {
        var end = index + 1;
        while (end < text.Length && char.IsLetter(text[end]))
        {
            end++;
        }

        return text[(index + 1)..end];
    }

    private static bool IsBpeContraction(ReadOnlySpan<char> suffix) =>
        suffix.Length switch
        {
            1 => suffix.Equals("t", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("s", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("m", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("d", StringComparison.OrdinalIgnoreCase),
            2 => suffix.Equals("re", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("ve", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("ll", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool IsCommonApostropheSContractionStem(ReadOnlySpan<char> stem) =>
        stem.Equals("he", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("she", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("it", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("that", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("there", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("here", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("what", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("who", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("where", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("when", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("why", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("how", StringComparison.OrdinalIgnoreCase)
        || stem.Equals("let", StringComparison.OrdinalIgnoreCase);

    private static Rune ReadRune(ReadOnlySpan<char> text, int index, out int charsConsumed)
    {
        var status = Rune.DecodeFromUtf16(text[index..], out var rune, out charsConsumed);
        if (status == OperationStatus.Done)
        {
            return rune;
        }

        charsConsumed = 1;
        return new Rune(text[index]);
    }

    private static bool ContainsEmoji(ReadOnlySpan<char> text)
    {
        var index = 0;

        while (index < text.Length)
        {
            var rune = ReadRune(text, index, out var consumed);
            if (IsEmojiRune(rune))
            {
                return true;
            }

            index += consumed;
        }

        return false;
    }

    private static bool IsEmojiRune(Rune rune) =>
        rune.Value is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139
        or 0x2194 or 0x2195 or 0x2196 or 0x2197 or 0x2198 or 0x2199
        or >= 0x231A and <= 0x231B
        or >= 0x23E9 and <= 0x23FA
        or >= 0x2460 and <= 0x24FF
        or >= 0x25AA and <= 0x27BF
        or >= 0x1F000 and <= 0x1FAFF;

    private static ScriptGroup DetectScriptGroup(ReadOnlySpan<char> piece)
    {
        var detected = ScriptGroup.None;
        var index = 0;

        while (index < piece.Length)
        {
            var rune = ReadRune(piece, index, out var consumed);
            index += consumed;

            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            var group = ClassifyRune(rune);
            if (group == ScriptGroup.None)
            {
                continue;
            }

            if (detected == ScriptGroup.None)
            {
                detected = group;
                continue;
            }

            if (detected != group)
            {
                return ScriptGroup.Mixed;
            }
        }

        return detected == ScriptGroup.None ? ScriptGroup.Other : detected;
    }

    private static ScriptGroup ClassifyRune(Rune rune)
    {
        var value = rune.Value;

        if (value is <= 0x024F or >= 0x1E00 and <= 0x1EFF)
        {
            return ScriptGroup.Latin;
        }

        if (value is >= 0x0370 and <= 0x03FF or >= 0x1F00 and <= 0x1FFF
            or >= 0x0400 and <= 0x052F or >= 0x2DE0 and <= 0x2DFF or >= 0xA640 and <= 0xA69F)
        {
            return ScriptGroup.CyrillicOrGreek;
        }

        if (value is >= 0x0590 and <= 0x05FF or >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08FF)
        {
            return ScriptGroup.ArabicHebrew;
        }

        if (value is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF)
        {
            return ScriptGroup.CjkOrKanaOrHangul;
        }

        return ScriptGroup.Other;
    }

    private static bool AllSameChar(ReadOnlySpan<char> value)
    {
        if (value.Length <= 1)
        {
            return true;
        }

        var first = ReadRune(value, 0, out var consumed);

        for (var index = consumed; index < value.Length;)
        {
            var next = ReadRune(value, index, out var nextConsumed);
            if (next != first)
            {
                return false;
            }

            index += nextConsumed;
        }

        return true;
    }

    private static int CeilingDiv(int value, int divisor)
    {
        if (value <= 0)
        {
            return 0;
        }

        return (value + divisor - 1) / divisor;
    }

    private readonly record struct ToolField(string Value, bool IsStructured);

    private enum ScriptGroup
    {
        None,
        Latin,
        CyrillicOrGreek,
        ArabicHebrew,
        CjkOrKanaOrHangul,
        Other,
        Mixed
    }
}
