using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.CLI.Components;

internal static class DegradationNotice
{
    public static IRenderable RenderWarning(string title, string message, string logFilePath, CodexplorerTheme theme)
    {
        return CreatePanel(title, message, logFilePath, theme.Warn, theme.WarnStyle);
    }

    public static IRenderable RenderError(string title, string message, string logFilePath, CodexplorerTheme theme)
    {
        return CreatePanel(title, message, logFilePath, theme.Error, theme.ErrorStyle);
    }

    private static IRenderable CreatePanel(string title, string message, string logFilePath, Color borderColor, Style titleStyle)
    {
        return new Panel(
            new Rows(
                new Text(message),
                new Text($"See session log: {logFilePath}", new Style(foreground: borderColor))))
        {
            Header = new PanelHeader(title),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        }.BorderColor(borderColor);
    }
}
