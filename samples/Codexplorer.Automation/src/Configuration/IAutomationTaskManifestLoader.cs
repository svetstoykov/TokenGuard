namespace Codexplorer.Automation.Configuration;

internal interface IAutomationTaskManifestLoader
{
    IReadOnlyList<AutomationTaskDefinition> LoadTasks();
}
