namespace Codexplorer.Tools;

/// <summary>
/// Represents an attempt to execute a tool name that the registry does not know.
/// </summary>
/// <remarks>
/// The agent loop should treat this as a programming or prompting error because tool names come from
/// the registry schema list and should stay in sync with later execution requests.
/// </remarks>
public sealed class UnknownToolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownToolException"/> class.
    /// </summary>
    /// <param name="toolName">The unknown tool name.</param>
    public UnknownToolException(string toolName)
        : base($"Unknown tool: {toolName}")
    {
        this.ToolName = toolName;
    }

    /// <summary>
    /// Gets the unknown tool name that caused this exception.
    /// </summary>
    public string ToolName { get; }
}
