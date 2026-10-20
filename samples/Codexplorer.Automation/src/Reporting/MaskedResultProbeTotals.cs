namespace Codexplorer.Automation.Reporting;

/// <summary>Represents counts and the pass rate for masked-result probes only.</summary>
internal sealed record MaskedResultProbeTotals
{
    /// <summary>Gets the declared masked-result probe count.</summary>
    public required int ProbeCount { get; init; }

    /// <summary>Gets the invalid probe count.</summary>
    public required int InvalidProbeCount { get; init; }

    /// <summary>Gets the probes of tasks that performed no masking.</summary>
    public required int NotApplicableProbeCount { get; init; }

    /// <summary>Gets the passed probe count.</summary>
    public required int PassedProbeCount { get; init; }

    /// <summary>Gets the passed fraction among eligible probes, or null when none are eligible.</summary>
    public required double? ProbePassRate { get; init; }

    /// <summary>Gets the probes whose answers contain an accepted value, including invalid probes.</summary>
    public required int CanaryPresentCount { get; init; }

    /// <summary>Gets the probes that fetched an accepted value after the question.</summary>
    public required int RefetchedCount { get; init; }

    /// <summary>Aggregates the masked-result population from task verdicts.</summary>
    /// <param name="probes">All recorded probe verdicts.</param>
    /// <returns>The masked-result counts, with invalid and not-applicable probes outside the pass-rate denominator.</returns>
    public static MaskedResultProbeTotals From(IEnumerable<ProbeResult> probes)
    {
        var population = probes.Where(probe => probe.Kind == "maskedResult").ToArray();
        var valid = population.Count(probe => probe.Status is "passed" or "failed");
        var passed = population.Count(probe => probe.Status == "passed");
        return new MaskedResultProbeTotals
        {
            ProbeCount = population.Length, InvalidProbeCount = population.Count(probe => probe.Status == "invalid"),
            NotApplicableProbeCount = population.Count(probe => probe.Status == "notApplicable"), PassedProbeCount = passed,
            ProbePassRate = valid == 0 ? null : (double)passed / valid,
            CanaryPresentCount = population.Count(probe => probe.CanaryPresent), RefetchedCount = population.Count(probe => probe.Refetched == true)
        };
    }
}
