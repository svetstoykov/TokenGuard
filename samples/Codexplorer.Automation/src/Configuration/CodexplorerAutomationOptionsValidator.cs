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
    internal static void ValidateTasks(
        IReadOnlyList<AutomationTaskDefinition> configuredTasks, string? manifestPath, List<string> failures)
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
                else if (!uniqueTaskIds.Add(task.TaskId))
                {
                    failures.Add($"Configuration field '{taskPrefix}:TaskId' must be unique. Duplicate value '{task.TaskId}' was found.");
                }

                if (string.IsNullOrWhiteSpace(task.Title))
                {
                    failures.Add($"Configuration field '{taskPrefix}:Title' is required.");
                }

                ValidateTaskTarget(task, taskPrefix, failures);

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
