using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.ConsoleRendering.Components;

internal static class StatusLineComponent
{
    public static IRenderable Render(int turnIndex, int maxTurns, int tokensInContext, int contextWindowTokens, CodexplorerTheme theme)
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
                $"turn {turnIndex + 1} / {maxTurns} | tokens in context {tokensInContext:N0} | budget used {percentUsed:F1}%",
                theme.TitleStyle));

        return table;
    }
}
