using System.Text.Json;

namespace Codexplorer.Tools;

internal interface IArtifactTool
{
    string Name { get; }

    ToolSchema Schema { get; }

    Task<string> ExecuteAsync(JsonElement arguments, string artifactsDirectory, CancellationToken ct);
}
