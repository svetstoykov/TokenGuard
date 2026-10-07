extern alias sample;

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Serilog;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using sample::Codexplorer.Diagnostics;

namespace Codexplorer.Automation.Tests;

/// <summary>Controls the provider response while intercepting the summarizer's actual overload.</summary>
internal sealed class SampleChatClient : ChatClient
{
    private readonly Func<CancellationToken, Task<ChatCompletion>> _response;

    /// <summary>Initializes a new instance of the <see cref="SampleChatClient" /> class.</summary>
    /// <param name="response">The response operation.</param>
    public SampleChatClient(Func<CancellationToken, Task<ChatCompletion>> response) => this._response = response;
    /// <summary>Gets the signal that the provider attempt has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public override async Task<ClientResult<ChatCompletion>> CompleteChatAsync(
        IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.Started.TrySetResult();
        return ClientResult.FromValue(await this._response(cancellationToken), new SamplePipelineResponse());
    }

    /// <summary>Deserializes a deterministic response with optional tool use and reported usage.</summary>
    /// <param name="text">The assistant answer text.</param>
    /// <param name="toolCall">Whether the response requests a tool.</param>
    /// <returns>The provider response.</returns>
    public static ChatCompletion Completion(string text = "answer", bool toolCall = false)
    {
        var message = toolCall
            ? "{\"role\":\"assistant\",\"content\":\"working\",\"tool_calls\":[{\"id\":\"tool_1\",\"type\":\"function\","
                + "\"function\":{\"name\":\"inspect\",\"arguments\":\"{}\"}}]}"
            : System.Text.Json.JsonSerializer.Serialize(new { role = "assistant", content = text });
        var json = "{\"id\":\"test\",\"object\":\"chat.completion\",\"created\":1700000000,\"model\":\"test\",\"choices\":[{\"index\":0,"
            + "\"finish_reason\":\"" + (toolCall ? "tool_calls" : "stop") + "\",\"message\":" + message
            + "}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":7,\"total_tokens\":107}}";
        return ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(json))!;
    }
}
