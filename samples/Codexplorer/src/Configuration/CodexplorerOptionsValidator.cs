using Microsoft.Extensions.Options;

namespace Codexplorer.Configuration;

internal sealed class CodexplorerOptionsValidator : IValidateOptions<CodexplorerOptions>
{
    public ValidateOptionsResult Validate(string? name, CodexplorerOptions? options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Codexplorer configuration is missing.");
        }

        List<string> failures = [];

        if (options.Budget is null)
        {
            failures.Add("Codexplorer:Budget section is required.");
        }
        else
        {
            ValidateNonNegative(options.Budget.ContextWindowTokens, "Codexplorer:Budget:ContextWindowTokens", failures);
            var softThresholdIsValid = ValidateRatio(
                options.Budget.SoftThresholdRatio,
                "Codexplorer:Budget:SoftThresholdRatio",
                failures);
            var hardThresholdIsValid = ValidateRatio(
                options.Budget.HardThresholdRatio,
                "Codexplorer:Budget:HardThresholdRatio",
                failures);
            ValidateNonNegative(options.Budget.WindowSize, "Codexplorer:Budget:WindowSize", failures);

            if (softThresholdIsValid
                && hardThresholdIsValid
                && options.Budget.HardThresholdRatio <= options.Budget.SoftThresholdRatio)
            {
                failures.Add(
                    "Codexplorer:Budget:HardThresholdRatio must be greater than Codexplorer:Budget:SoftThresholdRatio.");
            }
        }

        if (options.Model is null)
        {
            failures.Add("Codexplorer:Model section is required.");
        }
        else
        {
            ValidateRequiredText(options.Model.Name, "Codexplorer:Model:Name", failures);
            ValidateNonNegative(options.Model.MaxOutputTokens, "Codexplorer:Model:MaxOutputTokens", failures);
        }

        if (options.Workspace is null)
        {
            failures.Add("Codexplorer:Workspace section is required.");
        }
        else
        {
            ValidateRequiredText(options.Workspace.RootDirectory, "Codexplorer:Workspace:RootDirectory", failures);
            ValidateNonNegative(options.Workspace.CloneDepth, "Codexplorer:Workspace:CloneDepth", failures);
            ValidateNonNegative(options.Workspace.MaxRepoSizeMB, "Codexplorer:Workspace:MaxRepoSizeMB", failures);
        }

        if (options.Logging is null)
        {
            failures.Add("Codexplorer:Logging section is required.");
        }
        else
        {
            ValidateRequiredText(options.Logging.SessionLogsDirectory, "Codexplorer:Logging:SessionLogsDirectory", failures);
        }

        if (options.Agent is null)
        {
            failures.Add("Codexplorer:Agent section is required.");
        }
        else if (options.Agent.MaxTurns <= 0)
        {
            failures.Add("Codexplorer:Agent:MaxTurns must be greater than 0.");
        }

        if (options.OpenRouter is null)
        {
            failures.Add("Codexplorer:OpenRouter section is required.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateNonNegative(int value, string path, List<string> failures)
    {
        if (value < 0)
        {
            failures.Add($"{path} must be greater than or equal to 0.");
        }
    }

    private static bool ValidateRatio(double value, string path, List<string> failures)
    {
        if (value <= 0.0 || value > 1.0)
        {
            failures.Add($"{path} must be in range (0, 1].");
            return false;
        }

        return true;
    }

    private static void ValidateRequiredText(string? value, string path, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{path} cannot be empty or whitespace.");
        }
    }
}
