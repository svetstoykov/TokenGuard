using System.Text;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core.Summarization;

internal static class ConversationSummaryPrompt
{
    internal const string SystemPrompt =
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

    internal static string BuildUserPrompt(IReadOnlyList<ContextMessage> messages, int targetTokens)
    {
        return $"""
                Limit: <= {targetTokens} tokens.

                Transcript:
                {FormatTranscript(messages)}
                """;
    }

    internal static string FormatTranscript(IReadOnlyList<ContextMessage> messages)
    {
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
                .Append(FormatRole(message.Role));
            builder.AppendLine("]");

            foreach (var segment in message.Segments)
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
                            .Append(toolUse.ToolCallId)
                            .Append('|')
                            .AppendLine(toolUse.Content);
                        break;
                    case ToolResultContent toolResult:
                        builder.Append("r:")
                            .Append(toolResult.ToolName)
                            .Append('|')
                            .Append(toolResult.ToolCallId)
                            .Append('|')
                            .AppendLine(toolResult.Content);
                        break;
                    default:
                        builder.Append("c:")
                            .Append(segment.GetType().Name)
                            .Append('|')
                            .AppendLine(segment.Content);
                        break;
                }
            }

            if (i < messages.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd('\r', '\n');
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
}
