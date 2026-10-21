namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents raw provider usage with each value preserved as optional.</summary>
/// <param name="InputTokens">The provider-reported input token count.</param>
/// <param name="OutputTokens">The provider-reported output token count.</param>
/// <param name="CachedInputTokens">The cached portion of input tokens.</param>
/// <param name="ReasoningOutputTokens">The reasoning portion of output tokens.</param>
/// <param name="CacheWriteTokens">The provider-reported cache-write token count.</param>
/// <param name="Cost">The provider-reported cost.</param>
/// <param name="Complete">Whether provider usage contains complete input and output counts.</param>
internal sealed record RecordedUsage(
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? ReasoningOutputTokens,
    long? CacheWriteTokens,
    decimal? Cost,
    bool Complete);
