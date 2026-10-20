using System.Text.Json.Serialization;

namespace Codexplorer.Automation.Configuration;

/// <summary>Represents an opening-instruction or masked-tool-result probe.</summary>
internal sealed record AutomationProbeDefinition
{
    /// <summary>Gets maskedResult for a tool-result probe, or null for opening-instruction retention.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }

    /// <summary>Gets the question asked at wrap-up for a masked-result probe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Question { get; init; }

    /// <summary>Gets the accepted literal answers for a masked-result probe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AnyOf { get; init; }

    /// <summary>Gets the reference code.</summary>
    public string? Canary { get; init; }

    /// <summary>Gets the required compaction kind, or null for any kind.</summary>
    public string? Requires { get; init; }
}
