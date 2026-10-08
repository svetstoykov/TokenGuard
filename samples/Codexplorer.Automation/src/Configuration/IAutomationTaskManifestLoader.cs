namespace Codexplorer.Automation.Configuration;

/// <summary>Defines the immutable manifest-loading boundary.</summary>
internal interface IAutomationTaskManifestLoader
{
    /// <summary>Loads the validated immutable task list.</summary>
    /// <returns>The tasks from the cached manifest snapshot.</returns>
    IReadOnlyList<AutomationTaskDefinition> LoadTasks();

    /// <summary>Loads and hashes the manifest once.</summary>
    /// <returns>The cached manifest tasks and provenance.</returns>
    AutomationManifestSnapshot LoadSnapshot();
}
