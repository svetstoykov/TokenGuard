using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents an observed top-level request normalization.</summary>
/// <param name="Field">The JSON field name.</param>
/// <param name="Submitted">The submitted value, or null when absent.</param>
/// <param name="Effective">The forwarded value, or null when removed.</param>
internal sealed record RequestNormalizationField(string Field, JsonElement? Submitted, JsonElement? Effective);
