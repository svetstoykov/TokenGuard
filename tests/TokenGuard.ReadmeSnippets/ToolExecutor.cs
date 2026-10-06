using OpenAI.Chat;

namespace TokenGuard.ReadmeSnippets;

internal sealed class ToolExecutor
{
    public string Execute(ChatToolCall toolCall) => $"Executed {toolCall.FunctionName}.";
}
