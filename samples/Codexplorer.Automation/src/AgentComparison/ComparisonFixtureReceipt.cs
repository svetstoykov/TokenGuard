namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents successful real-process assertions and their retained synthetic evidence.</summary>
/// <param name="SchemaVersion">The receipt schema version.</param>
/// <param name="Synthetic">Whether all model responses were synthetic.</param>
/// <param name="LiveExecutionStarted">Whether public inference started.</param>
/// <param name="Passed">Whether the fixture's behavioral assertions passed.</param>
/// <param name="Runtime">The verified runtime identity.</param>
/// <param name="Scenario">The verified behavior.</param>
/// <param name="BundleDigest">The complete verified runtime-bundle digest.</param>
/// <param name="Evidence">The retained evidence files relative to the scenario directory.</param>
internal sealed record ComparisonFixtureReceipt(int SchemaVersion, bool Synthetic, bool LiveExecutionStarted, bool Passed,
    string Runtime, string Scenario, string BundleDigest, IReadOnlyList<FileDigest> Evidence);
