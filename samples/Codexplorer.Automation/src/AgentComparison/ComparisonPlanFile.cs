using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Reads launch plans and verifies their canonical digest.</summary>
internal static class ComparisonPlanFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Loads a launch plan and rejects a mismatched canonical digest.</summary>
    /// <param name="planPath">The launch-plan file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the validated launch plan.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="planPath" /> is empty.</exception>
    /// <exception cref="InvalidDataException">The plan is empty or its canonical digest is invalid.</exception>
    public static async Task<ComparisonLaunchPlan> LoadAsync(string planPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        await using var stream = File.OpenRead(planPath);
        var plan = await JsonSerializer.DeserializeAsync<ComparisonLaunchPlan>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The comparison launch plan is empty.");
        if (!ValidateDigest(plan))
        {
            throw new InvalidDataException("The comparison launch-plan digest does not match its contents.");
        }

        return plan;
    }

    /// <summary>Checks the canonical launch-plan digest.</summary>
    /// <param name="plan">The launch plan.</param>
    /// <returns><see langword="true" /> when the digest matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan" /> is <see langword="null" />.</exception>
    public static bool ValidateDigest(ComparisonLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return string.Equals(plan.PlanDigest, ComputeDigest(plan), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Computes the canonical digest without the digest field itself.</summary>
    /// <param name="plan">The launch plan.</param>
    /// <returns>The lowercase SHA-256 digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan" /> is <see langword="null" />.</exception>
    internal static string ComputeDigest(ComparisonLaunchPlan plan)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            plan.SchemaVersion,
            plan.CampaignId,
            plan.RepositoryRoot,
            plan.BaseRef,
            plan.BaseSha,
            plan.SourceSha,
            plan.SourceDirtyDigest,
            plan.SourceDirty,
            plan.BaseDirty,
            plan.Isolation,
            plan.PreparedAt,
            plan.OutputRoot,
            plan.Settings,
            plan.Bundles,
            plan.Inputs,
            plan.Sessions,
            plan.Blockers,
            plan.LiveExecutionStarted
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
