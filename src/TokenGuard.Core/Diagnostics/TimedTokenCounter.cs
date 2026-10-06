using System.Diagnostics;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Represents a token counter that can measure the time its inner counter spends counting.
/// </summary>
/// <remarks>
///     One instance is shared by a <see cref="ConversationContext" /> and the strategy built for it, so the time
///     measured for one prepare call covers counting done by both. Timing is off until <see cref="StartTiming" /> is
///     called, and while it is off a call adds nothing to the inner counter's cost.
/// </remarks>
/// <param name="inner">The counter that does the counting. Cannot be <see langword="null" />.</param>
internal sealed class TimedTokenCounter(ITokenCounter inner) : ITokenCounter
{
    private bool _timing;
    private long _elapsedTicks;

    /// <inheritdoc />
    public int Count(ContextMessage contextMessage)
    {
        if (!this._timing)
            return inner.Count(contextMessage);

        var start = Stopwatch.GetTimestamp();
        var count = inner.Count(contextMessage);
        this._elapsedTicks += Stopwatch.GetTimestamp() - start;
        return count;
    }

    /// <inheritdoc />
    public int Count(IEnumerable<ContextMessage> messages)
    {
        if (!this._timing)
            return inner.Count(messages);

        var start = Stopwatch.GetTimestamp();
        var count = inner.Count(messages);
        this._elapsedTicks += Stopwatch.GetTimestamp() - start;
        return count;
    }

    /// <summary>
    ///     Starts accumulating counting time from zero.
    /// </summary>
    internal void StartTiming()
    {
        this._elapsedTicks = 0;
        this._timing = true;
    }

    /// <summary>
    ///     Stops accumulating counting time.
    /// </summary>
    /// <returns>The time spent counting since <see cref="StartTiming" /> was called.</returns>
    internal TimeSpan StopTiming()
    {
        this._timing = false;
        return Stopwatch.GetElapsedTime(0, this._elapsedTicks);
    }
}
