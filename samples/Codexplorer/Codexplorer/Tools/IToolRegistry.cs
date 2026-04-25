using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Exposes cached tool schemas and executes workspace-scoped tools by name.
/// </summary>
/// <remarks>
/// This abstraction keeps tool discovery and tool dispatch behind one boundary so the agent loop can
/// advertise one stable schema list to the model and route later tool calls back through the same
/// registry instance.
/// </remarks>
public interface IToolRegistry
{
    /// <summary>
    /// Returns cached OpenAI-compatible tool definitions for all registered tools.
    /// </summary>
    /// <returns>A stable list of function tool schemas.</returns>
    IReadOnlyList<ToolSchema> GetSchemas();

    /// <summary>
    /// Executes one registered tool against one cloned workspace.
    /// </summary>
    /// <param name="toolName">The registered tool name to execute.</param>
    /// <param name="arguments">The raw JSON argument payload supplied by the model.</param>
    /// <param name="workspace">The workspace that constrains filesystem access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>The tool result as plain text for the model.</returns>
    /// <exception cref="UnknownToolException">Thrown when <paramref name="toolName"/> is not registered.</exception>
    /// <exception cref="PathEscapeException">Thrown when a path argument tries to escape <paramref name="workspace"/>.</exception>
    Task<string> ExecuteAsync(string toolName, JsonElement arguments, WorkspaceModel workspace, CancellationToken ct);
}

/// <summary>
/// Represents one OpenAI-compatible function tool definition.
/// </summary>
/// <param name="Type">Tool kind. OpenAI function tools use <c>function</c>.</param>
/// <param name="Function">Function metadata and JSON parameter schema.</param>
public sealed record ToolSchema(string Type, ToolFunctionSchema Function)
{
    /// <summary>
    /// Creates one OpenAI-compatible function tool definition from a JSON schema document.
    /// </summary>
    /// <param name="name">The unique tool name.</param>
    /// <param name="description">The tool description shown to the model.</param>
    /// <param name="parametersJson">The JSON schema document for the tool parameters.</param>
    /// <returns>A cached-ready function tool definition.</returns>
    public static ToolSchema CreateFunction(string name, string description, string parametersJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        using var schemaDocument = JsonDocument.Parse(parametersJson);
        return new ToolSchema("function", new ToolFunctionSchema(name, description, schemaDocument.RootElement.Clone()));
    }
}

/// <summary>
/// Holds metadata for one OpenAI-compatible function tool.
/// </summary>
/// <param name="Name">The unique function name the model calls.</param>
/// <param name="Description">The load-bearing description that helps the model choose this tool.</param>
/// <param name="Parameters">The JSON schema for the function arguments.</param>
public sealed record ToolFunctionSchema(string Name, string Description, JsonElement Parameters);
