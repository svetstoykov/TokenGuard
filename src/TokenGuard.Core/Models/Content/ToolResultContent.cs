namespace TokenGuard.Core.Models.Content;

/// <summary>
/// Represents the recorded output of a completed tool invocation.
/// </summary>
/// <remarks>
/// <see cref="ToolResultContent"/> closes the loop started by <see cref="ToolUseContent"/>. The stored values let the
/// model inspect prior tool outputs, and they give compaction strategies enough structure to preserve or mask those
/// outputs without losing the surrounding conversation flow.
/// </remarks>
public sealed record ToolResultContent : ContentSegment
{
    /// <summary>
    /// Initializes a new <see cref="ToolResultContent"/> instance.
    /// </summary>
    /// <remarks>
    /// The constructor enforces the minimum data required to round-trip tool output back to the model and to support
    /// later masking or inspection.
    /// </remarks>
    /// <param name="ToolCallId">The identifier of the original tool call this result answers.</param>
    /// <param name="ToolName">The name of the tool that produced the result.</param>
    /// <param name="Content">The raw tool output payload to record in conversation history.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ToolCallId"/> or <paramref name="ToolName"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="Content"/> is null.</exception>
    public ToolResultContent(string ToolCallId, string ToolName, string Content)
    {
        if (string.IsNullOrWhiteSpace(ToolCallId))
        {
            throw new ArgumentException("Tool call id cannot be null or whitespace.", nameof(ToolCallId));
        }

        if (string.IsNullOrWhiteSpace(ToolName))
        {
            throw new ArgumentException("Tool name cannot be null or whitespace.", nameof(ToolName));
        }

        ArgumentNullException.ThrowIfNull(Content);

        this.ToolCallId = ToolCallId;
        this.ToolName = ToolName;
        this.Content = Content;
    }

    /// <summary>
    /// Gets the identifier of the tool call this result corresponds to.
    /// </summary>
    public string ToolCallId { get; init; }

    /// <summary>
    /// Gets the name of the tool that produced this result.
    /// </summary>
    public string ToolName { get; init; }

    /// <summary>
    /// Gets the raw tool output payload recorded for this result.
    /// </summary>
    public string Content { get; init; }
}
