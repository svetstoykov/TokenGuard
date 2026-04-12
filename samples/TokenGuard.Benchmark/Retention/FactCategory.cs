namespace TokenGuard.Samples.Benchmark.Retention;

/// <summary>
/// Defines retention benchmark fact placement patterns.
/// </summary>
/// <remarks>
/// The category determines how a benchmark scenario introduces and interprets a planted fact within a synthetic
/// conversation. Later synthesizer and scoring components use these values to model repeated mentions, superseded
/// values, relational dependencies, and buried recall difficulty consistently.
/// </remarks>
public enum FactCategory
{
    /// <summary>
    /// Indicates a fact that appears once and is never repeated.
    /// </summary>
    Anchor,

    /// <summary>
    /// Indicates a fact that appears once and is referenced again later in the conversation.
    /// </summary>
    Reinforced,

    /// <summary>
    /// Indicates a fact whose original planted value is later replaced by newer final value.
    /// </summary>
    Superseded,

    /// <summary>
    /// Indicates a fact whose wording depends on another previously planted fact.
    /// </summary>
    Relational,

    /// <summary>
    /// Indicates a fact embedded in noisy surrounding detail so recall is harder.
    /// </summary>
    Buried,
}
