namespace Codexplorer.Automation.Configuration;

/// <summary>Represents one deterministic deliverable declaration.</summary>
internal sealed record AutomationCheckDefinition
{
    /// <summary>Gets the check identifier.</summary>
    public string? Id { get; init; }

    /// <summary>Gets the contains or matches kind.</summary>
    public string? Kind { get; init; }

    /// <summary>Gets the relative artifact path, or null for the answer.</summary>
    public string? Artifact { get; init; }

    /// <summary>Gets the unchanged regex pattern.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets the positive alternatives.</summary>
    public IReadOnlyList<string>? AnyOf { get; init; }

    /// <summary>Gets the forbidden values.</summary>
    public IReadOnlyList<string>? NoneOf { get; init; }
}
