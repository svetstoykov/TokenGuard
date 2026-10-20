using System.Collections.Frozen;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
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
public readonly record struct SlidingWindowOptions
{
    private readonly FrozenSet<string>? _exemptToolNames;

    /// <summary>
    /// Initializes a default <see cref="SlidingWindowOptions"/> value using library-defined defaults.
    /// </summary>
    /// <remarks>
    /// This parameterless constructor exists because value types are always default-initializable. Routing through
    /// the validating constructor preserves consistent default behavior for both explicit and implicit construction.
    /// </remarks>
    public SlidingWindowOptions()
        : this(
            windowSize: SlidingWindowDefaults.WindowSize,
            protectedWindowFraction: SlidingWindowDefaults.ProtectedWindowFraction,
            placeholderFormat: SlidingWindowDefaults.PlaceholderFormat)
    {
    }

    /// <summary>
    /// Initializes a <see cref="SlidingWindowOptions"/> value with validated masking behavior.
    /// </summary>
    /// <remarks>
    /// The constructor validates the protected window size, token fraction, and placeholder format before the value
    /// participates in record equality. This keeps the record state aligned with the exposed validated properties rather
    /// than capturing duplicate hidden primary-constructor fields.
    /// </remarks>
    /// <param name="windowSize">The minimum number of newest messages to preserve unchanged before the token cap can stop the backward walk.</param>
    /// <param name="protectedWindowFraction">The fraction of <see cref="ContextBudget.MaxTokens"/> reserved for the protected newest-message window.</param>
    /// <param name="placeholderFormat">The composite format string used when replacing older tool results, where <c>{0}</c> is the tool name and <c>{1}</c> is the tool call identifier.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="windowSize"/> is not greater than zero.
    /// -or-
    /// <paramref name="protectedWindowFraction"/> is not a finite number or is outside the range (0.0, 1.0).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="placeholderFormat"/> is <see langword="null"/> or whitespace.
    /// -or-
    /// <paramref name="placeholderFormat"/> is not a valid composite format string, or uses an index other than
    /// <c>{0}</c> and <c>{1}</c>.
    /// </exception>
    public SlidingWindowOptions(
        int windowSize = SlidingWindowDefaults.WindowSize,
        double protectedWindowFraction = SlidingWindowDefaults.ProtectedWindowFraction,
        string placeholderFormat = SlidingWindowDefaults.PlaceholderFormat)
    {
        this.WindowSize = ValidateWindowSize(windowSize, nameof(windowSize));
        this.ProtectedWindowFraction = ValidateProtectedWindowFraction(protectedWindowFraction, nameof(protectedWindowFraction));
        this.PlaceholderFormat = ValidatePlaceholderFormat(placeholderFormat, nameof(placeholderFormat));
    }

    /// <summary>
    /// Gets the default configuration used by <see cref="SlidingWindowStrategy"/>.
    /// </summary>
    public static SlidingWindowOptions Default => new(SlidingWindowDefaults.WindowSize);

    /// <summary>
    /// Gets the minimum number of newest messages to preserve unchanged.
    /// </summary>
    public int WindowSize { get; }

    /// <summary>
    /// Gets the largest fraction of available tokens the protected newest-message window may hold.
    /// </summary>
    /// <remarks>
    /// Nothing is masked while the compactable history fits in this fraction. Above it, the protected window holds
    /// between this bound and one step below it, where a step is one quarter of the bound: the masking boundary stays
    /// in place while the history grows and moves one step at a time. The default is 0.80, which gives a step of 0.20
    /// of the available tokens.
    /// </remarks>
    public double ProtectedWindowFraction { get; }

    /// <summary>
    /// Gets the format string used to replace older tool results with placeholders.
    /// </summary>
    /// <remarks>
    /// A tool result is replaced only when it costs more estimated tokens than the placeholder this format gives for
    /// it, so a longer format leaves more short results as they are.
    /// </remarks>
    public string PlaceholderFormat { get; }

    /// <summary>
    /// Gets the names of the tools whose results masking never replaces. The default is an empty set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tool result is exempt when its <see cref="ToolResultContent.ToolName"/> equals one of these names exactly,
    /// compared ordinally. Use it for tools whose results the agent relies on later, such as a notes or todo-list tool.
    /// The value returned is a set, so a name given more than once is held once.
    /// </para>
    /// <para>
    /// An exempt result stays in the prepared view at its recorded size wherever it falls in the history. The tool result
    /// limit applies when recording exempt tools too. When exempt results
    /// are large, masking reclaims less space, later stages have more to remove, and a prepare can end as
    /// <see cref="PrepareOutcome.CompactionInsufficient"/> where it would otherwise end as
    /// <see cref="PrepareOutcome.Compacted"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The assigned collection is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The assigned collection contains a name that is <see langword="null"/>, empty, or whitespace.</exception>
    public IReadOnlyCollection<string> ExemptToolNames
    {
        get => this.ExemptToolNameSet;
        init => this._exemptToolNames = ValidateExemptToolNames(value, nameof(this.ExemptToolNames));
    }

    private FrozenSet<string> ExemptToolNameSet => this._exemptToolNames ?? FrozenSet<string>.Empty;

    /// <summary>
    /// Determines whether masking leaves the results of the named tool in place.
    /// </summary>
    /// <param name="toolName">The tool name recorded on a tool result.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="toolName"/> is one of <see cref="ExemptToolNames"/>; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    internal bool IsExempt(string toolName) => this.ExemptToolNameSet.Contains(toolName);

    /// <summary>
    /// Gets a value indicating whether this value was created by a constructor.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if a constructor produced this value; <see langword="false"/> for
    /// <c>default(SlidingWindowOptions)</c>, whose <see cref="PlaceholderFormat"/> is <see langword="null"/>.
    /// </value>
    internal bool IsInitialized => this.PlaceholderFormat is not null;

    /// <summary>
    /// Determines whether this value and <paramref name="other"/> configure the same masking policy.
    /// </summary>
    /// <remarks>
    /// The exempt tool names are compared as sets, so their order and repetition when they were set do not matter, and
    /// an empty set equals one that was never set.
    /// </remarks>
    /// <param name="other">The value to compare with this value.</param>
    /// <returns><see langword="true"/> if both values hold the same settings; otherwise, <see langword="false"/>.</returns>
    public bool Equals(SlidingWindowOptions other) =>
        this.WindowSize == other.WindowSize
        && this.ProtectedWindowFraction.Equals(other.ProtectedWindowFraction)
        && string.Equals(this.PlaceholderFormat, other.PlaceholderFormat, StringComparison.Ordinal)
        && this.ExemptToolNameSet.SetEquals(other.ExemptToolNameSet);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(this.WindowSize, this.ProtectedWindowFraction, this.PlaceholderFormat, this.ExemptToolNames.Count);

    private static FrozenSet<string> ValidateExemptToolNames(IReadOnlyCollection<string> value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (value.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("ExemptToolNames cannot contain a null, empty, or whitespace name.", paramName);
        }

        return value.ToFrozenSet(StringComparer.Ordinal);
    }

    private static int ValidateWindowSize(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "WindowSize must be greater than zero.");
        }

        return value;
    }

    private static string ValidatePlaceholderFormat(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("PlaceholderFormat cannot be null or whitespace.", paramName);
        }

        try
        {
            _ = string.Format(value, "tool_name", "tool_call_id");
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "PlaceholderFormat must be a composite format string that uses only {0} (the tool name) and {1} (the tool call identifier).",
                paramName, exception);
        }

        return value;
    }

    private static double ValidateProtectedWindowFraction(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, "ProtectedWindowFraction must be a finite number.");
        }

        if (value <= 0.0 || value >= 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, "ProtectedWindowFraction must be in the range (0.0, 1.0).");
        }

        return value;
    }
}
