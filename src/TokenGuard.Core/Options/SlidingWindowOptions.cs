using TokenGuard.Core.Models;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Options;

/// <summary>
/// Configures how <see cref="SlidingWindowStrategy"/> protects recent history and masks older tool results.
/// </summary>
/// <remarks>
/// <see cref="SlidingWindowOptions"/> exposes the trade-off between preserving the newest conversational turns exactly
/// and reclaiming space from older tool-heavy history. The values are validated at construction time so strategy
/// instances fail fast when supplied with an impossible or ambiguous policy.
/// </remarks>
/// <param name="windowSize">The maximum number of newest messages to preserve unchanged.</param>
/// <param name="protectedWindowFraction">The fraction of <see cref="ContextBudget.AvailableTokens"/> reserved for the protected newest-message window.</param>
/// <param name="placeholderFormat">The composite format string used when replacing older tool results, where <c>{0}</c> is the tool name and <c>{1}</c> is the tool call identifier.</param>
public readonly record struct SlidingWindowOptions(
    int windowSize = 10,
    double protectedWindowFraction = 0.40,
    string placeholderFormat = "[Tool result cleared — {0}, {1}]")
{
    /// <summary>
    /// Gets the default configuration used by <see cref="SlidingWindowStrategy"/>.
    /// </summary>
    public static SlidingWindowOptions Default => new(10, 0.40, "[Tool result cleared — {0}, {1}]");

    /// <summary>
    /// Gets the maximum number of newest messages to preserve unchanged.
    /// </summary>
    public int WindowSize { get; } = ValidateWindowSize(windowSize);

    /// <summary>
    /// Gets the fraction of available tokens reserved for the protected newest-message window.
    /// </summary>
    public double ProtectedWindowFraction { get; } = ValidateProtectedWindowFraction(protectedWindowFraction);

    /// <summary>
    /// Gets the format string used to replace older tool results with placeholders.
    /// </summary>
    public string PlaceholderFormat { get; } = ValidatePlaceholderFormat(placeholderFormat);

    private static int ValidateWindowSize(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "WindowSize must be greater than zero.");
        }

        return value;
    }

    private static string ValidatePlaceholderFormat(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("PlaceholderFormat cannot be null or whitespace.", nameof(placeholderFormat));
        }

        return value;
    }

    private static double ValidateProtectedWindowFraction(double value)
    {
        if (value <= 0.0 || value >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(protectedWindowFraction), "ProtectedWindowFraction must be in the range (0.0, 1.0).");
        }

        return value;
    }
}
