using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Extensions.Anthropic;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops.Providers;

internal sealed class AnthropicAgentLoopProvider : IAgentLoopProvider
{
    private readonly AnthropicClient _client;

    public AnthropicAgentLoopProvider(IConfiguration configuration, AgentLoopOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        this.ModelId = options.ModelId;
        this._client = new AnthropicClient
        {
            ApiKey = configuration["AnthropicAPIKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "sk-test-key",
        };
    }

    public string Name => "Anthropic";

    public string ModelId { get; }

    public async Task<ProviderTurnResult> ExecuteTurnAsync(
        IReadOnlyList<ContextMessage> preparedMessages,
        IReadOnlyList<ITool> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedMessages);
        ArgumentNullException.ThrowIfNull(tools);

        var (messages, system) = preparedMessages.ForAnthropic();
        var response = await this._client.Messages.Create(new MessageCreateParams
        {
            Model = this.ModelId,
            MaxTokens = 1024,
            Messages = messages,
            System = system,
            Tools = tools.Select(tool => (ToolUnion)ToAnthropicTool(tool)).ToList(),
        });

        return new ProviderTurnResult(
            response.InputTokens(),
            response.ResponseSegments(),
            response.ToolUseSegments()
                .Select(call => new ProviderToolCall(call.ToolCallId, call.ToolName, call.Content))
                .ToList());
    }

    private static Tool ToAnthropicTool(ITool tool)
    {
        var properties = new Dictionary<string, JsonElement>();
        var required = Array.Empty<string>();

        if (tool.ParametersSchema is { } schema)
        {
            var root = schema.RootElement;

            if (root.TryGetProperty("properties", out var propsElement))
            {
                foreach (var prop in propsElement.EnumerateObject())
                {
                    properties[prop.Name] = prop.Value.Clone();
                }
            }

            if (root.TryGetProperty("required", out var reqElement))
            {
                required = reqElement.EnumerateArray()
                    .Select(static element => element.GetString())
                    .OfType<string>()
                    .ToArray();
            }
        }

        return new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new InputSchema
            {
                Properties = properties,
                Required = required,
            },
        };
    }
}
