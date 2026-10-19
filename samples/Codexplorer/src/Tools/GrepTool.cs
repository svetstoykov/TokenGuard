using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Searches workspace text files with .NET regular expressions.
/// </summary>
/// <remarks>
/// Use this as the cheapest way to find names, literals, or code patterns before opening files.
/// Results stream line by line with one-line context so large repositories do not need to be loaded
/// into memory to answer a search.
/// </remarks>
public sealed class GrepTool : IWorkspaceTool
{
    /// <summary>
    /// Maximum number of matches returned by one call.
    /// </summary>
    public const int MatchCap = 100;

    /// <summary>
    /// Maximum number of characters kept from one matched or context line.
    /// </summary>
    public const int LineCharacterCap = 200;

    /// <summary>
    /// Maximum number of characters in one result, including its truncation marker.
    /// </summary>
    /// <remarks>
    /// At about four characters per token one full result is about 4,000 tokens, and about 8,000 for dense text
    /// at two characters per token, which leaves it well under the smallest 20,000-token context window.
    /// </remarks>
    public const int ResultCharacterCap = 16_000;

    /// <summary>
    /// Characters of <see cref="ResultCharacterCap"/> held back for the truncation marker that follows the matches.
    /// </summary>
    private const int TruncationMarkerReserve = 160;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "grep",
        "Search workspace text files with a .NET regular expression. Prefer this to reading many files when you need to find a name, literal, or "
        + $"content pattern quickly. Each returned line is cut at {LineCharacterCap} characters and one result holds at most {ResultCharacterCap} "
        + "characters; when a result is truncated, narrow the pattern or pathGlob.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "pattern": {
              "type": "string",
              "description": "The .NET regular expression to search for."
            },
            "pathGlob": {
              "type": "string",
              "description": "Optional workspace-relative glob that limits which files are searched, such as \"src/**/*.cs\"."
            },
            "maxMatches": {
              "type": "integer",
              "description": "Optional maximum matches to return, capped at 100."
            }
          },
          "required": ["pattern"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "grep";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="GrepTool"/>.
    /// </summary>
    /// <param name="Pattern">The .NET regular expression to search for.</param>
    /// <param name="PathGlob">Optional file glob relative to the workspace root.</param>
    /// <param name="MaxMatches">Optional maximum match count, capped at <see cref="MatchCap"/>.</param>
    public sealed record Parameters(string Pattern, string? PathGlob, int? MaxMatches);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Searches matching files line by line and returns each hit with one-line context.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains file access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>Formatted match blocks, a truncation marker, or a recoverable error string.</returns>
    /// <remarks>
    /// Every returned line is cut at <see cref="LineCharacterCap"/> characters. Matches are added until <see cref="MatchCap"/> or
    /// <see cref="ResultCharacterCap"/> is reached; the remaining matches are counted and reported in the truncation marker.
    /// </remarks>
    public async Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(parameters.Pattern))
        {
            return "Error: pattern is required";
        }

        if (parameters.MaxMatches is <= 0)
        {
            return "Error: maxMatches must be greater than 0";
        }

        Regex regex;

        try
        {
            regex = new Regex(
                parameters.Pattern,
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            return $"Error: invalid regex: {ex.Message}";
        }

        var effectiveMaxMatches = Math.Min(parameters.MaxMatches ?? MatchCap, MatchCap);
        var matcher = CreateMatcher(parameters.PathGlob);
        var workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.LocalPath));
        var filePaths = Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);

        var totalMatches = 0;
        var displayedMatches = 0;
        var resultCapHit = false;
        var builder = new StringBuilder();

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = ToolResultFormatting.ToWorkspaceRelativePath(workspace, filePath);

            if (matcher is not null && !matcher.Match(relativePath).HasMatches)
            {
                continue;
            }

            if (await ToolFileHelpers.IsBinaryFileAsync(filePath, ct).ConfigureAwait(false))
            {
                continue;
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            using var reader = new StreamReader(stream);

            string? previousLine = null;
            var currentLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            var lineNumber = 1;

            while (currentLine is not null)
            {
                ct.ThrowIfCancellationRequested();

                var nextLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                try
                {
                    if (regex.IsMatch(currentLine))
                    {
                        totalMatches++;

                        if (!resultCapHit && displayedMatches < effectiveMaxMatches)
                        {
                            var block = FormatMatchBlock(relativePath, lineNumber, previousLine, currentLine, nextLine);
                            var separator = builder.Length > 0 ? Environment.NewLine + Environment.NewLine : string.Empty;

                            if (builder.Length + separator.Length + block.Length > ResultCharacterCap - TruncationMarkerReserve)
                            {
                                resultCapHit = true;
                            }
                            else
                            {
                                builder.Append(separator).Append(block);
                                displayedMatches++;
                            }
                        }
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return "Error: regex match timed out";
                }

                previousLine = currentLine;
                currentLine = nextLine;
                lineNumber++;
            }
        }

        if (totalMatches == 0)
        {
            return string.Empty;
        }

        if (resultCapHit)
        {
            builder.AppendLine();
            builder.Append(ToolResultFormatting.TruncationMarker(
                totalMatches - displayedMatches, "matches", ResultCharacterCap, "characters", "narrow the pattern or pathGlob"));
        }
        else if (totalMatches > effectiveMaxMatches)
        {
            builder.AppendLine();
            builder.Append(ToolResultFormatting.TruncationMarker(totalMatches - effectiveMaxMatches, effectiveMaxMatches, "matches"));
        }

        return builder.ToString();
    }

    private static Matcher? CreateMatcher(string? pathGlob)
    {
        if (string.IsNullOrWhiteSpace(pathGlob))
        {
            return null;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(ToolResultFormatting.NormalizePath(pathGlob));
        return matcher;
    }

    private static string FormatMatchBlock(string relativePath, int lineNumber, string? previousLine, string currentLine, string? nextLine)
    {
        var matchedLine = ToolResultFormatting.CutLine(currentLine, LineCharacterCap);
        var builder = new StringBuilder();
        builder.Append(relativePath);
        builder.Append(':');
        builder.Append(lineNumber);
        builder.Append(": ");
        builder.Append(matchedLine);

        if (previousLine is not null)
        {
            builder.AppendLine();
            builder.Append("  ");
            builder.Append(lineNumber - 1);
            builder.Append("| ");
            builder.Append(ToolResultFormatting.CutLine(previousLine, LineCharacterCap));
        }

        builder.AppendLine();
        builder.Append("> ");
        builder.Append(lineNumber);
        builder.Append("| ");
        builder.Append(matchedLine);

        if (nextLine is not null)
        {
            builder.AppendLine();
            builder.Append("  ");
            builder.Append(lineNumber + 1);
            builder.Append("| ");
            builder.Append(ToolResultFormatting.CutLine(nextLine, LineCharacterCap));
        }

        return builder.ToString();
    }
}
