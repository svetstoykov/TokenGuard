namespace TokenGuard.Core.Models;

/// <summary>Represents the provider-corrected tokens assigned to mutually exclusive prepared-message categories.</summary>
/// <remarks>
///     Whole messages are classified in this order: pinned, summarized, masked, containing a tool result, then other.
///     Mixed messages belong entirely to the first matching category. Counts include message framing and the proportional
///     provider correction; rounding is assigned by cumulative category boundaries so the categories sum to the prepared total.
///     When every estimate is zero, the corrected total belongs to the first message's category, or Other for an empty view.
/// </remarks>
public sealed record PreparedTokenBreakdown
{
    /// <summary>Gets the tokens in pinned messages; the default is zero.</summary>
    public int Pinned { get; init; }

    /// <summary>Gets the tokens in unpinned summary messages; the default is zero.</summary>
    public int Summary { get; init; }

    /// <summary>Gets the tokens in unpinned masked messages, including their retained segments; the default is zero.</summary>
    public int Masked { get; init; }

    /// <summary>Gets the tokens in remaining messages containing tool results; the default is zero.</summary>
    public int ToolResults { get; init; }

    /// <summary>Gets the tokens in all remaining messages; the default is zero.</summary>
    public int Other { get; init; }
}
