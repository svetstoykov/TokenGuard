namespace TokenGuard.Core.Strategies;

/// <summary>
/// Represents the newest message tail kept verbatim during LLM summarization.
/// </summary>
/// <param name="FirstIndex">The first message index included in the protected tail.</param>
/// <param name="TokenCount">The token count for all messages in the protected tail.</param>
internal sealed record ProtectedTail(int FirstIndex, int TokenCount);
