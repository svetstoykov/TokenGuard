using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.AgentComparison.Preparation;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class LaunchMatrixTests
{
    /// <summary>Verifies create sessions async uses all six frozen tasks across three arms and rotates order.</summary>
    [Fact]
    public async Task CreateSessionsAsync_UsesAllSixFrozenTasksAcrossThreeArmsAndRotatesOrder()
    {
        var repositoryRoot = FindRepositoryRoot();
        var promptRoot = Path.Combine(Path.GetTempPath(), $"tokenguard-prompts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(promptRoot);
        try
        {
            var manifests = new List<(LaunchPreparation.Tier Tier, AutomationManifestSnapshot Snapshot)>
            {
                Load(repositoryRoot, new("100k", "long-sessions-100k.json", 80, 8, 50_000)),
                Load(repositoryRoot, new("200k", "long-sessions-200k.json", 140, 10, 65_000)),
                Load(repositoryRoot, new("300k", "long-sessions-300k.json", 200, 12, 100_000))
            };
            var inputs = new List<FileDigest>();

            var sessions = await LaunchPreparation.CreateSessionsAsync(repositoryRoot, promptRoot, manifests, 2, inputs, CancellationToken.None);

            sessions.Should().HaveCount(36);
            sessions.GroupBy(session => (session.Tier, session.Task.TaskId, session.Repetition))
                .Should().OnlyContain(group => group.Select(session => session.Arm).Distinct().Count() == 3);
            sessions.Select(session => session.Arm).Take(3).Should().Equal("codexplorer-control", "codexplorer-treatment", "opencode");
            sessions.Skip(3).Take(3).Select(session => session.Arm).Should().Equal("codexplorer-treatment", "opencode", "codexplorer-control");
            sessions.Where(session => session.Arm == "codexplorer-control").Should().OnlyContain(session => session.ContextWindowTokens == 1_000_000);
            sessions.Where(session => session.Arm == "codexplorer-treatment").Select(session => session.ContextWindowTokens)
                .Should().Equal(
                    Enumerable.Repeat<int?>(50_000, 2)
                        .Concat(Enumerable.Repeat<int?>(65_000, 2))
                        .Concat(Enumerable.Repeat<int?>(100_000, 2))
                        .Concat(Enumerable.Repeat<int?>(50_000, 2))
                        .Concat(Enumerable.Repeat<int?>(65_000, 2))
                        .Concat(Enumerable.Repeat<int?>(100_000, 2)));
            sessions.Where(session => session.Tier == "100k")
                .Should().OnlyContain(session => session.MaxAgentCalls == 80 && session.WrapUpCalls == 8);
            sessions.Where(session => session.Tier == "200k")
                .Should().OnlyContain(session => session.MaxAgentCalls == 140 && session.WrapUpCalls == 10);
            sessions.Where(session => session.Tier == "300k")
                .Should().OnlyContain(session => session.MaxAgentCalls == 200 && session.WrapUpCalls == 12);
            sessions.GroupBy(session => session.Task.TaskId).Should().OnlyContain(
                group => group.Select(session => session.Task.Checks).Distinct().Count() == 1);
            sessions.Where(session => session.Arm == "opencode").Should().OnlyContain(
                session => session.Prompt.Contains("${ARTIFACTS_DIR}/evidence.md", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(promptRoot, recursive: true);
        }
    }

    private static (LaunchPreparation.Tier Tier, AutomationManifestSnapshot Snapshot) Load(
        string repositoryRoot,
        LaunchPreparation.Tier tier)
    {
        var path = Path.Combine(repositoryRoot, "samples", "Codexplorer.Automation", "src", "tasks", tier.ManifestName);
        var options = new CodexplorerAutomationOptions { ManifestPath = path };
        var loader = new AutomationTaskManifestLoader(
            Options.Create(options),
            NullLogger<AutomationTaskManifestLoader>.Instance,
            new AnswerScorer());
        return (tier, loader.LoadSnapshot());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root for frozen manifests.");
    }
}
