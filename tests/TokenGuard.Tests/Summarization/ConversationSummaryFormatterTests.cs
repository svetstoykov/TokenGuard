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

        var prompt = formatter.BuildUserPrompt(messages, 256);

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
