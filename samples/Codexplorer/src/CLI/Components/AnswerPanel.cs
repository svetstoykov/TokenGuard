using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.CLI.Components;

internal static class AnswerPanel
{
    public static IRenderable Render(string answer, CodexplorerTheme theme)
    {
        IRenderable content = new Text(answer);

        return new Panel(content)
        {
            Header = new PanelHeader("Assistant Reply"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        }.BorderColor(theme.Success);
    }
}
