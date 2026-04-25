using Codexplorer.Workspace;
using Spectre.Console;

namespace Codexplorer.ConsoleShell.Screens;

internal sealed class CloneScreen : IScreen
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IAnsiConsole _console;

    public CloneScreen(IWorkspaceManager workspaceManager)
    {
        ArgumentNullException.ThrowIfNull(workspaceManager);

        this._workspaceManager = workspaceManager;
        this._console = AnsiConsole.Console;
    }

    public async Task<ScreenTransition> RunAsync(CancellationToken ct)
    {
        this._console.Clear();

        var githubUrl = this._console.Prompt(
            new TextPrompt<string>("GitHub URL")
                .PromptStyle("green")
                .Validate(static value => string.IsNullOrWhiteSpace(value)
                    ? ValidationResult.Error("[red]A GitHub URL is required.[/]")
                    : ValidationResult.Success()));

        try
        {
            var workspace = await this._workspaceManager.CloneAsync(githubUrl.Trim(), ct: ct).ConfigureAwait(false);
            this._console.MarkupLine($"[green]Cloned {Markup.Escape(workspace.OwnerRepo)} into {Markup.Escape(workspace.LocalPath)}.[/]");
            PromptContinue(this._console, "Press [green]Enter[/] to open the query screen.");
            return new GoToQuery(workspace);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or RepositoryTooLargeException)
        {
            this._console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            PromptContinue(this._console, "Press [green]Enter[/] to return to the main menu.");
            return new GoToMenu();
        }
    }

    private static void PromptContinue(IAnsiConsole console, string prompt)
    {
        console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }
}
