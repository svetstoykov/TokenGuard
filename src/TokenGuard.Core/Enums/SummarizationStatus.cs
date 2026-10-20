namespace TokenGuard.Core.Enums;

/// <summary>Defines the summarization decision made during one preparation.</summary>
public enum SummarizationStatus
{
    /// <summary>No summary was needed or possible under the current policy and budget.</summary>
    NotNeeded,

    /// <summary>A saved summary was reused.</summary>
    CheckpointReused,

    /// <summary>A new summary was accepted.</summary>
    NewSummary,

    /// <summary>The attempt threw or produced a summary that exceeded the budget.</summary>
    Failed,

    /// <summary>An identical history and budget previously failed, so the attempt was skipped.</summary>
    SkippedPreviousFailure,
}
