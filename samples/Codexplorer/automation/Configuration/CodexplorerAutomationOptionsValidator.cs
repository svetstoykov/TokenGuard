using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Codexplorer.Automation.Configuration;

internal sealed class CodexplorerAutomationOptionsValidator : IValidateOptions<CodexplorerAutomationOptions>
{
    private readonly IConfiguration _configuration;

    public CodexplorerAutomationOptionsValidator(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this._configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, CodexplorerAutomationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CodexplorerExecutablePath))
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:CodexplorerExecutablePath' is required.");
        }
        else
        {
            var resolvedExecutablePath = AutomationPathResolver.ResolveFromCurrentDirectory(options.CodexplorerExecutablePath);
            if (!File.Exists(resolvedExecutablePath))
            {
                failures.Add(
                    $"Configured Codexplorer executable path '{resolvedExecutablePath}' does not exist.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.CodexplorerWorkingDirectory))
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:CodexplorerWorkingDirectory' is required.");
        }
        else
        {
            var resolvedWorkingDirectory = AutomationPathResolver.ResolveFromCurrentDirectory(options.CodexplorerWorkingDirectory);
            if (!Directory.Exists(resolvedWorkingDirectory))
            {
                failures.Add(
                    $"Configured Codexplorer working directory '{resolvedWorkingDirectory}' does not exist.");
            }
        }

        if (options.Tasks.Count == 0)
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:Tasks' must contain at least one task definition.");
        }
        else
        {
            var uniqueTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < options.Tasks.Count; index++)
            {
                var task = options.Tasks[index];
                var taskPrefix = $"{CodexplorerAutomationOptions.SectionName}:Tasks:{index}";

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

                if (string.IsNullOrWhiteSpace(task.WorkspacePath))
                {
                    failures.Add($"Configuration field '{taskPrefix}:WorkspacePath' is required.");
                }
                else
                {
                    var resolvedWorkspacePath = AutomationPathResolver.ResolveFromCurrentDirectory(task.WorkspacePath);
                    if (!Directory.Exists(resolvedWorkspacePath))
                    {
                        failures.Add($"Configured workspace path '{resolvedWorkspacePath}' does not exist for task '{task.TaskId ?? $"index-{index}"}'.");
                    }
                }

                if (string.IsNullOrWhiteSpace(task.InitialPrompt))
                {
                    failures.Add($"Configuration field '{taskPrefix}:InitialPrompt' is required.");
                }
            }
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

        var effectiveApiKey = this._configuration["OPENROUTER_API_KEY"]
            ?? this._configuration[$"{CodexplorerAutomationOptions.SectionName}:HelperAi:ApiKey"];
        if (string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            failures.Add(
                $"Configuration field '{CodexplorerAutomationOptions.SectionName}:HelperAi:ApiKey' or environment variable 'OPENROUTER_API_KEY' is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
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
