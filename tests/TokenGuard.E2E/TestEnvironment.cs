using System.Text;

namespace TokenGuard.E2E;

/// <summary>
/// Resolves test-only environment variables from process state or the local E2E dotenv file.
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// Returns required environment variable value, falling back to `tests/TokenGuard.E2E/.env.local`.
    /// </summary>
    /// <param name="variableName">Name of the environment variable required by the current test.</param>
    public static string RequireVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = TryLoadVariableFromDotEnvLocal(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{variableName} is not set. Configure it in the process environment or in tests/TokenGuard.E2E/.env.local.");
        }

        Environment.SetEnvironmentVariable(variableName, value);
        return value;
    }

    /// <summary>
    /// Reads a variable from the local dotenv file when the process environment does not provide it.
    /// </summary>
    /// <param name="variableName">Name of the variable to search for.</param>
    private static string? TryLoadVariableFromDotEnvLocal(string variableName)
    {
        var envFilePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".env.local"));
        if (!File.Exists(envFilePath))
            return null;

        foreach (var rawLine in File.ReadLines(envFilePath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            if (!string.Equals(line[..separatorIndex].Trim(), variableName, StringComparison.Ordinal))
                continue;

            var value = line[(separatorIndex + 1)..].Trim().Trim('"').Trim('\'');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
