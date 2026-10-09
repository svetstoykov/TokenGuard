using Codexplorer.Automation.Reporting;
using Codexplorer.Automation.Scoring;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Codexplorer.Automation.Configuration;

/// <summary>Validates runner options and the separately loaded immutable task definitions.</summary>
internal sealed class CodexplorerAutomationOptionsValidator : IValidateOptions<CodexplorerAutomationOptions>
{
    private readonly IConfiguration _configuration;

    /// <summary>Initializes a new instance of the <see cref="CodexplorerAutomationOptionsValidator" /> class.</summary>
    /// <param name="configuration">The configuration used to resolve effective credentials.</param>
    public CodexplorerAutomationOptionsValidator(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this._configuration = configuration;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CodexplorerAutomationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CodexplorerExecutablePath))
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:CodexplorerExecutablePath' is required.");
        }
        else if (!Path.IsPathRooted(options.CodexplorerExecutablePath))
        {
            failures.Add(
                $"Configuration field '{CodexplorerAutomationOptions.SectionName}:CodexplorerExecutablePath' must be an absolute path.");
        }
        else
        {
            if (!File.Exists(options.CodexplorerExecutablePath))
            {
                failures.Add(
                    $"Configured Codexplorer executable path '{options.CodexplorerExecutablePath}' does not exist.");
            }
        }

        if (options.Arm is not ("treatment" or "control"))
        {
            failures.Add("Arm must be treatment or control.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            failures.Add("OutputDirectory is required.");
        }

        if (options.ControlContextWindowTokens <= 0)
        {
            failures.Add("ControlContextWindowTokens must be positive.");
        }

        ValidateBudgetProfile(options.TurnBudgets.Small, $"{CodexplorerAutomationOptions.SectionName}:TurnBudgets:Small", failures);
        ValidateBudgetProfile(options.TurnBudgets.Medium, $"{CodexplorerAutomationOptions.SectionName}:TurnBudgets:Medium", failures);
        ValidateBudgetProfile(options.TurnBudgets.Large, $"{CodexplorerAutomationOptions.SectionName}:TurnBudgets:Large", failures);

        var helperAiOptions = options.HelperAi ?? new AutomationHelperAiOptions();
        if (string.IsNullOrWhiteSpace(helperAiOptions.Endpoint)
            || !Uri.TryCreate(helperAiOptions.Endpoint, UriKind.Absolute, out _))
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:HelperAi:Endpoint' must be a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(helperAiOptions.ModelName))
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:HelperAi:ModelName' is required.");
        }

        if (helperAiOptions.MaxOutputTokens <= 0)
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:HelperAi:MaxOutputTokens' must be greater than zero.");
        }

        if (helperAiOptions.Temperature < 0.0 || helperAiOptions.Temperature > 2.0)
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:HelperAi:Temperature' must be between 0.0 and 2.0.");
        }

        var effectiveApiKey = HelperAiCredentials.Resolve(this._configuration, helperAiOptions);
        if (string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            failures.Add(
                $"Configuration field '{CodexplorerAutomationOptions.SectionName}:HelperAi:ApiKey' "
                + "or environment variable 'OPENROUTER_API_KEY' is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Validates tasks from a single immutable manifest load.</summary>
    /// <param name="configuredTasks">The loaded task definitions.</param>
    /// <param name="manifestPath">The configured path, or null for inline tasks.</param>
    /// <param name="failures">The destination for validation failures.</param>
    /// <param name="scorer">The shared evaluator for anti-restatement checks.</param>
    internal static void ValidateTasks(
        IReadOnlyList<AutomationTaskDefinition> configuredTasks, string? manifestPath, List<string> failures, IAnswerScorer scorer)
    {
        if (configuredTasks.Count == 0)
        {
            failures.Add(
                $"Configuration must provide at least one task through '{CodexplorerAutomationOptions.SectionName}:ManifestPath' "
                + $"or '{CodexplorerAutomationOptions.SectionName}:Tasks'.");
        }
        else
        {
            var uniqueTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < configuredTasks.Count; index++)
            {
                var task = configuredTasks[index];
                var taskPrefix = !string.IsNullOrWhiteSpace(manifestPath)
                    ? $"{CodexplorerAutomationOptions.SectionName}:ManifestPath:Tasks:{index}"
                    : $"{CodexplorerAutomationOptions.SectionName}:Tasks:{index}";

                if (task is null)
                {
                    failures.Add($"Configuration field '{taskPrefix}' must be an object.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(task.TaskId))
                {
                    failures.Add($"Configuration field '{taskPrefix}:TaskId' is required.");
                }
                else if (!IsDirectoryName(task.TaskId))
                {
                    failures.Add(
                        $"Configuration field '{taskPrefix}:TaskId' must start with a letter or digit and contain only letters, digits, "
                        + $"'.', '-', and '_'. Value '{task.TaskId}' cannot name a session directory.");
                }
                else if (string.Equals(task.TaskId, JsonRunReportWriter.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"Configuration field '{taskPrefix}:TaskId' must not be '{JsonRunReportWriter.FileName}', the run report file name.");
                }
                else if (!uniqueTaskIds.Add(task.TaskId))
                {
                    failures.Add($"Configuration field '{taskPrefix}:TaskId' must be unique. Duplicate value '{task.TaskId}' was found.");
                }

                if (string.IsNullOrWhiteSpace(task.Title))
                {
                    failures.Add($"Configuration field '{taskPrefix}:Title' is required.");
                }

                ValidateTaskTarget(task, taskPrefix, failures);
                ValidateRepositoryCommit(task, taskPrefix, failures);
                ValidateScoring(task, taskPrefix, failures, scorer);

                if (string.IsNullOrWhiteSpace(task.InitialPrompt))
                {
                    failures.Add($"Configuration field '{taskPrefix}:InitialPrompt' is required.");
                }
                else
                {
                    ValidateTaskPrompt(task, taskPrefix, failures);
                }
            }
        }
    }

    /// <summary>Accumulates declaration errors and rejects checks already satisfied by their own prompt.</summary>
    /// <param name="task">The task containing optional declarations.</param>
    /// <param name="prefix">The indexed configuration prefix.</param>
    /// <param name="failures">The independent semantic errors.</param>
    /// <param name="scorer">The shared deterministic evaluator.</param>
    private static void ValidateScoring(AutomationTaskDefinition task, string prefix, List<string> failures, IAnswerScorer scorer)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < (task.Checks?.Count ?? 0); index++)
        {
            var check = task.Checks![index];
            var field = $"{prefix}:Checks:{index}";
            var context = $"task '{task.TaskId}', check '{check?.Id}'";
            void Error(string property, string message) => failures.Add($"Configuration field '{field}:{property}' ({context}) {message}");
            if (check is null)
            {
                Error("", "must be an object.");
                continue;
            }
            var before = failures.Count;
            if (string.IsNullOrWhiteSpace(check.Id) || !IsDirectoryName(check.Id))
                Error("Id", "must start with an ASCII letter/digit and contain only ASCII letters/digits, '.', '-' or '_'.");
            else if (!ids.Add(check.Id))
                Error("Id", "must be unique ignoring case.");
            if (check.Kind is not ("contains" or "matches"))
                Error("Kind", "must be contains or matches.");
            if (check.Artifact is { } artifact && (string.IsNullOrWhiteSpace(artifact) || artifact.Contains('\\') || artifact.Contains(':')
                || artifact.Split('/').Any(segment => segment is "" or "." or "..")))
                Error("Artifact", "must be a relative forward-slash file path without empty, dot or parent segments, backslashes or colons.");
            if (check.Kind == "contains")
            {
                if (check.AnyOf is null)
                    Error("AnyOf", "is required for contains.");
                if (check.Pattern is not null)
                    Error("Pattern", "must be omitted for contains.");
            }
            if (check.Kind == "matches")
            {
                if (check.AnyOf is not null)
                    Error("AnyOf", "must be omitted for matches.");
                if (string.IsNullOrWhiteSpace(check.Pattern))
                    Error("Pattern", "is required for matches.");
                else
                {
                    try
                    {
                        _ = new Regex(check.Pattern, AnswerScorer.PatternOptions, Regex.InfiniteMatchTimeout);
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    {
                        Error("Pattern", $"is invalid for the scoring engine: {ex.Message}");
                    }
                }
            }
            ValidateValues(check.AnyOf, "AnyOf", Error);
            ValidateValues(check.NoneOf, "NoneOf", Error);
            if (failures.Count == before)
            {
                foreach (var forbidden in check.NoneOf ?? [])
                {
                    if (check.AnyOf?.Any(value => ValueMatcher.IsMatch(TextNormalizer.Normalize(value), TextNormalizer.Normalize(forbidden))) == true)
                        Error("NoneOf", "must not match inside an AnyOf value.");
                }
                if (!string.IsNullOrWhiteSpace(task.InitialPrompt) && scorer.EvaluateCheck(check, task.InitialPrompt, "noAnswer").Passed)
                    Error("", "must not pass on its own InitialPrompt, including artifact-targeted checks.");
            }
        }
        if (task.Probe is { } probe)
        {
            var canary = probe.Canary;
            var field = $"{prefix}:Probe";
            if (canary is null || canary.Length is < 6 or > 32 || !char.IsAsciiLetterOrDigit(canary[0])
                || !char.IsAsciiLetterOrDigit(canary[^1]) || !canary.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                failures.Add($"Configuration field '{field}:Canary' (task '{task.TaskId}') "
                    + "must be 6–32 ASCII letters/digits/hyphens with alphanumeric ends.");
            else if (task.InitialPrompt is { } prompt && ValueMatcher.IsMatch(TextNormalizer.Normalize(prompt), TextNormalizer.Normalize(canary)))
                failures.Add($"Configuration field '{field}:Canary' (task '{task.TaskId}') must not occur in InitialPrompt.");
            if (probe.Requires is not (null or "masked" or "summarized" or "dropped"))
                failures.Add($"Configuration field '{field}:Requires' (task '{task.TaskId}') must be masked, summarized or dropped when supplied.");
        }
    }

    private static void ValidateValues(IReadOnlyList<string>? values, string property, Action<string, string> error)
    {
        if (values is null)
            return;
        if (values.Count == 0)
            error(property, "must contain at least one value.");
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (string.IsNullOrWhiteSpace(value) || value.Contains('`') || value.Contains('*') || TextNormalizer.Normalize(value).Length == 0)
                error($"{property}:{index}", "must be a nonempty literal without backticks or asterisks.");
        }
    }

    /// <summary>Determines whether a task identifier is one portable directory name.</summary>
    /// <remarks>The runner names each session directory after its task, so the identifier must stay one segment inside the run folder.</remarks>
    private static bool IsDirectoryName(string taskId)
    {
        return char.IsAsciiLetterOrDigit(taskId[0])
            && taskId.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static void ValidateTaskPrompt(
        AutomationTaskDefinition task,
        string taskPrefix,
        List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPrefix);
        ArgumentNullException.ThrowIfNull(failures);

        if (!task.InitialPrompt!.Contains("Do not modify", StringComparison.OrdinalIgnoreCase)
            || !task.InitialPrompt.Contains("repository source", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Configuration field '{taskPrefix}:InitialPrompt' must explicitly forbid modifying repository source files.");
        }
    }

    private static void ValidateTaskTarget(
        AutomationTaskDefinition task,
        string taskPrefix,
        List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPrefix);
        ArgumentNullException.ThrowIfNull(failures);

        var hasRepositoryUrl = !string.IsNullOrWhiteSpace(task.RepositoryUrl);

        if (!hasRepositoryUrl)
        {
            failures.Add($"Configuration field '{taskPrefix}:RepositoryUrl' is required.");
            return;
        }

        ValidateRepositoryUrl(task.RepositoryUrl!, $"{taskPrefix}:RepositoryUrl", failures);
    }

    private static void ValidateRepositoryCommit(AutomationTaskDefinition task, string taskPrefix, List<string> failures)
    {
        if (task.RepositoryCommit is null || IsFullCommitSha(task.RepositoryCommit))
        {
            return;
        }

        failures.Add(
            $"Configuration field '{taskPrefix}:RepositoryCommit' of task '{task.TaskId}' must be a full 40-character hexadecimal commit SHA. "
            + $"Value '{task.RepositoryCommit}' is not one.");
    }

    /// <summary>Determines whether a value is a complete SHA-1 commit identifier.</summary>
    /// <remarks>A remote resolves a fetched commit only by its complete identifier, so an abbreviation cannot pin a repository.</remarks>
    private static bool IsFullCommitSha(string value) => value.Length == 40 && value.All(char.IsAsciiHexDigit);

    private static void ValidateRepositoryUrl(string repositoryUrl, string fieldName, List<string> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(failures);

        if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Configuration field '{fieldName}' must target a GitHub HTTPS repository URL.");
            }

            return;
        }

        if (!repositoryUrl.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Configuration field '{fieldName}' must be a GitHub HTTPS or SSH repository URL.");
        }
    }

    private static void ValidateBudgetProfile(TurnBudgetProfile profile, string prefix, List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(failures);

        if (profile.MaxTurns <= 0)
        {
            failures.Add($"Configuration field '{prefix}:MaxTurns' must be greater than zero.");
        }

        if (profile.WrapUpWindow <= 0)
        {
            failures.Add($"Configuration field '{prefix}:WrapUpWindow' must be greater than zero.");
        }

        if (profile.WrapUpWindow >= profile.MaxTurns)
        {
            failures.Add($"Configuration field '{prefix}:WrapUpWindow' must be smaller than '{prefix}:MaxTurns'.");
        }
    }
}
