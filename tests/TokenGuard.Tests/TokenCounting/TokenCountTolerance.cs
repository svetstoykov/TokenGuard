namespace TokenGuard.Tests.TokenCounting;

/// <summary>
///     Represents how far an estimate may lie from a reference token count, as fractions of the reference.
/// </summary>
/// <param name="Under">The largest accepted shortfall, as a fraction of the reference count.</param>
/// <param name="Over">
///     The largest accepted excess, as a fraction of the reference count, or <see langword="null" /> when the fixture states
///     no upper bound for the text kind.
/// </param>
internal readonly record struct TokenCountTolerance(double Under, double? Over)
{
    /// <summary>
    ///     Gets the signed error of an estimate as a fraction of the reference count.
    /// </summary>
    /// <param name="estimate">The estimated token count.</param>
    /// <param name="reference">The reference token count.</param>
    /// <returns>A negative value when the estimate is below the reference; otherwise, zero or a positive value.</returns>
    public static double Error(double estimate, double reference) => (estimate - reference) / reference;

    /// <summary>
    ///     Determines whether an estimate lies within this tolerance of a reference count.
    /// </summary>
    /// <param name="estimate">The estimated token count.</param>
    /// <param name="reference">The reference token count.</param>
    /// <param name="slackTokens">The number of tokens allowed on top of the tolerance, for rounding.</param>
    /// <returns><see langword="true" /> if the estimate is within the tolerance; otherwise, <see langword="false" />.</returns>
    public bool Allows(double estimate, double reference, double slackTokens = 0) =>
        estimate >= (reference * (1 - this.Under)) - slackTokens
        && (this.Over is not { } over || estimate <= (reference * (1 + over)) + slackTokens);

    /// <inheritdoc />
    public override string ToString() => this.Over is { } over ? $"-{this.Under:0%} to +{over:0%}" : $"-{this.Under:0%}, no upper bound stated";
}
