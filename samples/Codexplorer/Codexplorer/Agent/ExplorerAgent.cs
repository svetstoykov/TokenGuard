using System.Text.Json;
using Codexplorer.CLI;
using Codexplorer.Configuration;
using Codexplorer.Sessions;
using Codexplorer.Tools;
using OpenAI;
using OpenAI.Chat;
using Microsoft.Extensions.Options;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.OpenAI;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Agent;

/// <summary>
/// Runs the Codexplorer repository agent loop over TokenGuard and OpenRouter.
/// </summary>
/// <remarks>
/// This service owns session creation for the Codexplorer repository agent loop. Each started session receives its own
/// TokenGuard conversation context, transcript logger, renderer task, and OpenRouter chat client while still sharing
/// the application's stable tool registry and validated configuration.
/// </remarks>
internal sealed class ExplorerAgent : IExplorerAgent
{
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    private readonly IConversationContextFactory _conversationContextFactory;
    private readonly ISessionLoggerFactory _sessionLoggerFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly CodexplorerOptions _options;
    private readonly IReadOnlyList<ChatTool> _chatTools;
    private readonly SessionRenderer _sessionRenderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplorerAgent"/> class.
    /// </summary>
    /// <param name="conversationContextFactory">Factory for fresh TokenGuard conversation contexts.</param>
    /// <param name="toolRegistry">Registry for workspace-scoped tools.</param>
    /// <param name="sessionLoggerFactory">Factory for per-query session transcripts.</param>
    /// <param name="sessionRenderer">Session renderer for generating human-readable output.</param>
    /// <param name="options">The validated Codexplorer options snapshot.</param>
    public ExplorerAgent(
        IConversationContextFactory conversationContextFactory,
        IToolRegistry toolRegistry,
        ISessionLoggerFactory sessionLoggerFactory,
        SessionRenderer sessionRenderer,
        IOptions<CodexplorerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(conversationContextFactory);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(sessionLoggerFactory);
        ArgumentNullException.ThrowIfNull(sessionRenderer);
        ArgumentNullException.ThrowIfNull(options);

        this._conversationContextFactory = conversationContextFactory;
        this._toolRegistry = toolRegistry;
        this._sessionLoggerFactory = sessionLoggerFactory;
        this._sessionRenderer = sessionRenderer;
        this._options = options.Value;
        this._chatTools = this._toolRegistry.GetSchemas()
            .Select(CreateChatTool)
            .ToArray();
    }

    /// <inheritdoc />
    public IExplorerSession StartSession(WorkspaceModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var agentOptions = this._options.Agent
            ?? throw new InvalidOperationException("Codexplorer agent options are not configured.");
        var modelOptions = this._options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");
        var conversationContext = this._conversationContextFactory.Create();
        var sessionLogger = this._sessionLoggerFactory.BeginSession(workspace, "Interactive repo chat");
        var rendererTask = this._sessionRenderer.RenderAsync(sessionLogger, CancellationToken.None);
        var chatClient = CreateChatClient(this._options);

        conversationContext.SetSystemPrompt(SystemPrompt.Text);

        return new ExplorerSession(
            workspace,
            conversationContext,
            sessionLogger,
            rendererTask,
            this._toolRegistry,
            chatClient,
            this._chatTools,
            agentOptions,
            modelOptions);
    }

    internal static ChatCompletionOptions CreateChatCompletionOptions(IReadOnlyList<ChatTool> chatTools, int maxOutputTokens)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxOutputTokens,
            ToolChoice = ChatToolChoice.CreateAutoChoice(),
            AllowParallelToolCalls = false
        };

        foreach (var chatTool in chatTools)
        {
            options.Tools.Add(chatTool);
        }

        return options;
    }

    private static ChatClient CreateChatClient(CodexplorerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var openRouterOptions = options.OpenRouter
            ?? throw new InvalidOperationException("Codexplorer OpenRouter options are not configured.");
        var modelOptions = options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");
        var apiKey = openRouterOptions.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Codexplorer OpenRouter API key is not configured.");
        }

        var modelName = modelOptions.Name
            ?? throw new InvalidOperationException("Codexplorer model name is not configured.");
        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = OpenRouterEndpoint });

        return client.GetChatClient(modelName);
    }

    private static ChatTool CreateChatTool(ToolSchema schema)
    {
        return ChatTool.CreateFunctionTool(
            schema.Function.Name,
            schema.Function.Description,
            BinaryData.FromString(schema.Function.Parameters.GetRawText()),
            functionSchemaIsStrict: false);
    }

    internal static JsonElement ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

}
