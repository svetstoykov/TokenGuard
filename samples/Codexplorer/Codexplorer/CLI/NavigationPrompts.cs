using Spectre.Console;

namespace Codexplorer.CLI;

internal static class NavigationPrompts
{
    private const string DefaultBackOptionLabel = "Back";

    internal static string? PromptTextOrBack(IAnsiConsole console, string prompt, string destinationLabel)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationLabel);

        console.MarkupLine($"[grey]Leave blank and press Enter to go back to {Markup.Escape(destinationLabel)}.[/]");
        var value = console.Prompt(
            new TextPrompt<string>(prompt)
                .PromptStyle("green")
                .AllowEmpty());

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static T? PromptSelectionOrBack<T>(
        IAnsiConsole console,
        string title,
        IEnumerable<T> choices,
        Func<T, string> converter,
        string backOptionLabel = DefaultBackOptionLabel)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentException.ThrowIfNullOrWhiteSpace(backOptionLabel);

        var entries = choices
            .Select(choice => SelectionEntry<T>.CreateValue(choice, converter(choice)))
            .Append(SelectionEntry<T>.CreateBack($"[grey]{Markup.Escape(backOptionLabel)}[/]"))
            .ToArray();

        var selectedEntry = console.Prompt(
            new SelectionPrompt<SelectionEntry<T>>()
                .Title(title)
                .PageSize(10)
                .UseConverter(static entry => entry.Label)
                .AddChoices(entries));

        return selectedEntry.IsBack ? null : selectedEntry.Value;
    }

    private sealed record SelectionEntry<T>(string Label, T? Value, bool IsBack)
        where T : class
    {
        internal static SelectionEntry<T> CreateValue(T value, string label)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            return new SelectionEntry<T>(label, value, false);
        }

        internal static SelectionEntry<T> CreateBack(string label)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            return new SelectionEntry<T>(label, null, true);
        }
    }
}
