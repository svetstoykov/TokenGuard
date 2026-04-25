using System.Text;
using System.Text.RegularExpressions;

namespace Codexplorer.Sessions;

/// <summary>
/// Produces filesystem-safe slug text for session transcript filenames.
/// </summary>
/// <remarks>
/// Session logs are intended to be opened directly by contributors on multiple operating systems, so filename text must
/// stay within a conservative ASCII subset. Any unsupported run collapses to one underscore and an empty result becomes
/// <c>query</c>.
/// </remarks>
public static partial class SessionSlug
{
    /// <summary>
    /// Converts free-form query text into a filesystem-safe slug.
    /// </summary>
    /// <param name="value">The source query text.</param>
    /// <returns>A slug containing only <c>[a-zA-Z0-9_-]</c>, or <c>query</c> when no safe characters remain.</returns>
    public static string Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "query";
        }

        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
                previousWasUnderscore = false;
                continue;
            }

            if (previousWasUnderscore)
            {
                continue;
            }

            builder.Append('_');
            previousWasUnderscore = true;
        }

        var slug = TrimBoundaryUnderscores().Replace(builder.ToString(), string.Empty);
        return string.IsNullOrWhiteSpace(slug) ? "query" : slug;
    }

    [GeneratedRegex("^_+|_+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrimBoundaryUnderscores();
}
