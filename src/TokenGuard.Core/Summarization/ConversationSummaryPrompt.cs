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
        - Tool use entries include only tool name and call id.
        - Tool result entries use one of three forms:
          - full: the full tool payload is included for small outputs.
          - excerpt: metadata plus head/salient/tail excerpts are included for medium outputs.
          - meta: metadata only is included for very large outputs.
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
}
