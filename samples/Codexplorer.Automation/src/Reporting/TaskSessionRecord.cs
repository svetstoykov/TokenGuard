namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents where one task's session wrote and what its artifacts folder held.
/// </summary>
/// <param name="SessionId">Codexplorer's session identifier, or <see langword="null" /> when the session never opened.</param>
/// <param name="SessionDirectory">
///     The task's session directory relative to the run folder, or <see langword="null" /> when the session never opened.
/// </param>
/// <param name="ArtifactsAtStart">The artifact-relative paths of files present when the session opened.</param>
/// <param name="ArtifactsAtEnd">The files present when the session closed.</param>
internal sealed record TaskSessionRecord(
    string? SessionId, string? SessionDirectory, IReadOnlyList<string> ArtifactsAtStart, IReadOnlyList<ArtifactFileReport> ArtifactsAtEnd);
