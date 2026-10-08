namespace Codexplorer.Automation.Configuration;

/// <summary>Represents the immutable tasks and provenance of one manifest load.</summary>
/// <param name="Tasks">The immutable validated task list.</param>
/// <param name="Path">The resolved file path or inline marker.</param>
/// <param name="Sha256">The hash of the loaded manifest bytes.</param>
/// <param name="Provenance">The file or inline provenance.</param>
internal sealed record AutomationManifestSnapshot(
    IReadOnlyList<AutomationTaskDefinition> Tasks, string Path, string Sha256, string Provenance);
