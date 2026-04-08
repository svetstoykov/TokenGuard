namespace SemanticFold.Strategies;

/// <summary>
/// Configuration for <see cref="SlidingWindowStrategy"/>.
/// </summary>
/// <param name="windowSize">Number of newest messages to preserve unchanged.</param>
/// <param name="protectedWindowFraction">
/// Fraction of <see cref="ContextBudget.AvailableTokens"/> allowed for the protected newest-message window.
/// </param>
/// <param name="placeholderFormat">
/// Format string used to replace older tool results. {0} is tool name and {1} is tool call id.
/// </param>
public readonly record struct SlidingWindowOptions(
    int windowSize = 10,
    double protectedWindowFraction = 0.40,
    string placeholderFormat = "[Tool result cleared — {0}, {1}]")
{
    /// <summary>
    /// Gets the default sliding-window options.
    /// </summary>
    public static SlidingWindowOptions Default => new(10, 0.40, "[Tool result cleared — {0}, {1}]");

    /// <summary>
    /// Gets the number of newest messages to preserve unchanged.
    /// </summary>
    public int WindowSize { get; } = ValidateWindowSize(windowSize);

    /// <summary>
    /// Gets the fraction of available tokens allowed for the protected newest-message window.
    /// </summary>
    public double ProtectedWindowFraction { get; } = ValidateProtectedWindowFraction(protectedWindowFraction);

    /// <summary>
    /// Gets the format string used to replace older tool results.
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
