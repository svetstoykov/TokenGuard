namespace Codexplorer.Sessions;

/// <summary>
///     Represents the complete model response recorded for one model call.
/// </summary>
/// <param name="Text">The complete assistant text, or an empty string when the model returned only tool calls.</param>
/// <param name="ToolCalls">The tool calls the model issued, with their full argument payloads.</param>
/// <param name="FinishReason">The reason the provider gave for ending the response.</param>
/// <param name="InputTokens">The provider-reported input tokens, or <see langword="null" /> when not reported.</param>
/// <param name="OutputTokens">The provider-reported output tokens, or <see langword="null" /> when not reported.</param>
/// <param name="TotalTokens">The provider-reported total tokens, or <see langword="null" /> when not reported.</param>
internal sealed record CapturedResponse(
    string Text, IReadOnlyList<SessionToolCall> ToolCalls, string FinishReason, int? InputTokens, int? OutputTokens, int? TotalTokens);
