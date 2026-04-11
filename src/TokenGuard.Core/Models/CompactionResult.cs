using TokenGuard.Core.Contexts;

namespace TokenGuard.Core.Models;

/// <summary>
/// Represents the outcome of a compaction strategy invocation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompactionResult"/> carries both the transformed message sequence and the metrics needed to understand
/// what changed during a single compaction pass. It allows callers such as <see cref="ConversationContext"/> to keep
/// using the compacted messages while also preserving observability for diagnostics and future notification pipelines.
/// </para>
/// <para>
/// Implementations should populate <see cref="TokensBefore"/>, <see cref="TokensAfter"/>,
/// <see cref="MessagesAffected"/>, <see cref="StrategyName"/>, and <see cref="WasApplied"/> so downstream consumers can
/// distinguish a no-op evaluation from a masking or reduction pass without having to diff the message lists.
/// </para>
/// </remarks>
/// <param name="Messages">The ordered messages produced by the compaction strategy.</param>
/// <param name="TokensBefore">The token count across the input messages before compaction was applied.</param>
/// <param name="TokensAfter">The token count across <paramref name="Messages"/> after compaction was applied.</param>
/// <param name="MessagesAffected">The number of messages whose compaction state changed during this invocation.</param>
/// <param name="StrategyName">The strategy identifier reported by the compaction implementation.</param>
/// <param name="WasApplied">Indicates whether the strategy changed the history during this invocation.</param>
public sealed record CompactionResult(
    IReadOnlyList<ContextMessage> Messages,
    int TokensBefore,
    int TokensAfter,
    int MessagesAffected,
    string StrategyName,
    bool WasApplied);
