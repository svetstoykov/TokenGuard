using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.CLI.Components;

internal static class StatusLineComponent
{
    public static IRenderable Render(int turnIndex, int tokensInContext, int contextWindowTokens, CodexplorerTheme theme)
    {
        var percentUsed = contextWindowTokens == 0
            ? 0.0
            : (double)tokensInContext / contextWindowTokens * 100.0;

        Table table = new();
        table.HideHeaders();
        table.Border(TableBorder.None);
        table.AddColumn(string.Empty);
        table.AddRow(
            new Text(
                $"model turn {turnIndex + 1} | tokens in context {tokensInContext:N0} | budget used {percentUsed:F1}%",
                theme.TitleStyle));

        return table;
    }
}
