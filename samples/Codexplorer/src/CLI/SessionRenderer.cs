using Codexplorer.CLI.Components;
using Codexplorer.Sessions;
using Spectre.Console;

namespace Codexplorer.CLI;

/// <summary>
/// Renders a live console feed from the ordered session event stream.
/// </summary>
/// <remarks>
/// The renderer uses direct Spectre writes instead of a live region so redirected stdout stays readable and stable. It
/// summarizes high-volume events on the console while leaving the markdown session log as the detailed record.
/// </remarks>
internal sealed class SessionRenderer
{
    private readonly CodexplorerTheme _theme;
    private readonly IAnsiConsole _console;
    private readonly bool _enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionRenderer"/> class.
    /// </summary>
    public SessionRenderer()
        : this(AnsiConsole.Console, CodexplorerTheme.Default)
    {
    }

    internal SessionRenderer(IAnsiConsole console, CodexplorerTheme theme, bool enabled = true)
    {
        this._console = console ?? throw new ArgumentNullException(nameof(console));
        this._theme = theme ?? throw new ArgumentNullException(nameof(theme));
        this._enabled = enabled;
    }

    internal static SessionRenderer CreateDisabled()
    {
        return new SessionRenderer(AnsiConsole.Console, CodexplorerTheme.Default, enabled: false);
    }

    /// <summary>
    /// Consumes a session event stream and renders each event in order.
    /// </summary>
    /// <param name="sessionLogger">The active session logger whose events should be rendered.</param>
    /// <param name="ct">The cancellation token for the rendering loop.</param>
    public async Task RenderAsync(ISessionLogger sessionLogger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionLogger);

        if (!this._enabled)
        {
            return;
        }

        var state = new RenderState(sessionLogger.LogFilePath);

        await foreach (var evt in sessionLogger.Events.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case SessionStartedEvent started:
                    state.ContextWindowTokens = started.Budget.ContextWindowTokens;
                    this._console.Write(BannerComponent.Render(started, this._theme));
                    this._console.WriteLine();
                    break;

                case PreparedContextEvent prepared:
                    this._console.Write(StatusLineComponent.Render(
                        prepared.TurnIndex,
                        prepared.TokensAfterCompaction,
                        state.ContextWindowTokens,
                        this._theme));
                    this._console.WriteLine();
                    this._console.Write(PrepareResultCard.Render(prepared, this._theme));
                    this._console.WriteLine();
                    break;

                case ToolCalledEvent toolCalled:
                    this._console.Write(ToolCallEntry.RenderCalled(toolCalled.ToolName, toolCalled.ArgumentsJson, this._theme));
                    this._console.WriteLine();
                    break;

                case ToolCompletedEvent toolCompleted:
                    this._console.Write(ToolCallEntry.RenderCompleted(
                        toolCompleted.ToolName,
                        toolCompleted.ResultContent,
                        toolCompleted.Duration,
                        state.LogFilePath,
                        this._theme));
                    this._console.WriteLine();
                    break;

                case ModelRespondedEvent modelResponded:
                    RenderModelResponded(modelResponded, state, this._console, this._theme);
                    break;

                case AssistantReplyEvent assistantReply:
                    state.LastAssistantReply = assistantReply.Content;
                    this._console.Write(AnswerPanel.Render(assistantReply.Content, this._theme));
                    this._console.WriteLine();
                    break;

                case SessionEndedEvent sessionEnded:
                    RenderSessionEnded(sessionEnded, state, this._console, this._theme);
                    break;

                case SessionCancelledEvent cancelled:
                    this._console.Write(DegradationNotice.RenderWarning(
                        "Run Cancelled",
                        $"Stopped at turn {cancelled.TurnIndex + 1}. {cancelled.PartialReason}",
                        state.LogFilePath,
                        this._theme));
                    this._console.WriteLine();
                    break;

                case SessionFailedEvent failed:
                    this._console.Write(DegradationNotice.RenderError(
                        "Run Failed",
                        $"{failed.ExceptionType}: {failed.Message}",
                        state.LogFilePath,
                        this._theme));
                    this._console.WriteLine();
                    break;
            }
        }
    }

    private static void RenderModelResponded(
        ModelRespondedEvent evt,
        RenderState state,
        IAnsiConsole console,
        CodexplorerTheme theme)
    {
        if (evt.ToolCallsIssued.Count == 0)
        {
            state.LastAssistantContent = evt.AssistantContent;
            return;
        }

        if (evt.ToolCallsIssued.Count > 0 && string.IsNullOrWhiteSpace(evt.AssistantContent))
        {
            console.Write(new Text($"Model issuing {evt.ToolCallsIssued.Count} tool call(s).", theme.AccentStyle));
            console.WriteLine();
            return;
        }

        if (evt.ToolCallsIssued.Count > 0)
        {
            console.Write(new Text($"Model responded and requested {evt.ToolCallsIssued.Count} tool call(s).", theme.AccentStyle));
            console.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(evt.AssistantContent))
        {
            return;
        }

        state.LastAssistantContent = evt.AssistantContent;
        console.Write(new Text(evt.AssistantContent));
        console.WriteLine();
    }

    private static void RenderSessionEnded(
        SessionEndedEvent evt,
        RenderState state,
        IAnsiConsole console,
        CodexplorerTheme theme)
    {
        if (string.Equals(evt.TerminalOutcome, "EndedByUser", StringComparison.Ordinal))
        {
            console.Write(new Text($"Session ended. Transcript saved to {state.LogFilePath}", theme.MutedStyle));
            console.WriteLine();
            return;
        }

        console.Write(DegradationNotice.RenderWarning(
            "Session Ended",
            $"{evt.TerminalOutcome}. Total turns: {evt.TotalTurns}. Total tokens: {evt.TotalReportedTokens?.ToString() ?? "n/a"}.",
            state.LogFilePath,
            theme));
        console.WriteLine();
    }

    private sealed class RenderState(string logFilePath)
    {
        public string LogFilePath { get; } = logFilePath;

        public int ContextWindowTokens { get; set; }

        public string? LastAssistantContent { get; set; }

        public string? LastAssistantReply { get; set; }
    }
}
