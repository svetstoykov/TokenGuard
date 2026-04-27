namespace Codexplorer.Automation.Protocol;

internal sealed record AutomationWorkspace(
    string Name,
    string OwnerRepo,
    string LocalPath,
    DateTime ClonedAt,
    long SizeBytes);
