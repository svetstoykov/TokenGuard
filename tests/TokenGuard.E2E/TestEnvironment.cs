using System.Text;

namespace TokenGuard.E2E;

internal static class TestEnvironment
{
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
