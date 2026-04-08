namespace SemanticFold;

/// <summary>
/// Represents a tool call requested by the assistant.
/// </summary>
public sealed record ToolUseContent : ContentBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolUseContent"/> record.
    /// </summary>
    /// <param name="ToolCallId">The unique identifier for this tool call.</param>
    /// <param name="ToolName">The name of the tool being invoked.</param>
    /// <param name="ArgumentsJson">The raw JSON string containing tool arguments.</param>
    /// <exception cref="ArgumentException">Thrown when any argument is null or whitespace.</exception>
    public ToolUseContent(string ToolCallId, string ToolName, string ArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(ToolCallId))
        {
            throw new ArgumentException("Tool call id cannot be null or whitespace.", nameof(ToolCallId));
        }

        if (string.IsNullOrWhiteSpace(ToolName))
        {
            throw new ArgumentException("Tool name cannot be null or whitespace.", nameof(ToolName));
        }

        if (string.IsNullOrWhiteSpace(ArgumentsJson))
        {
            throw new ArgumentException("Arguments JSON cannot be null or whitespace.", nameof(ArgumentsJson));
        }

        this.ToolCallId = ToolCallId;
        this.ToolName = ToolName;
        this.ArgumentsJson = ArgumentsJson;
    }

    /// <summary>
    /// Gets the unique identifier for this tool call.
    /// </summary>
    public string ToolCallId { get; init; }

    /// <summary>
    /// Gets the name of the tool being invoked.
    /// </summary>
    public string ToolName { get; init; }

    /// <summary>
    /// Gets the tool arguments as raw JSON.
    /// </summary>
    public string ArgumentsJson { get; init; }
}
