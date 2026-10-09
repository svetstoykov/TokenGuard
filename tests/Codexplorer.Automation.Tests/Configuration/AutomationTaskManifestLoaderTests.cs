using Codexplorer.Automation.Scoring;
using Codexplorer.Automation.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies immutable task snapshots and manifest provenance.</summary>
public sealed class AutomationTaskManifestLoaderTests
{
    /// <summary>Verifies later file changes preserve the original task snapshot.</summary>
    [Fact]
    public void LoadTasks_FileChangesAfterFirstLoad_UsesOriginalTasks()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Manifest("original"));
            var loader = new AutomationTaskManifestLoader(
                Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
                NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
            loader.LoadTasks();
            File.WriteAllText(path, Manifest("changed"));

            var tasks = loader.LoadTasks();

            tasks.Single().TaskId.Should().Be("original");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies the manifest hash describes the bytes originally executed.</summary>
    [Fact]
    public void LoadSnapshot_FileChangesAfterFirstLoad_PreservesOriginalByteHash()
    {
        var path = Path.GetTempFileName();
        var original = Encoding.UTF8.GetBytes(Manifest("original"));
        try
        {
            File.WriteAllBytes(path, original);
            var loader = new AutomationTaskManifestLoader(
                Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
                NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
            loader.LoadSnapshot();
            File.WriteAllText(path, Manifest("changed"));

            var snapshot = loader.LoadSnapshot();

            snapshot.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant());
            snapshot.Tasks.Single().TaskId.Should().Be("original");
            snapshot.Provenance.Should().Be("file");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies invalid task definitions are rejected during snapshot loading.</summary>
    [Fact]
    public void LoadSnapshot_InvalidTask_RejectsTheLoadedManifest()
    {
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions
        {
            ManifestPath = null, Tasks = [new AutomationTaskDefinition { TaskId = "invalid" }]
        }), NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());

        var load = () => loader.LoadSnapshot();

        load.Should().Throw<OptionsValidationException>();
    }

    /// <summary>Verifies a task identifier that cannot name one session directory in the run folder is rejected.</summary>
    /// <param name="taskId">The task identifier under test.</param>
    [Theory]
    [InlineData("../shared")]
    [InlineData("group/task")]
    [InlineData("group\\task")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("task one")]
    [InlineData("run-report.json")]
    [InlineData("Run-Report.json")]
    public void LoadSnapshot_TaskIdIsNotOneDirectoryName_RejectsTheLoadedManifest(string taskId)
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions { ManifestPath = null, Tasks = [ValidTask(taskId)] }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());

        var load = () => loader.LoadSnapshot();

        load.Should().Throw<OptionsValidationException>().Which.Failures.Should().ContainSingle().Which.Should().Contain("TaskId");
    }

    /// <summary>Verifies a task identifier made of letters, digits, dots, hyphens, and underscores is accepted.</summary>
    [Fact]
    public void LoadSnapshot_TaskIdIsOneDirectoryName_LoadsTheTask()
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions { ManifestPath = null, Tasks = [ValidTask("batch-small_01.v2")] }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());

        var snapshot = loader.LoadSnapshot();

        snapshot.Tasks.Single().TaskId.Should().Be("batch-small_01.v2");
    }

    /// <summary>Verifies independent declaration failures accumulate with indexed diagnostics.</summary>
    [Fact]
    public void LoadSnapshot_ScoringErrorsAccumulate()
    {
        var task = ValidTask("task") with
        {
            Checks =
            [
                new AutomationCheckDefinition { Id = "bad id", Kind = "Contains", Artifact = "../file", AnyOf = ["**x**", null!] },
                new AutomationCheckDefinition { Id = "second", Kind = "matches", AnyOf = [], Pattern = "(?=x)" },
                null!,
            ],
            Probe = new AutomationProbeDefinition { Canary = "bad", Requires = "Masked" }
        };
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions { ManifestPath = null, Tasks = [task] }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
        var load = () => loader.LoadSnapshot();
        var errors = load.Should().Throw<OptionsValidationException>().Which.Failures.ToArray();
        errors.Should().Contain(error => error.Contains("Tasks:0:Checks:0:Artifact"));
        errors.Should().Contain(error => error.Contains("Tasks:0:Checks:1:Pattern"));
        errors.Should().Contain(error => error.Contains("Tasks:0:Probe:Requires"));
        errors.Length.Should().BeGreaterThan(6);
    }

    /// <summary>Verifies guarded anti-restatement uses the same scorer for both target kinds.</summary>
    /// <param name="artifact">The optional artifact target.</param>
    /// <param name="kind">The check kind.</param>
    [Theory]
    [InlineData(null, "contains")]
    [InlineData("notes.md", "contains")]
    [InlineData(null, "matches")]
    [InlineData("notes.md", "matches")]
    public void LoadSnapshot_RejectsPassingPromptButAllowsGuardedPrompt(string? artifact, string kind)
    {
        var check = new AutomationCheckDefinition
        {
            Id = "fact", Kind = kind, Artifact = artifact, AnyOf = kind == "contains" ? ["source"] : null,
            Pattern = kind == "matches" ? "source" : null,
        };
        var failures = new List<string>();
        var task = ValidTask("task") with { Checks = [check] };
        CodexplorerAutomationOptionsValidator.ValidateTasks([task], null, failures, new AnswerScorer());
        failures.Should().ContainSingle().Which.Should().Contain("InitialPrompt");
        failures.Clear();
        task = task with { Checks = [check with { NoneOf = ["modify"] }] };
        CodexplorerAutomationOptionsValidator.ValidateTasks([task], null, failures, new AnswerScorer());
        failures.Should().BeEmpty();
    }

    /// <summary>Verifies unknown members fail at every manifest nesting level with reader details.</summary>
    /// <param name="json">The invalid manifest.</param>
    [Theory]
    [InlineData("{\"typo\":true,\"tasks\":[]}")]
    [InlineData("{\"tasks\":[{\"typo\":true}]}")]
    [InlineData("{\"tasks\":[{\"checks\":[{\"typo\":true}]}]}")]
    [InlineData("{\"tasks\":[{\"probe\":{\"typo\":true}}]}")]
    public void LoadSnapshot_UnknownMemberNamesPropertyAndPath(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
                NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
            var load = () => loader.LoadSnapshot();
            load.Should().Throw<OptionsValidationException>().Which.Failures.Single().Should().Contain("typo").And.Contain("invalid JSON:");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies illegal relative artifact paths are rejected on every platform.</summary>
    /// <param name="artifact">The invalid path.</param>
    [Theory]
    [InlineData("")]
    [InlineData("/file")]
    [InlineData("a//file")]
    [InlineData("./file")]
    [InlineData("a/../file")]
    [InlineData("a\\file")]
    [InlineData("C:file")]
    public void ValidateTasks_RejectsInvalidArtifactPath(string artifact)
    {
        var failures = new List<string>();
        var task = ValidTask("task") with
        {
            Checks = [new AutomationCheckDefinition { Id = "fact", Kind = "contains", Artifact = artifact, AnyOf = ["expected"] }]
        };
        CodexplorerAutomationOptionsValidator.ValidateTasks([task], null, failures, new AnswerScorer());
        failures.Should().ContainSingle().Which.Should().Contain("Artifact");
    }

    /// <summary>Verifies declaration diagnostics for invalid IDs, fields, values and regex constructs.</summary>
    /// <param name="json">The invalid check declaration.</param>
    /// <param name="field">The field expected in diagnostics.</param>
    [Theory]
    [InlineData("""{}""", "Id")]
    [InlineData("""{"id":"_bad","kind":"contains","anyOf":["fact"]}""", "Id")]
    [InlineData("""{"id":"ébad","kind":"contains","anyOf":["fact"]}""", "Id")]
    [InlineData("""{"id":"fact","kind":"Contains","anyOf":["fact"]}""", "Kind")]
    [InlineData("""{"id":"fact","kind":"contains"}""", "AnyOf")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":[]}""", "AnyOf")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":[null]}""", "AnyOf:0")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":[" "]}""", "AnyOf:0")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":["`fact`"]}""", "AnyOf:0")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":["fact"],"pattern":""}""", "Pattern")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":["fact"],"noneOf":[]}""", "NoneOf")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":["fact"],"noneOf":[null]}""", "NoneOf:0")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":["fact"],"noneOf":["*wrong*"]}""", "NoneOf:0")]
    [InlineData("""{"id":"fact","kind":"contains","anyOf":["CARGO TEST"],"noneOf":["cargo   test"]}""", "NoneOf")]
    [InlineData("""{"id":"fact","kind":"matches"}""", "Pattern")]
    [InlineData("""{"id":"fact","kind":"matches","pattern":" "}""", "Pattern")]
    [InlineData("""{"id":"fact","kind":"matches","pattern":"["}""", "Pattern")]
    [InlineData("""{"id":"fact","kind":"matches","pattern":"(?=fact)"}""", "Pattern")]
    [InlineData("""{"id":"fact","kind":"matches","pattern":"(x)\\1"}""", "Pattern")]
    [InlineData("""{"id":"fact","kind":"matches","pattern":"fact","anyOf":[]}""", "AnyOf")]
    public void ValidateTasks_RejectsInvalidCheckDeclarations(string json, string field)
    {
        var check = System.Text.Json.JsonSerializer.Deserialize<AutomationCheckDefinition>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var failures = new List<string>();
        CodexplorerAutomationOptionsValidator.ValidateTasks([ValidTask("task") with { Checks = [check] }], null, failures, new AnswerScorer());
        failures.Should().Contain(error => error.Contains("Checks:0:" + field));
    }

    /// <summary>Verifies optional nulls, compatible empty checks and supported inline regex options.</summary>
    [Fact]
    public void ValidateTasks_AcceptsOptionalOmissionsAndSupportedRegexOptions()
    {
        var failures = new List<string>();
        var task = ValidTask("task") with
        {
            Checks =
            [
                new AutomationCheckDefinition
                {
                    Id = "run-report.json", Kind = "contains", AnyOf = ["fact"], NoneOf = ["factual"], Artifact = "a folder/file.md",
                },
                new AutomationCheckDefinition { Id = "regex", Kind = "matches", Pattern = "(?-i:FACT)" },
            ],
            Probe = new AutomationProbeDefinition { Canary = "ABC123" },
        };
        CodexplorerAutomationOptionsValidator.ValidateTasks([task], null, failures, new AnswerScorer());
        failures.Should().BeEmpty();
        foreach (var checks in new IReadOnlyList<AutomationCheckDefinition>?[] { null, [] })
        {
            CodexplorerAutomationOptionsValidator.ValidateTasks([task with { Checks = checks, Probe = null }], null, failures, new AnswerScorer());
            failures.Should().BeEmpty();
        }
    }

    /// <summary>Verifies duplicate check IDs and canary restrictions use ordinal matching.</summary>
    /// <param name="canary">The invalid code.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("-ABC123")]
    [InlineData("ABC123-")]
    [InlineData("ABC_123")]
    [InlineData("ABCé123")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    [InlineData("SOURCE")]
    public void ValidateTasks_RejectsInvalidCanariesAndDuplicateChecks(string? canary)
    {
        var check = new AutomationCheckDefinition { Id = "fact", Kind = "contains", AnyOf = ["expected"] };
        var task = ValidTask("task") with
        {
            Checks = [check, check with { Id = "FACT" }], Probe = new AutomationProbeDefinition { Canary = canary },
        };
        var failures = new List<string>();
        CodexplorerAutomationOptionsValidator.ValidateTasks([task], null, failures, new AnswerScorer());
        failures.Should().Contain(error => error.Contains("Checks:1:Id"));
        failures.Should().Contain(error => error.Contains("Probe:Canary"));
    }

    /// <summary>Verifies shipped manifests load with optional declarations and strict reading.</summary>
    /// <param name="name">The shipped manifest.</param>
    [Theory]
    [InlineData("initial-corpus.json")]
    [InlineData("report-baseline.json")]
    public void LoadSnapshot_LoadsShippedManifest(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
            directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "samples", "Codexplorer.Automation", "src", "tasks", name);
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
        var tasks = loader.LoadTasks();
        tasks.Count.Should().Be(name == "initial-corpus.json" ? 20 : 1);
        if (name == "report-baseline.json")
        {
            tasks.Single().Checks.Should().HaveCount(3);
            tasks.Single().Probe.Should().NotBeNull();
        }
    }

    /// <summary>Verifies a repository commit that is not a full hexadecimal SHA is rejected with a message naming the task.</summary>
    /// <param name="repositoryCommit">The repository commit under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("724ae6d")]
    [InlineData("main")]
    [InlineData("724ae6d9a6e84ba4ad7eb3734dfe64250d4bf2fag")]
    [InlineData("z24ae6d9a6e84ba4ad7eb3734dfe64250d4bf2fa")]
    [InlineData(" 724ae6d9a6e84ba4ad7eb3734dfe64250d4bf2f")]
    public void LoadSnapshot_RepositoryCommitIsNotFullSha_RejectsTheLoadedManifestNamingTheTask(string repositoryCommit)
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions
            {
                ManifestPath = null, Tasks = [ValidTask("pinned-task") with { RepositoryCommit = repositoryCommit }]
            }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());

        var load = () => loader.LoadSnapshot();

        load.Should().Throw<OptionsValidationException>().Which.Failures.Should().ContainSingle()
            .Which.Should().Contain("RepositoryCommit").And.Contain("'pinned-task'");
    }

    /// <summary>Verifies a full hexadecimal repository commit in either letter case is accepted.</summary>
    /// <param name="repositoryCommit">The repository commit under test.</param>
    [Theory]
    [InlineData("724ae6d9a6e84ba4ad7eb3734dfe64250d4bf2fa")]
    [InlineData("724AE6D9A6E84BA4AD7EB3734DFE64250D4BF2FA")]
    public void LoadSnapshot_RepositoryCommitIsFullSha_LoadsTheTask(string repositoryCommit)
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions
            {
                ManifestPath = null, Tasks = [ValidTask("pinned-task") with { RepositoryCommit = repositoryCommit }]
            }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());

        var snapshot = loader.LoadSnapshot();

        snapshot.Tasks.Single().RepositoryCommit.Should().Be(repositoryCommit);
    }

    private static AutomationTaskDefinition ValidTask(string taskId) => new()
    {
        TaskId = taskId,
        Title = "Task",
        RepositoryUrl = "https://github.com/example/repo",
        InitialPrompt = "Do not modify repository source files."
    };

    private static string Manifest(string taskId) => $$"""
        {"tasks":[{"taskId":"{{taskId}}","title":"Task","repositoryUrl":"https://github.com/example/repo",
        "initialPrompt":"Do not modify repository source files."}]}
        """;
}
