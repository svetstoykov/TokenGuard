using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Finds files by glob pattern under one workspace directory.
/// </summary>
/// <remarks>
/// Use this to locate candidate files by name or folder pattern before reading or grepping them.
/// Matching stays inside the workspace and results are always returned as workspace-relative paths.
/// </remarks>
public sealed class FindFilesTool : IWorkspaceTool
{
    /// <summary>
    /// Maximum number of file paths returned by one call.
    /// </summary>
    public const int EntryCap = 500;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "find_files",
        "Find workspace files by glob pattern. Use this when you know filename or folder shape and want matching paths without reading file contents.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "glob": {
              "type": "string",
              "description": "Glob pattern such as \"**/*.cs\" or \"src/**/Program.cs\"."
            },
            "path": {
              "type": "string",
              "description": "Optional workspace-relative directory that scopes where matching starts."
            }
          },
          "required": ["glob"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "find_files";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="FindFilesTool"/>.
    /// </summary>
    /// <param name="Glob">The glob pattern to evaluate.</param>
    /// <param name="Path">The optional workspace-relative directory scope.</param>
    public sealed record Parameters(string Glob, string? Path);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Finds files that match one glob under one workspace path.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains file access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>Matching workspace-relative file paths, a truncation marker, or a recoverable error string.</returns>
    public Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(parameters.Glob))
        {
            return Task.FromResult("Error: glob is required");
        }

        var requestedPath = string.IsNullOrWhiteSpace(parameters.Path) ? "." : parameters.Path;
        var resolvedPath = PathGuard.ResolvePath(workspace.LocalPath, requestedPath);

        if (!Directory.Exists(resolvedPath))
        {
            return Task.FromResult($"Error: directory not found: {ToolResultFormatting.NormalizePath(requestedPath)}");
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(ToolResultFormatting.NormalizePath(parameters.Glob));

        var matches = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(resolvedPath)))
            .Files
            .Select(match => ToolResultFormatting.NormalizePath(Path.Combine(requestedPath, match.Path)))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builder = new StringBuilder();

        foreach (var match in matches.Take(EntryCap))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(match);
        }

        if (matches.Length > EntryCap)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(ToolResultFormatting.TruncationMarker(matches.Length - EntryCap, EntryCap, "entries"));
        }

        return Task.FromResult(builder.ToString());
    }
}
