using System.Text;
using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Routes tool execution and exposes cached tool schemas.
/// </summary>
/// <remarks>
/// This registry is intentionally small and static: Codexplorer only exposes six read-only workspace
/// tools, so one in-memory map keeps schema publication and execution dispatch deterministic.
/// </remarks>
public sealed class ToolRegistry : IToolRegistry
{
    private static readonly JsonSerializerOptions ArgumentSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyList<IWorkspaceTool> Tools =
    [
        new ListDirectoryTool(),
        new ReadFileTool(),
        new ReadRangeTool(),
        new GrepTool(),
        new FindFilesTool(),
        new FileTreeTool()
    ];

    private static readonly IReadOnlyDictionary<string, IWorkspaceTool> ToolsByName = Tools
        .ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ToolSchema> Schemas = Tools
        .Select(tool => tool.Schema)
        .ToArray();

    /// <inheritdoc />
    public IReadOnlyList<ToolSchema> GetSchemas() => Schemas;

    /// <inheritdoc />
    public Task<string> ExecuteAsync(string toolName, JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(workspace);

        if (!ToolsByName.TryGetValue(toolName, out var tool))
        {
            throw new UnknownToolException(toolName);
        }

        return tool.ExecuteAsync(arguments, workspace, ct);
    }

    internal static TParameters DeserializeArguments<TParameters>(JsonElement arguments)
    {
        var parameters = arguments.Deserialize<TParameters>(ArgumentSerializerOptions);
        return parameters ?? throw new JsonException($"Failed to deserialize tool arguments for {typeof(TParameters).Name}.");
    }
}

internal interface IWorkspaceTool
{
    string Name { get; }

    ToolSchema Schema { get; }

    Task<string> ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct);
}

internal static class ToolResultFormatting
{
    public static string TruncationMarker(int omittedCount, int cap, string unit)
    {
        return $"[... truncated: {omittedCount} more {unit}; cap {cap} {unit} hit ...]";
    }

    public static string ToWorkspaceRelativePath(WorkspaceModel workspace, string absolutePath)
    {
        var workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.LocalPath));
        var relativePath = Path.GetRelativePath(workspaceRoot, absolutePath);
        return NormalizePath(relativePath);
    }

    public static string NormalizePath(string path)
    {
        var normalizedPath = path
            .Replace('\\', '/')
            .Replace(Path.DirectorySeparatorChar, '/');

        return normalizedPath.StartsWith("./", StringComparison.Ordinal)
            ? normalizedPath[2..]
            : normalizedPath;
    }
}

internal static class ToolFileHelpers
{
    public static async Task<bool> IsBinaryFileAsync(string path, CancellationToken ct)
    {
        const int probeLength = 4096;
        var buffer = new byte[probeLength];

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: probeLength,
            useAsync: true);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, probeLength), ct).ConfigureAwait(false);

        for (var index = 0; index < bytesRead; index++)
        {
            if (buffer[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildTextResult(IReadOnlyList<string> lines, int totalLineCount, int cap)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        if (totalLineCount <= cap)
        {
            return string.Join(Environment.NewLine, lines);
        }

        var builder = new StringBuilder();
        builder.AppendJoin(Environment.NewLine, lines);
        builder.AppendLine();
        builder.Append(ToolResultFormatting.TruncationMarker(totalLineCount - lines.Count, cap, "lines"));
        return builder.ToString();
    }
}
