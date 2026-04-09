namespace SemanticFold.Core.Models.Content;

/// <summary>
/// Represents the result of a tool execution.
/// </summary>
public sealed record ToolResultContent : ContentBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolResultContent"/> record.
    /// </summary>
    /// <param name="ToolCallId">The tool call identifier this result corresponds to.</param>
    /// <param name="ToolName">The name of the tool that produced this result.</param>
    /// <param name="Content">The tool output payload.</param>
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
    /// Gets the tool call identifier this result corresponds to.
    /// </summary>
    public string ToolCallId { get; init; }

    /// <summary>
    /// Gets the name of the tool that produced this result.
    /// </summary>
    public string ToolName { get; init; }

    /// <summary>
    /// Gets the tool output payload.
    /// </summary>
    public string Content { get; init; }
}
