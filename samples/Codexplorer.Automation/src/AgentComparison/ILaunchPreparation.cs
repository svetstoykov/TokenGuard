namespace Codexplorer.Automation.AgentComparison;

/// <summary>Prepares and validates an immutable comparison launch package.</summary>
internal interface ILaunchPreparation
{
    /// <summary>Loads the source corpus, resolves the session matrix and saves its evidence.</summary>
    /// <param name="repositoryRoot">The source repository root.</param>
    /// <param name="outputDirectory">The comparison evidence root.</param>
    /// <param name="repetitions">The positive number of full matrix repetitions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the prepared launch plan.
    /// </returns>
    Task<ComparisonLaunchPlan> PrepareAsync(
        string repositoryRoot,
        string outputDirectory,
        int repetitions,
        CancellationToken cancellationToken);

    /// <summary>Rechecks plan provenance, retained bundles and readiness requirements.</summary>
    /// <param name="planPath">The launch-plan path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the redacted readiness result.
    /// </returns>
    Task<ComparisonReadiness> PreflightAsync(string planPath, CancellationToken cancellationToken);
}
