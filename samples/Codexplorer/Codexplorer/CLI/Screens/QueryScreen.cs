using Codexplorer.Agent;
using Codexplorer.CLI.Components;
using Codexplorer.Workspace;
using Spectre.Console;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.CLI.Screens;

internal sealed class QueryScreen : IScreen
{
    private readonly WorkspaceModel _workspace;
    private readonly IExplorerAgent _explorerAgent;
    private readonly CancellationCoordinator _cancellationCoordinator;
    private readonly IAnsiConsole _console;

    public QueryScreen(
        WorkspaceModel workspace,
        IExplorerAgent explorerAgent,
        CancellationCoordinator cancellationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(explorerAgent);
        ArgumentNullException.ThrowIfNull(cancellationCoordinator);

        this._workspace = workspace;
        this._explorerAgent = explorerAgent;
        this._cancellationCoordinator = cancellationCoordinator;
        this._console = AnsiConsole.Console;
    }

    public async Task<ScreenTransition> RunAsync(CancellationToken ct)
    {
        this._console.Clear();
        using var sessionCancellationSource = this._cancellationCoordinator.BeginAgentRun();
        await using var session = this._explorerAgent.StartSession(this._workspace);

        while (!ct.IsCancellationRequested &&
               !this._cancellationCoordinator.AppCancellationToken.IsCancellationRequested &&
               !sessionCancellationSource.IsCancellationRequested)
        {
            this._console.WriteLine();
            this._console.MarkupLine("[grey]Ask another question about this repo. Press Ctrl+C to end the current session, or submit an empty message to return to the main menu.[/]");
            var userQuery = NavigationPrompts.PromptTextOrBack(this._console, "Question", "the main menu");

            if (userQuery is null)
            {
                return new GoToMenu();
            }

            var result = await session.SubmitAsync(userQuery, sessionCancellationSource.Token).ConfigureAwait(false);

            if (result is AgentReplyReceived)
            {
                continue;
            }

            switch (result)
            {
                case AgentExchangeMaxTurnsReached maxTurnsReached:
                    this._console.Write(DegradationNotice.RenderWarning(
                        "Message Turn Limit Reached",
                        maxTurnsReached.PartialText is null
                            ? "The assistant did not produce a reply before hitting the configured per-message turn limit."
                            : "The assistant hit the configured per-message turn limit before producing a reply. Review the session log for the partial trace.",
                        session.LogFilePath,
                        CodexplorerTheme.Default));
                    this._console.WriteLine();
                    continue;

                case AgentExchangeCancelled:
                    this._console.MarkupLine("[yellow]Session cancelled. Returning to the main menu.[/]");
                    await Task.Delay(400, CancellationToken.None).ConfigureAwait(false);
                    return new GoToMenu();

                case AgentExchangeDegraded degraded:
                    this._console.Write(DegradationNotice.RenderWarning(
                        "Session Degraded",
                        degraded.Reason,
                        session.LogFilePath,
                        CodexplorerTheme.Default));
                    this._console.WriteLine();
                    PromptContinue(this._console, "Press [green]Enter[/] to return to the main menu.");
                    return new GoToMenu();

                case AgentExchangeFailed failed:
                    this._console.Write(DegradationNotice.RenderError(
                        "Session Failed",
                        $"{failed.Exception.GetType().Name}: {failed.Exception.Message}",
                        session.LogFilePath,
                        CodexplorerTheme.Default));
                    this._console.WriteLine();
                    PromptContinue(this._console, "Press [green]Enter[/] to return to the main menu.");
                    return new GoToMenu();

                default:
                    throw new InvalidOperationException($"Unsupported session result '{result.GetType().Name}'.");
            }
        }

        return new GoToMenu();
    }

    private static void PromptContinue(IAnsiConsole console, string prompt)
    {
        console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }
}
