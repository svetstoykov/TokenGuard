namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents the fixed per-arm invocation and sibling-directory templates.</summary>
/// <param name="Control">The control invocation template relative to the evidence root.</param>
/// <param name="Treatment">The treatment invocation template relative to the evidence root.</param>
/// <param name="OpenCode">The OpenCode invocation template relative to the evidence root.</param>
/// <param name="Checkout">The repository directory beneath each invocation.</param>
/// <param name="Artifacts">The external artifact sibling directory.</param>
/// <param name="State">The isolated runtime home/config/state sibling directory.</param>
internal sealed record ComparisonIsolationPlan(string Control, string Treatment, string OpenCode, string Checkout, string Artifacts, string State)
{
    /// <summary>Gets the fixed directory layout implemented by the comparison launcher.</summary>
    internal static ComparisonIsolationPlan Default { get; } = new("control/{executionId}/{sessionId}",
        "treatment/{executionId}/{sessionId}", "pilots/opencode/{executionId}/{sessionId}", "checkout", "artifacts", "state");
}
