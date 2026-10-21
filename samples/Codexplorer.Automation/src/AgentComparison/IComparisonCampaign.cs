namespace Codexplorer.Automation.AgentComparison;

/// <summary>Defines synthetic verification and guarded future orchestration.</summary>
internal interface IComparisonCampaign
{
    /// <summary>Asynchronously verifies real runtime processes against a local fake.</summary>
    /// <param name="root">The source worktree.</param>
    /// <param name="output">The experiment output directory.</param>
    /// <param name="writer">The redacted status output.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing zero only when synthetic verification succeeds.</returns>
    Task<int> VerifyOfflineAsync(string root, string output, TextWriter writer, CancellationToken cancellationToken);

    /// <summary>Asynchronously starts an explicitly requested future campaign after fresh validation.</summary>
    /// <param name="planPath">The immutable launch plan.</param>
    /// <param name="writer">The redacted status output.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the final campaign status.</returns>
    Task<int> RunAsync(string planPath, TextWriter writer, CancellationToken cancellationToken);
}
