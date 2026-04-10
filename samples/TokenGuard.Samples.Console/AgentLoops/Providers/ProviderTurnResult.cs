using TokenGuard.Core.Models.Content;

namespace TokenGuard.Samples.Console.AgentLoops.Providers;

internal sealed record ProviderTurnResult(
    int? InputTokens,
    IReadOnlyList<ContentSegment> ResponseSegments,
    IReadOnlyList<ProviderToolCall> ToolCalls)
{
    public bool HasToolCalls => this.ToolCalls.Count > 0;
}
