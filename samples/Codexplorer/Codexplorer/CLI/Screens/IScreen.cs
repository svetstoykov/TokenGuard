using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.CLI.Screens;

internal interface IScreen
{
    Task<ScreenTransition> RunAsync(CancellationToken ct);
}

internal abstract record ScreenTransition;

internal sealed record GoToMenu : ScreenTransition;

internal sealed record GoToQuery(WorkspaceModel Workspace) : ScreenTransition;

internal sealed record GoToLogViewer(string LogFilePath) : ScreenTransition;

internal sealed record ExitApp : ScreenTransition;
