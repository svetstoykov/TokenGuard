using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

internal interface IWorkspaceTool
{
    string Name { get; }

    ToolSchema Schema { get; }

    Task<string> ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct);
}
