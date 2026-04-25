using Codexplorer.Agent;
using Codexplorer.Workspace;
using Spectre.Console;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.CLI.Screens;

internal sealed class QueryScreen : IScreen
{
    private const string AskAnotherQuestionOption = "Ask another question of this repo";
    private const string DifferentRepoOption = "Different repo";
    private const string MainMenuOption = "Main menu";
    private const string QuitOption = "Quit";

    private readonly WorkspaceModel _workspace;
    private readonly IExplorerAgent _explorerAgent;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly CancellationCoordinator _cancellationCoordinator;
    private readonly IAnsiConsole _console;

    public QueryScreen(
        WorkspaceModel workspace,
        IExplorerAgent explorerAgent,
        IWorkspaceManager workspaceManager,
        CancellationCoordinator cancellationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(explorerAgent);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(cancellationCoordinator);

        this._workspace = workspace;
        this._explorerAgent = explorerAgent;
        this._workspaceManager = workspaceManager;
        this._cancellationCoordinator = cancellationCoordinator;
        this._console = AnsiConsole.Console;
    }

    public async Task<ScreenTransition> RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !this._cancellationCoordinator.AppCancellationToken.IsCancellationRequested)
        {
            this._console.Clear();
            this._console.MarkupLine($"[green]Repo:[/] {Markup.Escape(this._workspace.OwnerRepo)}");
            this._console.MarkupLine($"[grey]{Markup.Escape(this._workspace.LocalPath)}[/]");

            var userQuery = NavigationPrompts.PromptTextOrBack(this._console, "Question", "the main menu");

            if (userQuery is null)
            {
                return new GoToMenu();
            }

            AgentRunResult result;
            var runCancellationSource = this._cancellationCoordinator.BeginAgentRun();

            try
            {
                result = await this._explorerAgent
                    .RunAsync(this._workspace, userQuery, runCancellationSource.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                this._cancellationCoordinator.EndAgentRun(runCancellationSource);
            }

            if (result is AgentCancelled)
            {
                this._console.MarkupLine("[yellow]Run cancelled. Returning to the main menu.[/]");
                await Task.Delay(400, CancellationToken.None).ConfigureAwait(false);
                return new GoToMenu();
            }

            var nextStep = this._console.Prompt(
                new SelectionPrompt<string>()
                    .Title(CreatePostRunTitle(result))
                    .PageSize(10)
                    .AddChoices(
                        AskAnotherQuestionOption,
                        DifferentRepoOption,
                        MainMenuOption,
                        QuitOption));

            switch (nextStep)
            {
                case AskAnotherQuestionOption:
                    continue;
                case DifferentRepoOption:
                    return await this.SelectDifferentRepoAsync().ConfigureAwait(false);
                case MainMenuOption:
                    return new GoToMenu();
                case QuitOption:
                    return new ExitApp();
                default:
                    throw new InvalidOperationException($"Unsupported query follow-up action '{nextStep}'.");
            }
        }

        return new ExitApp();
    }

    private async Task<ScreenTransition> SelectDifferentRepoAsync()
    {
        var otherWorkspaces = this._workspaceManager.ListExisting()
            .Where(workspace => !string.Equals(workspace.OwnerRepo, this._workspace.OwnerRepo, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (otherWorkspaces.Length == 0)
        {
            this._console.MarkupLine("[yellow]No other cloned repos are available yet.[/]");
            PromptContinue(this._console, "Press [green]Enter[/] to return to the main menu.");
            return new GoToMenu();
        }

        var selectedWorkspace = NavigationPrompts.PromptSelectionOrBack(
            this._console,
            "Pick a different repo",
            otherWorkspaces,
            static workspace => $"{Markup.Escape(workspace.OwnerRepo)} [grey]({Markup.Escape(workspace.LocalPath)})[/]",
            "Back to this repo");

        await Task.CompletedTask.ConfigureAwait(false);
        return selectedWorkspace is null ? new GoToQuery(this._workspace) : new GoToQuery(selectedWorkspace);
    }

    private static string CreatePostRunTitle(AgentRunResult result)
    {
        return result switch
        {
            AgentSucceeded => "Run complete. What next?",
            AgentDegraded => "Run degraded. What next?",
            AgentMaxTurnsReached => "Turn limit reached. What next?",
            AgentFailed => "Run failed. What next?",
            _ => "What next?"
        };
    }

    private static void PromptContinue(IAnsiConsole console, string prompt)
    {
        console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }
}
