namespace Codexplorer.Automation.Configuration;

internal static class AutomationPathResolver
{
    public static string ResolveFromCurrentDirectory(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var expandedPath = ExpandEnvironmentVariables(path);
        return Path.GetFullPath(expandedPath, Directory.GetCurrentDirectory());
    }

    private static string ExpandEnvironmentVariables(string path)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            path,
            @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}",
            static match =>
            {
                var variableName = match.Groups["name"].Value;
                var variableValue = Environment.GetEnvironmentVariable(variableName);
                return !string.IsNullOrWhiteSpace(variableValue)
                    ? variableValue
                    : throw new InvalidOperationException(
                        $"Environment variable '{variableName}' is not set for automation path '{match.Value}'.");
            });
    }
}
