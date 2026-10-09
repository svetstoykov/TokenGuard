using Codexplorer.Measurements;

namespace Codexplorer.Automation.Scoring;

/// <summary>Represents the available finalization evidence.</summary>
internal sealed record ScoringInput
{
    /// <summary>Gets the protocol-complete answer, or null when unavailable.</summary>
    public string? FinalAnswer { get; init; }

    /// <summary>Gets successfully read texts keyed by requested manifest paths.</summary>
    public required IReadOnlyDictionary<string, string> ArtifactTexts { get; init; }

    /// <summary>Gets a value indicating whether the code appeared before wrap-up.</summary>
    public bool CanaryRepeated { get; init; }

    /// <summary>Gets the cumulative session measurements.</summary>
    public required SessionMeasurements Measurements { get; init; }
}
