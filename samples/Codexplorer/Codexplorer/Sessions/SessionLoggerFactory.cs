using Codexplorer.Configuration;
using Microsoft.Extensions.Options;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Creates markdown-backed session loggers using the configured Codexplorer transcript directory.
/// </summary>
/// <remarks>
/// The factory centralizes filename generation and configuration capture so the rest of the application only needs a
/// workspace and user query to begin a new transcript.
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
    public ISessionLogger BeginSession(WorkspaceModel workspace, string userQuery)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        var logDirectory = Path.GetFullPath(this._options.Logging.SessionLogsDirectory);
        Directory.CreateDirectory(logDirectory);

        var timestampUtc = DateTime.UtcNow;
        var slug = SessionSlug.Create(userQuery);
        var baseFileName = $"{timestampUtc:yyyyMMdd-HHmmssfff}-{slug}";
        var logFilePath = CreateUniqueFilePath(logDirectory, baseFileName);

        return new MarkdownSessionLogger(
            logFilePath,
            timestampUtc,
            workspace,
            userQuery,
            this._options.Model.Name,
            this._options.Budget);
    }

    private static string CreateUniqueFilePath(string logDirectory, string baseFileName)
    {
        var attempt = 0;

        while (true)
        {
            var suffix = attempt == 0 ? string.Empty : $"-{attempt}";
            var candidatePath = Path.Combine(logDirectory, $"{baseFileName}{suffix}.md");

            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }

            attempt++;
        }
    }
}
