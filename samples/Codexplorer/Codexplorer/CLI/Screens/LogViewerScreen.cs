using Spectre.Console;

namespace Codexplorer.CLI.Screens;

internal sealed class LogViewerScreen(string logFilePath) : IScreen
{
    private readonly IAnsiConsole _console = AnsiConsole.Console;

    public async Task<ScreenTransition> RunAsync(CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        if (!File.Exists(logFilePath))
        {
            this._console.Clear();
            this._console.MarkupLine($"[red]Log file not found: {Markup.Escape(logFilePath)}[/]");
            PromptContinue(this._console, "Press [green]Enter[/] to return to the main menu.");
            return new GoToMenu();
        }

        var lines = await File.ReadAllLinesAsync(logFilePath, ct).ConfigureAwait(false);
        var pageSize = GetPageSize();

        if (lines.Length == 0)
        {
            lines = ["(empty log file)"];
        }

        for (var pageStart = 0; pageStart < lines.Length; pageStart += pageSize)
        {
            this._console.Clear();

            var currentPage = (pageStart / pageSize) + 1;
            var totalPages = (int)Math.Ceiling(lines.Length / (double)pageSize);
            var pageText = string.Join(Environment.NewLine, lines.Skip(pageStart).Take(pageSize));

            this._console.Write(new Rule($"Session Log — {Path.GetFileName(logFilePath)}"));
            this._console.WriteLine();
            this._console.Write(new Panel(new Text(pageText)).Expand());
            this._console.WriteLine();
            this._console.MarkupLine($"[grey]Page {currentPage}/{totalPages} — press Space or Enter for next page, or q to quit.[/]");

            if (!ShouldAdvancePage())
            {
                return new GoToMenu();
            }
        }

        return new GoToMenu();
    }

    private static int GetPageSize()
    {
        try
        {
            return Math.Max(10, System.Console.WindowHeight - 6);
        }
        catch (IOException)
        {
            return 14;
        }
    }

    private static bool ShouldAdvancePage()
    {
        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);

            if (key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter)
            {
                return true;
            }

            if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                return false;
            }
        }
    }

    private static void PromptContinue(IAnsiConsole console, string prompt)
    {
        console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }
}
