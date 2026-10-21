using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.Configuration;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Preparation;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class ComparisonPlanFileTests
{
    /// <summary>Verifies validate digest rejects changed settings.</summary>
    [Fact]
    public void ValidateDigest_RejectsChangedSettings()
    {
        var plan = CreatePlan();
        var prepared = plan with { PlanDigest = ComparisonPlanFile.ComputeDigest(plan) };

        ComparisonPlanFile.ValidateDigest(prepared).Should().BeTrue();
        ComparisonPlanFile.ValidateDigest(prepared with { Settings = prepared.Settings with { MaxOutputTokens = 16_384 } }).Should().BeFalse();
    }

    /// <summary>Verifies sessions equal rejects recomputed plan with changed checks or budgets.</summary>
    [Fact]
    public void SessionsEqual_RejectsRecomputedPlanWithChangedChecksOrBudgets()
    {
        var expected = new ComparisonSessionPlan(
            "100k-task-r1-codexplorer-control",
            "100k",
            1,
            "codexplorer-control",
            new AutomationTaskDefinition
            {
                TaskId = "task",
                InitialPrompt = "Create evidence.md.",
                Checks = [new AutomationCheckDefinition { Id = "evidence", Artifact = "evidence.md", Pattern = "amount: 42" }]
            },
            "Create evidence.md.",
            "source-prompt-digest",
            "adapted-prompt-digest",
            80,
            8,
            1_000_000);

        LaunchPreparation.SessionsEqual(expected, expected).Should().BeTrue();
        LaunchPreparation.SessionsEqual(expected, expected with
        {
            Task = expected.Task with
            {
                Checks = [new AutomationCheckDefinition { Id = "evidence", Artifact = "evidence.md", Pattern = "amount: 99" }]
            }
        }).Should().BeFalse();
        LaunchPreparation.SessionsEqual(expected, expected with { MaxAgentCalls = 200 }).Should().BeFalse();
    }

    private static ComparisonLaunchPlan CreatePlan() => new(
        1,
        "campaign",
        "/workspace",
        "main",
        "base",
        "source",
        "dirty",
        DateTimeOffset.Parse("2026-10-21T00:00:00Z"),
        ".artifacts/reports/opencode-comparison",
        new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32_768, true),
        [],
        [],
        [],
        [],
        string.Empty,
        false);
}
