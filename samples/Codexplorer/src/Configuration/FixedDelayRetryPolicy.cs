using System.ClientModel.Primitives;
using Serilog;

namespace Codexplorer.Configuration;

/// <summary>Retries a failed model request after a fixed wait, up to a fixed number of times.</summary>
/// <remarks>
///     A provider rate limit (HTTP 429) usually clears within seconds, so a fixed wait lets a long session continue
///     past it. The policy applies to every failure the SDK classifies as retriable, and logs each retry.
/// </remarks>
internal sealed class FixedDelayRetryPolicy : ClientRetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _delay;

    /// <summary>Initializes a new instance of the <see cref="FixedDelayRetryPolicy" /> class.</summary>
    /// <param name="maxRetries">The number of retries allowed after the first attempt.</param>
    /// <param name="delay">The wait before each retry.</param>
    internal FixedDelayRetryPolicy(int maxRetries, TimeSpan delay) : base(maxRetries)
    {
        this._maxRetries = maxRetries;
        this._delay = delay;
    }

    /// <inheritdoc />
    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        Log.Warning(
            "Model request attempt {Attempt} failed with HTTP {Status}. Retrying in {DelaySeconds} s (up to {MaxRetries} retries).",
            tryCount, message.Response?.Status, this._delay.TotalSeconds, this._maxRetries);

        return this._delay;
    }
}
