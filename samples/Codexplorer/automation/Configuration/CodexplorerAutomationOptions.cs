namespace Codexplorer.Automation.Configuration;

internal sealed record CodexplorerAutomationOptions
{
    public const string SectionName = "CodexplorerAutomation";

    public string? CodexplorerExecutablePath { get; init; } = "../src/bin/Debug/net10.0/Codexplorer";

    public string? CodexplorerWorkingDirectory { get; init; } = "..";

    public IReadOnlyList<string> WorkspacePaths { get; init; } = [];
}
