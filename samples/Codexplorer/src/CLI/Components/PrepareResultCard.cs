using Codexplorer.Sessions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.CLI.Components;

internal static class PrepareResultCard
{
    public static IRenderable Render(PreparedContextEvent evt, CodexplorerTheme theme)
    {
        Table table = new();
        table.Border(TableBorder.Rounded);
        table.BorderColor(theme.Accent);
        table.AddColumn(new TableColumn(new Text("Metric", theme.MutedStyle)));
        table.AddColumn(new TableColumn(new Text("Value", theme.AccentStyle)));
        table.AddRow(
            new Text("Token delta", theme.MutedStyle),
            new Text($"{evt.TokensBeforeCompaction:N0} -> {evt.TokensAfterCompaction:N0}", theme.AccentStyle));
        table.AddRow(
            new Text("Messages compacted", theme.MutedStyle),
            new Text(evt.MessagesCompacted.ToString("N0"), theme.AccentStyle));
        table.AddRow(
            new Text("Outcome", theme.MutedStyle),
            new Text(evt.Outcome, theme.AccentStyle));

        if (!string.IsNullOrWhiteSpace(evt.DegradationReason))
        {
            table.AddRow(
                new Text("Reason", theme.MutedStyle),
                new Text(evt.DegradationReason, theme.WarnStyle));
        }

        return new Panel(table)
        {
            Header = new PanelHeader($"PrepareResult (turn {evt.TurnIndex + 1})"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(0, 0, 0, 0)
        }.BorderColor(theme.Accent);
    }
}
