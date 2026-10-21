using System.Text.Json.Serialization;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents a resolved provider credential and its non-secret provenance.</summary>
/// <param name="ApiKey">The credential value kept in memory for child setup.</param>
/// <param name="Source">The source identifier without its value.</param>
/// <param name="Present">Whether a nonempty credential was found.</param>
internal sealed record ResolvedComparisonCredential([property: JsonIgnore] string? ApiKey, string Source, bool Present)
{
    /// <inheritdoc />
    public override string ToString() => $"Credential present={this.Present}; source={this.Source}";
}

/// <summary>Represents the safe credential presence metadata written into readiness reports.</summary>
/// <param name="Present">Whether a nonempty credential was found.</param>
/// <param name="Source">The source identifier without its value.</param>
internal sealed record CredentialPresence(bool Present, string Source);
