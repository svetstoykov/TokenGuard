using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Configuration;

internal sealed class CodexplorerAutomationOptionsValidator : IValidateOptions<CodexplorerAutomationOptions>
{
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

        if (options.WorkspacePaths.Count == 0)
        {
            failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:WorkspacePaths' must contain at least one workspace path.");
        }
        else
        {
            var uniqueWorkspacePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var workspacePath in options.WorkspacePaths)
            {
                if (string.IsNullOrWhiteSpace(workspacePath))
                {
                    failures.Add($"Configuration field '{CodexplorerAutomationOptions.SectionName}:WorkspacePaths' cannot contain empty values.");
                    continue;
                }

                var resolvedWorkspacePath = AutomationPathResolver.ResolveFromCurrentDirectory(workspacePath);
                if (!Directory.Exists(resolvedWorkspacePath))
                {
                    failures.Add($"Configured workspace path '{resolvedWorkspacePath}' does not exist.");
                    continue;
                }

                if (!uniqueWorkspacePaths.Add(resolvedWorkspacePath))
                {
                    failures.Add($"Configured workspace path '{resolvedWorkspacePath}' is duplicated.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
