namespace TokenGuard.Benchmark.Retention;

/// <summary>
/// Defines synthetic conversation noise themes used by retention benchmarks.
/// </summary>
/// <remarks>
/// These values select the template bank that fills turns not dedicated to planted facts. Keeping the style explicit on
/// the profile makes scenarios reproducible and lets benchmark runs compare retention behavior across different types of
/// realistic conversational clutter.
/// </remarks>
public enum NoiseStyle
{
    /// <summary>
    /// Indicates noise modeled after engineering design and implementation discussion.
    /// </summary>
    TechnicalDiscussion,

    /// <summary>
    /// Indicates noise modeled after planning and coordination meetings.
    /// </summary>
    PlanningMeeting,

    /// <summary>
    /// Indicates noise modeled after active debugging and incident investigation.
    /// </summary>
    DebugSession,

    /// <summary>
    /// Indicates noise modeled after discovery and requirement-collection sessions.
    /// </summary>
    RequirementsGathering,
}
