using Codexplorer.Automation.Configuration;
using Codexplorer.Measurements;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents the allowlisted provenance and effective settings of a manifest run.
/// </summary>
/// <remarks>Only reproducibility settings are serialized; task text and credentials belong outside this contract.</remarks>
internal sealed record RunMetadata
{
    /// <summary>
    ///     Gets the run identifier, which is the name of the run folder.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether sessions wrote <c>capture</c> folders.
    /// </summary>
    public required bool CaptureEnabled { get; init; }

    /// <summary>
    ///     Gets the TokenGuard repository commit recorded at run start.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the repository contained changes at run start.
    /// </summary>
    public required bool RepositoryDirty { get; init; }

    /// <summary>
    ///     Gets the UTC run start time.
    /// </summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>
    ///     Gets the UTC run end time.
    /// </summary>
    public required DateTimeOffset EndedAtUtc { get; init; }

    /// <summary>
    ///     Gets the allowlisted settings returned by the sample.
    /// </summary>
    public required EffectiveSettings EffectiveSettings { get; init; }

    /// <summary>
    ///     Gets the helper model slug.
    /// </summary>
    public required string HelperModel { get; init; }

    /// <summary>
    ///     Gets the helper response token cap.
    /// </summary>
    public required int HelperMaxOutputTokens { get; init; }

    /// <summary>
    ///     Gets the helper sampling temperature.
    /// </summary>
    public required double HelperTemperature { get; init; }

    /// <summary>
    ///     Gets the configured budgets for each task size.
    /// </summary>
    public required AutomationTurnBudgetOptions TurnBudgets { get; init; }

    /// <summary>
    ///     Gets the treatment or control arm.
    /// </summary>
    public required string Arm { get; init; }

    /// <summary>
    ///     Gets the manifest path relative to the run folder, or the explicit inline identifier.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    ///     Gets the SHA-256 hash of the immutable manifest bytes.
    /// </summary>
    public required string ManifestSha256 { get; init; }

    /// <summary>
    ///     Gets the file or inline manifest provenance.
    /// </summary>
    public required string ManifestProvenance { get; init; }
}
