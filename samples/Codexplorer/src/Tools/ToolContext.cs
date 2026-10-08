using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
///     Represents the two roots a session's tools work against.
/// </summary>
/// <param name="Workspace">The cloned repository the repository tools read.</param>
/// <param name="ArtifactsDirectory">The absolute path of the session's <c>artifacts</c> folder that roots the artifact tools.</param>
public sealed record ToolContext(WorkspaceModel Workspace, string ArtifactsDirectory);
