using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Tests.Summarization;

public sealed class ConversationSummaryPromptTests
{
    [Fact]
    public void SystemPrompt_UsesSharedContinuationTemplate()
    {
        const string expected =
            """
            Provide a detailed prompt for continuing our conversation above.

            Focus on information that would be helpful for continuing the conversation, including what we did,
            what we're doing, which files we're working on, and what we're going to do next.

            The summary that you construct will be used so that another agent can read it and continue the work.
            Follow the provided token limit as closely as possible. This is critical.
            Transcript uses compact markers to save tokens:
            - Message headers use [index|role] where role is sys, user, model, or tool.
            - Segment prefixes use t: for text, u: for tool use, r: for tool result, and c: for any other content.
            Pinned messages are excluded from the transcript because they are not compactable.

            When constructing the summary, try to stick to this template:

            ---

            ## Goal
            [What goal(s) is the user trying to accomplish?]

            ## Instructions
            - [What important instructions did the user give you that are relevant]
            - [If there is a plan or spec, include information about it so next agent can continue using it]

            ## Discoveries
            [What notable things were learned during this conversation that would be useful for the next agent]

            ## Accomplished
            [What work has been completed, what work is still in progress, and what work is left?]

            ## Relevant files / directories
            [Construct a structured list of relevant files that have been read, edited, or created that pertain
            to the task at hand. If all the files in a directory are relevant, include the path to the directory.]

            ---
            """;

        Assert.Equal(expected, ConversationSummaryPrompt.SystemPrompt);
    }

    [Fact]
    public void BuildUserPrompt_FormatsTranscriptCompactly()
    {
        var messages = new List<ContextMessage>
        {
            new()
            {
                Role = MessageRole.System,
                State = CompactionState.Original,
                IsPinned = true,
                Segments = [new TextContent("Keep XML docs.")],
            },
            new()
            {
                Role = MessageRole.Model,
                State = CompactionState.Masked,
                Segments =
                [
                    new TextContent("Investigating summarizer."),
                    new ToolUseContent("call-1", "view", "{\"path\":\"src/File.cs\"}"),
                    new ToolResultContent("call-1", "view", "file contents"),
                ],
            },
        };

        var prompt = ConversationSummaryPrompt.BuildUserPrompt(messages, 256);

        const string expected =
            """
            Limit: <= 256 tokens.

            Transcript:
            [1|sys]
            t:Keep XML docs.

            [2|model]
            t:Investigating summarizer.
            u:view|call-1|{"path":"src/File.cs"}
            r:view|call-1|file contents
            """;

        Assert.Equal(expected, prompt);
    }

    [Fact]
    public void FormatTranscript_WithUnknownSegmentType_PreservesPayload()
    {
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromContent(MessageRole.Tool, new CustomSegment("payload")),
        };

        var transcript = ConversationSummaryPrompt.FormatTranscript(messages);

        Assert.Equal(
            """
            [1|tool]
            c:CustomSegment|payload
            """,
            transcript);
    }

    private sealed record CustomSegment(string Value) : ContentSegment(Value);
}
