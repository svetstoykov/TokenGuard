using Codexplorer.CLI.Screens;
using Codexplorer.Configuration;
using Codexplorer.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace Codexplorer.CLI;

internal sealed class MainMenu
{
    private const string CloneNewRepoOption = "Clone a new repo";
    private const string QueryExistingRepoOption = "Query an existing repo";
    private const string ViewPastSessionLogsOption = "View past session logs";
    private const string ShowCurrentConfigurationOption = "Show current configuration";
    private const string QuitOption = "Quit";

    private readonly IServiceProvider _services;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly CodexplorerOptions _options;
    private readonly CancellationCoordinator _cancellationCoordinator;
    private readonly IAnsiConsole _console;

    public MainMenu(
        IServiceProvider services,
        IWorkspaceManager workspaceManager,
        IOptions<CodexplorerOptions> options,
        CancellationCoordinator cancellationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cancellationCoordinator);

        this._services = services;
        this._workspaceManager = workspaceManager;
        this._options = options.Value;
        this._cancellationCoordinator = cancellationCoordinator;
        this._console = AnsiConsole.Console;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        ScreenTransition transition = new GoToMenu();

        while (transition is not ExitApp)
        {
            if (ct.IsCancellationRequested || this._cancellationCoordinator.AppCancellationToken.IsCancellationRequested)
            {
                return 0;
            }

            transition = transition switch
            {
                GoToMenu => await this.RunMainMenuAsync(ct).ConfigureAwait(false),
                GoToQuery goToQuery => await this.CreateScreen<QueryScreen>(goToQuery.Workspace).RunAsync(ct).ConfigureAwait(false),
                GoToLogViewer goToLogViewer => await this.CreateScreen<LogViewerScreen>(goToLogViewer.LogFilePath).RunAsync(ct).ConfigureAwait(false),
                ExitApp exitApp => exitApp,
                _ => throw new InvalidOperationException($"Unsupported screen transition '{transition.GetType().Name}'.")
            };
        }

        return 0;
    }

    private async Task<ScreenTransition> RunMainMenuAsync(CancellationToken ct)
    {
        this._console.Clear();

        var choice = this._console.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Codexplorer[/] — pick an action")
                .PageSize(10)
                .AddChoices(
                    CloneNewRepoOption,
                    QueryExistingRepoOption,
                    ViewPastSessionLogsOption,
                    ShowCurrentConfigurationOption,
                    QuitOption));

        return choice switch
        {
            CloneNewRepoOption => await this.CreateScreen<CloneScreen>().RunAsync(ct).ConfigureAwait(false),
            QueryExistingRepoOption => await this.SelectExistingWorkspaceAsync().ConfigureAwait(false),
            ViewPastSessionLogsOption => await this.SelectLogFileAsync().ConfigureAwait(false),
            ShowCurrentConfigurationOption => await this.CreateScreen<ConfigScreen>().RunAsync(ct).ConfigureAwait(false),
            QuitOption => new ExitApp(),
            _ => throw new InvalidOperationException($"Unsupported menu choice '{choice}'.")
        };
    }

    private async Task<ScreenTransition> SelectExistingWorkspaceAsync()
    {
        var workspaces = this._workspaceManager.ListExisting();

        if (workspaces.Count == 0)
        {
            this._console.Clear();
            this._console.MarkupLine("[yellow]No repos cloned yet. Pick 'Clone a new repo' to get started.[/]");
            PromptReturnToMenu(this._console);
            return new GoToMenu();
        }

        var selectedWorkspace = NavigationPrompts.PromptSelectionOrBack(
            this._console,
            "Pick a repo",
            workspaces,
            static workspace => $"{Markup.Escape(workspace.OwnerRepo)} [grey]({Markup.Escape(workspace.LocalPath)})[/]",
            "Back to main menu");

        await Task.CompletedTask.ConfigureAwait(false);
        return selectedWorkspace is null ? new GoToMenu() : new GoToQuery(selectedWorkspace);
    }

    private async Task<ScreenTransition> SelectLogFileAsync()
    {
        var loggingOptions = this._options.Logging
            ?? throw new InvalidOperationException("Codexplorer logging options are not configured.");
        var sessionLogsDirectory = loggingOptions.SessionLogsDirectory
            ?? throw new InvalidOperationException("Codexplorer session logs directory is not configured.");
        var absoluteLogDirectory = Path.GetFullPath(sessionLogsDirectory);

        if (!Directory.Exists(absoluteLogDirectory))
        {
            this._console.Clear();
            this._console.MarkupLine("[yellow]No session logs exist yet.[/]");
            PromptReturnToMenu(this._console);
            return new GoToMenu();
        }

        var logFiles = Directory.EnumerateFiles(absoluteLogDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (logFiles.Length == 0)
        {
            this._console.Clear();
            this._console.MarkupLine("[yellow]No session logs exist yet.[/]");
            PromptReturnToMenu(this._console);
            return new GoToMenu();
        }

        var selectedLogFile = NavigationPrompts.PromptSelectionOrBack(
            this._console,
            "Pick a session log",
            logFiles,
            static path => Markup.Escape(Path.GetFileName(path)),
            "Back to main menu");

        await Task.CompletedTask.ConfigureAwait(false);
        return selectedLogFile is null ? new GoToMenu() : new GoToLogViewer(selectedLogFile);
    }

    private TScreen CreateScreen<TScreen>(params object[] arguments)
        where TScreen : notnull
    {
        return ActivatorUtilities.CreateInstance<TScreen>(this._services, arguments);
    }

    private static void PromptReturnToMenu(IAnsiConsole console)
    {
        console.Prompt(
            new TextPrompt<string>("Press [green]Enter[/] to return to the main menu.")
                .AllowEmpty());
    }
}
