using System.Globalization;
using System.Reflection;
using Codexplorer.Configuration;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace Codexplorer.ConsoleShell.Screens;

internal sealed class ConfigScreen : IScreen
{
    private const string MainMenuOption = "Main menu";
    private const string QuitOption = "Quit";

    private static readonly HashSet<string> SecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApiKey",
        "Secret",
        "ClientSecret",
        "Password",
        "Token",
        "AccessToken",
        "RefreshToken"
    };

    private readonly CodexplorerOptions _options;
    private readonly IAnsiConsole _console;

    public ConfigScreen(IOptions<CodexplorerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this._options = options.Value;
        this._console = AnsiConsole.Console;
    }

    public async Task<ScreenTransition> RunAsync(CancellationToken ct)
    {
        this._console.Clear();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Setting")
            .AddColumn("Value");

        foreach (var entry in BuildEntries(this._options))
        {
            table.AddRow(
                Markup.Escape(entry.Path),
                Markup.Escape(entry.Value));
        }

        table.AddRow(
            "Environment.OPENROUTER_API_KEY",
            Markup.Escape(GetEnvironmentPresence("OPENROUTER_API_KEY")));

        this._console.Write(table);
        this._console.WriteLine();

        var choice = this._console.Prompt(
            new SelectionPrompt<string>()
                .Title("What next?")
                .PageSize(10)
                .AddChoices(MainMenuOption, QuitOption));

        await Task.CompletedTask.ConfigureAwait(false);

        return choice switch
        {
            MainMenuOption => new GoToMenu(),
            QuitOption => new ExitApp(),
            _ => throw new InvalidOperationException($"Unsupported config action '{choice}'.")
        };
    }

    private static IReadOnlyList<ConfigurationEntry> BuildEntries(CodexplorerOptions options)
    {
        List<ConfigurationEntry> entries = [];

        foreach (var property in typeof(CodexplorerOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            AddEntries(property.Name, property.GetValue(options), entries);
        }

        return entries;
    }

    private static void AddEntries(string path, object? value, ICollection<ConfigurationEntry> entries)
    {
        if (value is null)
        {
            entries.Add(new ConfigurationEntry(path, "<null>"));
            return;
        }

        var valueType = value.GetType();

        if (IsScalar(valueType))
        {
            entries.Add(new ConfigurationEntry(path, FormatValue(path, value)));
            return;
        }

        foreach (var property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            AddEntries($"{path}.{property.Name}", property.GetValue(value), entries);
        }
    }

    private static string FormatValue(string path, object value)
    {
        if (IsSecretPath(path))
        {
            return "****";
        }

        return value switch
        {
            bool booleanValue => booleanValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "<null>"
        };
    }

    private static string GetEnvironmentPresence(string environmentVariableName)
    {
        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariableName))
            ? "<missing>"
            : "<present>";
    }

    private static bool IsScalar(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType.IsPrimitive
            || underlyingType.IsEnum
            || underlyingType == typeof(string)
            || underlyingType == typeof(decimal)
            || underlyingType == typeof(DateTime)
            || underlyingType == typeof(DateTimeOffset)
            || underlyingType == typeof(TimeSpan)
            || underlyingType == typeof(Guid);
    }

    private static bool IsSecretPath(string path)
    {
        var lastSeparatorIndex = path.LastIndexOf('.');
        var propertyName = lastSeparatorIndex >= 0 ? path[(lastSeparatorIndex + 1)..] : path;
        return SecretPropertyNames.Contains(propertyName);
    }

    private sealed record ConfigurationEntry(string Path, string Value);
}
