using Codexplorer.Automation.Configuration;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Represents a reviewed text and its independent gold verdict.</summary>
internal sealed record LabelledScorerCase
{
    /// <summary>Gets the unique display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the review rationale.</summary>
    public required string Why { get; init; }

    /// <summary>Gets the construction or recorded provenance.</summary>
    public required string Source { get; init; }

    /// <summary>Gets the declaration evaluated directly against this text.</summary>
    public required AutomationCheckDefinition Check { get; init; }

    /// <summary>Gets the inline text, including empty text, or null for a file.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the filename below Texts, or null for inline text.</summary>
    public string? TextFile { get; init; }

    /// <summary>Gets the independently reviewed verdict.</summary>
    public required Verdict Expected { get; init; }

    /// <summary>Represents the independent expected outcome.</summary>
    internal sealed record Verdict
    {
        /// <summary>Gets whether the text should pass.</summary>
        public required bool Passed { get; init; }

        /// <summary>Gets the expected reason, or null for a pass.</summary>
        public required string? Reason { get; init; }
    }
}
