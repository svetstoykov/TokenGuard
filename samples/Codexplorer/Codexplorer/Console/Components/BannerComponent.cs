using Codexplorer.Sessions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.ConsoleRendering.Components;

internal static class BannerComponent
{
    public static IRenderable Render(SessionStartedEvent evt, CodexplorerTheme theme)
    {
        Grid grid = new();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(new Text("Model", theme.MutedStyle), new Text(evt.ModelName));
        grid.AddRow(new Text("Workspace", theme.MutedStyle), new Text(evt.Workspace.OwnerRepo));
        grid.AddRow(new Text("Path", theme.MutedStyle), new Text(evt.Workspace.LocalPath));
        grid.AddRow(
            new Text("Budget", theme.MutedStyle),
            new Text(
                $"{evt.Budget.ContextWindowTokens} tokens | soft {evt.Budget.SoftThresholdRatio:P0} | hard {evt.Budget.HardThresholdRatio:P0}"));
        grid.AddEmptyRow();
        grid.AddRow(new Text("Query", theme.MutedStyle), new Text(evt.UserQuery));

        return new Panel(grid)
        {
            Header = new PanelHeader("Codexplorer Live Run"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        }.BorderColor(theme.Title);
    }
}
