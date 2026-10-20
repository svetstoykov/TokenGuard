using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed partial class ConversationContextToolResultLimitTests
{
    private const string NoticeStart = "[Tool result shortened to fit the context: ";
    private const string NoticeEnd = " omitted here. Call the tool again with a narrower request if you need them.]";

    [Fact]
    public async Task PrepareAsync_WhenOneToolResultIsFarAboveTheWindowWithTheDefaultLimit_ReturnsReadyOrCompacted()
    {
        // Arrange
        var grepOutput = GrepOutput(lines: 8_100);
        using var context = new ConversationContextFactory(ConversationConfigBuilder.Default(50_000)).Create();
        context.SetSystemPrompt("You are a code exploration assistant.");
        context.AddUserMessage("Find every call to Compute in the repository.");
        await context.PrepareAsync();
        context.RecordModelResponse([new ToolUseContent("call_1", "grep", "{\"pattern\":\"Compute\"}")]);
        context.RecordToolResult("call_1", "grep", grepOutput);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        CountToolResult(grepOutput).Should().BeInRange(240_000, 280_000, "the result is sized like the one in the 2026-10-13 run");
        result.Outcome.Should().BeOneOf(PrepareOutcome.Ready, PrepareOutcome.Compacted);
    }

    [Fact]
    public void RecordToolResult_WhenAboveTheLimit_StoresHeadLinesTheNoticeAndTailLines()
    {
        // Arrange
        var lines = NumberedLines(100, "\n");
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", string.Concat(lines));

        // Assert
        var stored = StoredText(context);
        var noticeIndex = stored.IndexOf(NoticeStart, StringComparison.Ordinal);
        noticeIndex.Should().BeGreaterThan(0, "the head keeps whole lines before the notice");
        var head = stored[..noticeIndex];
        var tail = stored[(stored.IndexOf(NoticeEnd, StringComparison.Ordinal) + NoticeEnd.Length + 1)..];
        string.Concat(lines).Should().StartWith(head).And.EndWith(tail);
        head.Should().EndWith("\n");
        tail.Should().NotBeEmpty().And.StartWith("line-");
    }

    [Fact]
    public void RecordToolResult_WhenAboveTheLimit_StoresAnEstimateAtOrUnderTheLimit()
    {
        // Arrange
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", string.Concat(NumberedLines(100, "\n")));

        // Assert
        context.History[^1].TokenCount.Should().BeLessThanOrEqualTo(400);
        StoredText(context).Length.Should().BeLessThanOrEqualTo(400);
    }

    [Fact]
    public void RecordToolResult_WhenAboveTheLimit_NoticeStatesTheOmittedAndTotalLineCounts()
    {
        // Arrange
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", string.Concat(NumberedLines(100, "\n")));

        // Assert
        var stored = StoredText(context);
        var match = LineNotice().Match(stored);
        match.Success.Should().BeTrue();
        var keptLines = stored.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(line => line.StartsWith("line-", StringComparison.Ordinal));
        int.Parse(match.Groups["total"].Value).Should().Be(100);
        int.Parse(match.Groups["omitted"].Value).Should().Be(100 - keptLines);
    }

    [Fact]
    public void RecordToolResult_WhenAtTheLimit_StoresTheContentUnchanged()
    {
        // Arrange
        var content = new string('x', 400);
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", content);

        // Assert
        StoredText(context).Should().Be(content);
    }

    [Fact]
    public async Task PrepareAsync_WhenEveryResultIsUnderTheLimit_ReturnsTheSameViewAsAContextWithoutALimit()
    {
        // Arrange
        using var limited = CreateContext(maxToolResultTokens: 400);
        using var unlimited = CreateContext(maxToolResultTokens: null);
        foreach (var context in new[] { limited, unlimited })
        {
            context.AddUserMessage("Find the files.");
            context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}")]);
            context.RecordToolResult("call_1", "search", string.Concat(NumberedLines(30, "\n")));
        }

        // Act
        var limitedResult = await limited.PrepareAsync();

        // Assert
        var unlimitedResult = await unlimited.PrepareAsync();
        limitedResult.Messages.Select(Describe).Should().Equal(unlimitedResult.Messages.Select(Describe));
        limitedResult.Outcome.Should().Be(unlimitedResult.Outcome);
    }

    [Fact]
    public void RecordToolResult_WhenTheLimitIsOff_StoresAResultFarAboveTheWindowWhole()
    {
        // Arrange
        var content = string.Concat(NumberedLines(1_000, "\n"));
        using var context = CreateContext(maxToolResultTokens: null);

        // Act
        context.RecordToolResult("call_1", "search", content);

        // Assert
        StoredText(context).Should().Be(content);
    }

    [Fact]
    public void RecordToolResult_WhenASingleLineIsAboveTheLimit_CutsByCharactersAndTheNoticeSaysCharacters()
    {
        // Arrange
        var content = string.Concat(Enumerable.Range(0, 200).Select(index => $"token{index:D4} "));
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", content);

        // Assert
        var stored = StoredText(context);
        var match = CharacterNotice().Match(stored);
        match.Success.Should().BeTrue();
        int.Parse(match.Groups["total"].Value).Should().Be(content.Length);
        content.Should().StartWith(stored[..match.Index]);
        content.Should().EndWith(stored[(match.Index + match.Length)..]);
        stored.Length.Should().BeLessThanOrEqualTo(400);
    }

    [Fact]
    public void RecordToolResult_WhenCutByCharacters_NeverSplitsASurrogatePair()
    {
        // Arrange
        var content = string.Concat(Enumerable.Repeat("\U0001F600", 400));
        var storedTexts = new List<string>();

        // Act
        for (var limit = 160; limit <= 260; limit++)
        {
            using var context = CreateContext(maxToolResultTokens: limit);
            context.RecordToolResult("call_1", "search", content);
            storedTexts.Add(StoredText(context));
        }

        // Assert
        storedTexts.Should().OnlyContain(text => HasNoLoneSurrogate(text));
        storedTexts.Should().OnlyContain(text => text.Contains(" characters omitted here.", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordToolResult_WhenTheFirstLineIsLongerThanItsHalf_CutsByCharacters()
    {
        // Arrange
        var content = new string('a', 1_000) + "\n" + string.Concat(NumberedLines(20, "\n"));
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", content);

        // Assert
        var stored = StoredText(context);
        CharacterNotice().IsMatch(stored).Should().BeTrue();
        stored.Should().StartWith("aaaa");
        stored.Length.Should().BeLessThanOrEqualTo(400);
    }

    [Fact]
    public void RecordToolResult_WhenLinesEndWithCarriageReturnLineFeed_KeepsEveryKeptLineWhole()
    {
        // Arrange
        var lines = NumberedLines(100, "\r\n");
        using var context = CreateContext(maxToolResultTokens: 400);

        // Act
        context.RecordToolResult("call_1", "search", string.Concat(lines));

        // Assert
        var stored = StoredText(context);
        var keptLines = stored.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        keptLines.Where(line => !line.StartsWith(NoticeStart, StringComparison.Ordinal))
            .Should().OnlyContain(line => lines.Contains(line + "\r\n"));
        stored.Should().NotContain("\n\n").And.NotMatchRegex("[^\r]\n");
    }

    [Fact]
    public void RecordToolResult_WhenTheLimitIsSmallerThanTheNotice_StoresTheNoticeAlone()
    {
        // Arrange
        var content = string.Concat(NumberedLines(100, "\n"));
        using var context = CreateContext(maxToolResultTokens: 10);

        // Act
        context.RecordToolResult("call_1", "search", content);

        // Assert
        StoredText(context).Should().Be(NoticeStart + "100 of 100 lines" + NoticeEnd);
    }

    [Fact]
    public void RecordToolResult_WhenAboveTheLimit_ShortensOnTheScaleOfTheContextTokenCounter()
    {
        // Arrange
        using var context = CreateContext(maxToolResultTokens: 1_000);

        // Act
        context.RecordToolResult("call_1", "search", string.Concat(NumberedLines(1_000, "\n")));

        // Assert
        StoredText(context).Length.Should().BeInRange(900, 1_000, "the character-counting test counter fills the limit in characters");
    }

    [Fact]
    public void RecordToolResult_WhenTheToolNameIsWhitespaceAndTheContentIsAboveTheLimit_ThrowsArgumentException()
    {
        // Arrange
        using var context = CreateContext(maxToolResultTokens: 10);

        // Act
        var act = () => context.RecordToolResult("call_1", " ", string.Concat(NumberedLines(100, "\n")));

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("toolName");
        context.History.Should().BeEmpty();
    }

    private static ConversationContext CreateContext(int? maxToolResultTokens) =>
        new(new ContextBudget(100_000), new TextLengthTokenCounter(), StubCompactionStrategy.Unchanged(),
            new ConversationDiagnostics(NullLoggerFactory.Instance, ConversationDiagnostics.DefaultContextName), maxToolResultTokens);

    private static string StoredText(ConversationContext context) => context.History[^1].Segments.OfType<ToolResultContent>().Single().Content;

    private static string Describe(ContextMessage message) =>
        $"{message.Role}:{message.State}:{string.Concat(message.Segments.Select(segment => segment.Content))}";

    private static List<string> NumberedLines(int count, string terminator) =>
        Enumerable.Range(1, count).Select(line => $"line-{line:D4}{terminator}").ToList();

    private static bool HasNoLoneSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    return false;

                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountToolResult(string content) =>
        new EstimatedTokenCounter().Count(ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call_1", "grep", content)));

    private static string GrepOutput(int lines)
    {
        var builder = new StringBuilder();
        for (var line = 1; line <= lines; line++)
        {
            builder.Append($"src/Module{line % 97}/Service{line}.cs:{line}:        var value = Compute(input, {line}, options.Mode);\n");
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\[Tool result shortened to fit the context: (?<omitted>\d+) of (?<total>\d+) lines omitted here\. [^\]]*\]")]
    private static partial Regex LineNotice();

    [GeneratedRegex(@"\[Tool result shortened to fit the context: (?<omitted>\d+) of (?<total>\d+) characters omitted here\. [^\]]*\]")]
    private static partial Regex CharacterNotice();
}
