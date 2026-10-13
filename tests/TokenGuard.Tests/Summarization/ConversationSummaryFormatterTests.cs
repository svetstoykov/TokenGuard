using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Tests.Summarization;

public sealed class ConversationSummaryFormatterTests
{
    private readonly ConversationSummaryFormattingOptions _options = new(
        fullToolResultMaxTokens: 10,
        excerptToolResultMaxTokens: 20,
        excerptHeadLineCount: 2,
        excerptTailLineCount: 2,
        excerptSalientLineCount: 2);

    [Fact]
    public void BuildUserPrompt_UsesFormattedTranscript()
    {
        var counter = new FixedTokenCounter
        {
            ToolResultCounts =
            {
                ["file contents"] = 5,
            },
        };
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.System, "Keep XML docs."),
            new()
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new TextContent("Investigating summarizer."),
                    new ToolUseContent("call-1", "view", "{\"path\":\"src/File.cs\"}"),
                    new ToolResultContent("call-1", "view", "file contents"),
                ],
            },
        };

        var prompt = formatter.BuildUserPrompt(messages, 256, int.MaxValue);

        Assert.Equal(
            """
            Limit: <= 256 tokens.

            Transcript:
            [1|sys]
            t:Keep XML docs.

            [2|model]
            t:Investigating summarizer.
            u:view|call-1
            r:view|call-1|full
            file contents
            """,
            prompt);
    }

    [Fact]
    public void BuildUserPrompt_WhenTheRequestIsAboveTheBudget_ShortensLongTextFromTheOldestEnd()
    {
        // Arrange
        var counter = new FixedTokenCounter();
        var options = new ConversationSummaryFormattingOptions(
            fullToolResultMaxTokens: 10, excerptToolResultMaxTokens: 1_000, excerptHeadLineCount: 1, excerptTailLineCount: 1,
            excerptSalientLineCount: 1);
        var formatter = new ConversationSummaryFormatter(counter, options);
        var messages = new[]
        {
            ContextMessage.FromText(MessageRole.User, Lines("alpha", 12)),
            ContextMessage.FromText(MessageRole.Model, "noted"),
            ContextMessage.FromText(MessageRole.User, Lines("beta", 12)),
            ContextMessage.FromText(MessageRole.Model, "done"),
        };
        var budget = RequestTokens(counter, formatter.BuildUserPrompt(messages, 256, int.MaxValue)) - 1;

        // Act
        var prompt = formatter.BuildUserPrompt(messages, 256, budget);

        // Assert
        Assert.Equal(
            $"""
            Limit: <= 256 tokens.

            Transcript:
            [1|user]
            t:excerpt(tokens=107,lines=12,kind=text)
            head:
            alpha 01
            tail:
            alpha 12

            [2|model]
            t:noted

            [3|user]
            t:{Lines("beta", 12)}

            [4|model]
            t:done
            """,
            prompt);
    }

    [Fact]
    public void BuildUserPrompt_WhenTheRequestIsAboveTheBudget_ReducesVeryLongTextToMetadata()
    {
        // Arrange
        var counter = new FixedTokenCounter();
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var messages = new[]
        {
            ContextMessage.FromText(MessageRole.User, Lines("alpha", 12)),
            ContextMessage.FromText(MessageRole.Model, "noted"),
        };
        var budget = RequestTokens(counter, formatter.BuildUserPrompt(messages, 256, int.MaxValue)) - 1;

        // Act
        var prompt = formatter.BuildUserPrompt(messages, 256, budget);

        // Assert
        Assert.Equal(
            """
            Limit: <= 256 tokens.

            Transcript:
            [1|user]
            t:meta(tokens=107,lines=12,chars=107,kind=text,truncated=true)

            [2|model]
            t:noted
            """,
            prompt);
    }

    [Fact]
    public void BuildUserPrompt_WhenShorteningTextIsNotEnough_LeavesOutTheOldestExchangesAndSaysHowMany()
    {
        // Arrange
        const string expected =
            """
            Limit: <= 256 tokens.

            Transcript:
            [2 earlier exchanges omitted]

            [6|user]
            t:third question

            [7|model]
            t:third answer
            """;
        var counter = new FixedTokenCounter();
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var budget = RequestTokens(counter, expected) + 10;

        // Act
        var prompt = formatter.BuildUserPrompt(CreateThreeExchanges(), 256, budget);

        // Assert
        Assert.Equal(expected, prompt);
    }

    [Fact]
    public void BuildUserPrompt_WhenExchangesAreLeftOut_ProducesARequestWithinTheBudget()
    {
        // Arrange
        var counter = new FixedTokenCounter();
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var messages = CreateThreeExchanges();
        var budget = RequestTokens(counter, formatter.BuildUserPrompt(messages, 256, int.MaxValue)) - 1;

        // Act
        var prompt = formatter.BuildUserPrompt(messages, 256, budget);

        // Assert
        Assert.Contains("[1 earlier exchange omitted]", prompt, StringComparison.Ordinal);
        Assert.True(RequestTokens(counter, prompt) <= budget);
    }

    [Fact]
    public void BuildUserPrompt_WhenTheNewestExchangeAloneIsAboveTheBudget_KeepsTheNewestExchange()
    {
        // Arrange
        var formatter = new ConversationSummaryFormatter(new FixedTokenCounter(), this._options);
        var messages = new[]
        {
            ContextMessage.FromText(MessageRole.User, "first question"),
            ContextMessage.FromText(MessageRole.Model, "first answer"),
            ContextMessage.FromText(MessageRole.User, "second question"),
        };

        // Act
        var prompt = formatter.BuildUserPrompt(messages, 256, requestTokenBudget: 1);

        // Assert
        Assert.Equal(
            """
            Limit: <= 256 tokens.

            Transcript:
            [1 earlier exchange omitted]

            [3|user]
            t:second question
            """,
            prompt);
    }

    [Fact]
    public void FormatTranscript_WithExcerptSizedToolResult_UsesDeterministicExcerpt()
    {
        var counter = new FixedTokenCounter
        {
            ToolResultCounts =
            {
                ["line 1\nline 2\nnoise\nERROR: boom\nline 5\nline 6"] = 15,
            },
        };
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var messages = new[]
        {
            ContextMessage.FromContent(
                MessageRole.Tool,
                new ToolResultContent("call-2", "bash", "line 1\nline 2\nnoise\nERROR: boom\nline 5\nline 6")),
        };

        var transcript = formatter.FormatTranscript(messages);

        Assert.Equal(
            """
            [1|tool]
            r:bash|call-2|excerpt(tokens=15,lines=6,kind=log)
            head:
            line 1
            line 2
            salient:
            ERROR: boom
            tail:
            line 5
            line 6
            """,
            transcript);
    }

    [Fact]
    public void FormatTranscript_WithProgrammingLanguageLine_TreatsItAsSalient()
    {
        var content = "line 1\nline 2\nMigrated parser from JavaScript to TypeScript\nline 5\nline 6";
        var counter = new FixedTokenCounter
        {
            ToolResultCounts =
            {
                [content] = 15,
            },
        };
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var messages = new[]
        {
            ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call-3", "bash", content)),
        };

        var transcript = formatter.FormatTranscript(messages);

        Assert.Equal(
            """
            [1|tool]
            r:bash|call-3|excerpt(tokens=15,lines=5,kind=text)
            head:
            line 1
            line 2
            salient:
            Migrated parser from JavaScript to TypeScript
            tail:
            line 5
            line 6
            """,
            transcript);
    }

    [Fact]
    public void FormatTranscript_WithOversizedToolResult_UsesMetadataOnly()
    {
        var content = "first line\nsecond line\nthird line";
        var counter = new FixedTokenCounter
        {
            ToolResultCounts =
            {
                [content] = 25,
            },
        };
        var formatter = new ConversationSummaryFormatter(counter, this._options);
        var messages = new[]
        {
            ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call-3", "grep", content)),
        };

        var transcript = formatter.FormatTranscript(messages);

        Assert.Equal(
            """
            [1|tool]
            r:grep|call-3|meta(tokens=25,lines=3,chars=33,kind=text,truncated=true)
            """,
            transcript);
    }

    [Fact]
    public void FormatTranscript_WithUnknownSegmentType_PreservesPayload()
    {
        var formatter = new ConversationSummaryFormatter(new FixedTokenCounter(), this._options);
        var messages = new[]
        {
            ContextMessage.FromContent(MessageRole.Tool, new CustomSegment("payload")),
        };

        var transcript = formatter.FormatTranscript(messages);

        Assert.Equal(
            """
            [1|tool]
            c:CustomSegment|payload
            """,
            transcript);
    }

    private static string Lines(string word, int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(number => $"{word} {number:00}"));

    /// <summary>
    ///     Counts the tokens of one summarizer request: the system prompt plus the given user prompt.
    /// </summary>
    private static int RequestTokens(FixedTokenCounter counter, string userPrompt) =>
        counter.Count(ContextMessage.FromText(MessageRole.User, ConversationSummaryPrompt.SystemPrompt))
        + counter.Count(ContextMessage.FromText(MessageRole.User, userPrompt));

    /// <summary>
    ///     Builds three exchanges, each opened by a user message; the first one holds a tool call and its result.
    /// </summary>
    private static ContextMessage[] CreateThreeExchanges() =>
    [
        ContextMessage.FromText(MessageRole.User, "first question"),
        ContextMessage.FromContent(MessageRole.Model, new ToolUseContent("call-1", "view", "{}")),
        ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call-1", "view", "contents")),
        ContextMessage.FromText(MessageRole.User, "second question"),
        ContextMessage.FromText(MessageRole.Model, "second answer"),
        ContextMessage.FromText(MessageRole.User, "third question"),
        ContextMessage.FromText(MessageRole.Model, "third answer"),
    ];

    private sealed record CustomSegment(string Value) : ContentSegment(Value);

    private sealed class FixedTokenCounter : ITokenCounter
    {
        public Dictionary<string, int> ToolResultCounts { get; } = new(StringComparer.Ordinal);

        public int Count(ContextMessage contextMessage)
        {
            return contextMessage.Segments.Sum(segment => segment switch
            {
                ToolResultContent toolResult when this.ToolResultCounts.TryGetValue(toolResult.Content, out var count) => count,
                ToolResultContent toolResult => toolResult.Content.Length,
                TextContent text => text.Content.Length,
                ToolUseContent => 1,
                _ => 1,
            });
        }

        public int Count(IEnumerable<ContextMessage> messages)
        {
            return messages.Sum(this.Count);
        }
    }
}
