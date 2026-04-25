namespace Codexplorer.Tools;

/// <summary>
/// Represents an attempt to access a path outside the workspace root.
/// </summary>
/// <remarks>
/// This exception protects Codexplorer's read-only tools from directory traversal, absolute path
/// access, and symlink hops that would otherwise expose files outside the cloned repository.
/// </remarks>
public sealed class PathEscapeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PathEscapeException"/> class.
    /// </summary>
    /// <param name="path">The user-supplied path that failed validation.</param>
    /// <param name="workspaceRoot">The normalized workspace root used for validation.</param>
    /// <param name="reason">The reason the path was rejected.</param>
    public PathEscapeException(string path, string workspaceRoot, string reason)
        : base($"Path '{path}' escapes workspace root '{workspaceRoot}'. {reason}")
    {
        this.Path = path;
        this.WorkspaceRoot = workspaceRoot;
        this.Reason = reason;
    }

    /// <summary>
    /// Gets the user-supplied path that failed validation.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the normalized workspace root used for validation.
    /// </summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// Gets the specific reason the path was rejected.
    /// </summary>
    public string Reason { get; }
}
