using System.Net.Http;
using System.Text.Json;
using TokenGuard.Core.Abstractions;

namespace Codexplorer.Tools;

/// <summary>
/// Routes tool execution and exposes cached tool schemas.
/// </summary>
/// <remarks>
/// This registry is intentionally small and static: Codexplorer only exposes a fixed tool set, and
/// this type owns that list directly so schema publication and execution dispatch stay centralized in
/// one place even when individual tools need DI-managed collaborators.
/// </remarks>
public sealed class ToolRegistry : IToolRegistry
{
    private static readonly JsonSerializerOptions ArgumentSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyDictionary<string, IWorkspaceTool> _toolsByName;
    private readonly IReadOnlyDictionary<string, IArtifactTool> _artifactToolsByName;
    private readonly IReadOnlyList<ToolSchema> _schemas;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used by web-backed tools.</param>
    /// <param name="tokenCounter">Token counter used by token-aware tools.</param>
    /// <param name="braveSearchSettings">Resolved Brave Search settings for <see cref="WebSearchTool"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="httpClientFactory"/>, <paramref name="tokenCounter"/>, or
    /// <paramref name="braveSearchSettings"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when tool names collide.</exception>
    public ToolRegistry(
        IHttpClientFactory httpClientFactory,
        ITokenCounter tokenCounter,
        BraveSearchSettings braveSearchSettings)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentNullException.ThrowIfNull(braveSearchSettings);

        IWorkspaceTool[] toolList =
        [
            new ListDirectoryTool(),
            new ReadFileTool(),
            new ReadRangeTool(),
            new EditFileTool(),
            new GrepTool(),
            new FindFilesTool(),
            new FileTreeTool(),
            new WebSearchTool(httpClientFactory, braveSearchSettings),
            new WebFetchTool(httpClientFactory, tokenCounter)
        ];

        IArtifactTool[] artifactToolList =
        [
            new CreateArtifactTool(),
            new WriteArtifactTool(),
            new ReadArtifactTool(),
            new ListArtifactsTool()
        ];

        var duplicateName = toolList.Select(tool => tool.Name).Concat(artifactToolList.Select(tool => tool.Name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?
            .Key;

        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"Tool name '{duplicateName}' is registered more than once.");
        }

        this._toolsByName = toolList.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        this._artifactToolsByName = artifactToolList.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        this._schemas = toolList.Select(tool => tool.Schema).Concat(artifactToolList.Select(tool => tool.Schema)).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolSchema> GetSchemas() => this._schemas;

    /// <inheritdoc />
    public Task<string> ExecuteAsync(string toolName, JsonElement arguments, ToolContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(context);

        if (this._artifactToolsByName.TryGetValue(toolName, out var artifactTool))
        {
            return artifactTool.ExecuteAsync(arguments, context.ArtifactsDirectory, ct);
        }

        if (!this._toolsByName.TryGetValue(toolName, out var tool))
        {
            throw new UnknownToolException(toolName);
        }

        return tool.ExecuteAsync(arguments, context.Workspace, ct);
    }

    internal static TParameters DeserializeArguments<TParameters>(JsonElement arguments)
    {
        var parameters = arguments.Deserialize<TParameters>(ArgumentSerializerOptions);
        return parameters ?? throw new JsonException($"Failed to deserialize tool arguments for {typeof(TParameters).Name}.");
    }
}
