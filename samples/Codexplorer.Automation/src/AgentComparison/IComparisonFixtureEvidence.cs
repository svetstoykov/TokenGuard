namespace Codexplorer.Automation.AgentComparison;

/// <summary>Defines hash-bound receipts for native-process offline checks.</summary>
internal interface IComparisonFixtureEvidence
{
    /// <summary>Asynchronously writes a receipt after the fixture's independent assertions pass.</summary>
    /// <param name="scenarioRoot">The synthetic session directory.</param>
    /// <param name="runtimeExecutable">The verified executable and dependency directory.</param>
    /// <param name="runtime">The runtime identity.</param>
    /// <param name="scenario">The verified behavior.</param>
    /// <returns>A task representing persistence.</returns>
    Task WriteAsync(string scenarioRoot, string runtimeExecutable, string runtime, string scenario);

    /// <summary>Finds successful retained receipts matching the current complete runtime bundles.</summary>
    /// <param name="root">The experiment evidence root.</param>
    /// <param name="bundles">The current executable locations.</param>
    /// <returns>The verified receipt and capture paths, or an empty list if any required scenario is missing.</returns>
    IReadOnlyList<string> FindVerified(string root, IReadOnlyList<RuntimeBundle> bundles);
}
