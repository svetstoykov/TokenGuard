using Codexplorer.Configuration;
using Microsoft.Extensions.Options;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Creates markdown-backed session loggers that write the transcript into a session directory.
/// </summary>
/// <remarks>
/// The factory centralizes configuration capture so the rest of the application only needs a workspace, a session
/// label, and a session directory to begin a new transcript.
/// </remarks>
public sealed class SessionLoggerFactory : ISessionLoggerFactory
{
    private readonly CodexplorerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLoggerFactory"/> class.
    /// </summary>
    /// <param name="options">The validated Codexplorer options snapshot.</param>
    public SessionLoggerFactory(IOptions<CodexplorerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._options = options.Value;
    }

    /// <inheritdoc />
    public ISessionLogger BeginSession(WorkspaceModel workspace, string sessionLabel, SessionDirectory sessionDirectory)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionLabel);
        ArgumentNullException.ThrowIfNull(sessionDirectory);

        var modelOptions = this._options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");
        var budgetOptions = this._options.Budget
            ?? throw new InvalidOperationException("Codexplorer budget options are not configured.");
        var modelName = modelOptions.Name
            ?? throw new InvalidOperationException("Codexplorer model name is not configured.");

        return new MarkdownSessionLogger(sessionDirectory.TranscriptPath, DateTime.UtcNow, workspace, sessionLabel, modelName, budgetOptions);
    }
}
