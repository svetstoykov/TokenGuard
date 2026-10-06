using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents a compaction strategy whose result is produced by a test-supplied delegate.
/// </summary>
/// <param name="compact">Produces the result from the messages and available tokens passed to the strategy.</param>
internal sealed class StubCompactionStrategy(Func<IReadOnlyList<ContextMessage>, int, CompactionResult> compact) : ICompactionStrategy
{
    /// <summary>
    ///     The strategy name reported by the results this stub builds.
    /// </summary>
    public const string Name = "StubStrategy";

    /// <summary>
    ///     Creates a strategy that returns its input unchanged.
    /// </summary>
    /// <param name="summarizationError">The summarization failure to report, or <see langword="null" /> to report none.</param>
    /// <returns>The pass-through strategy.</returns>
    public static StubCompactionStrategy Unchanged(Exception? summarizationError = null) =>
        new((messages, _) => Result(messages, messages, 0, summarizationError));

    /// <summary>
    ///     Creates a strategy that keeps only the newest messages.
    /// </summary>
    /// <param name="keep">The number of newest messages to keep.</param>
    /// <returns>The strategy.</returns>
    public static StubCompactionStrategy KeepNewest(int keep) =>
        new((messages, _) => Result(messages, messages.Skip(Math.Max(0, messages.Count - keep)).ToArray(), Math.Max(0, messages.Count - keep)));

    /// <inheritdoc />
    public Task<CompactionResult> CompactAsync(IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default) =>
        Task.FromResult(compact(messages, availableTokens));

    private static CompactionResult Result(
        IReadOnlyList<ContextMessage> before, IReadOnlyList<ContextMessage> after, int affected, Exception? summarizationError = null) =>
        new(after, before.Sum(m => m.TokenCount ?? 0), after.Sum(m => m.TokenCount ?? 0), affected, Name, summarizationError);
}
