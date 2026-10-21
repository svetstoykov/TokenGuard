namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Resolves the existing provider credential without exposing it in diagnostic metadata.</summary>
internal interface IComparisonCredentialResolver
{
    /// <summary>Resolves the credential value and its source for in-memory child setup.</summary>
    /// <returns>The credential value and safe provenance metadata.</returns>
    ResolvedComparisonCredential Resolve();

    /// <summary>Reads only the credential presence and source for readiness reporting.</summary>
    /// <returns>Safe metadata that contains no credential value.</returns>
    CredentialPresence Inspect();
}
