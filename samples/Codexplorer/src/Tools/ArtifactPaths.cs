using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Codexplorer.Tools;

/// <summary>
///     Provides the path resolution shared by the artifact tools.
/// </summary>
/// <remarks>
///     Every artifact tool resolves a path through this helper, so one path names the same file for creating, writing,
///     reading, and listing.
/// </remarks>
internal static class ArtifactPaths
{
    internal static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    ///     Resolves a path given to an artifact tool to a file inside the artifacts folder.
    /// </summary>
    /// <param name="artifactsDirectory">The absolute path of the session's artifacts folder.</param>
    /// <param name="path">The path supplied by the model, relative to the artifacts folder.</param>
    /// <param name="absolutePath">The resolved absolute path when the method returns <see langword="true" />.</param>
    /// <param name="artifactPath">The artifact-relative path reported back to the model, such as <c>report.md</c>.</param>
    /// <param name="error">The recoverable tool error when the method returns <see langword="false" />.</param>
    /// <returns>
    ///     <see langword="true" /> when the path stays inside the artifacts folder; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool TryResolve(
        string artifactsDirectory, string? path, out string absolutePath, out string artifactPath, [NotNullWhen(false)] out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactsDirectory);
        absolutePath = string.Empty;
        artifactPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Error: path is required";
            return false;
        }

        try
        {
            absolutePath = PathGuard.ResolvePath(artifactsDirectory, path);
        }
        catch (PathEscapeException)
        {
            error = $"Error: path is outside the artifacts folder: {ToolResultFormatting.NormalizePath(path)}";
            return false;
        }

        artifactPath = ToArtifactPath(artifactsDirectory, absolutePath);
        error = null;
        return true;
    }

    internal static string ToArtifactPath(string artifactsDirectory, string absolutePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifactsDirectory));
        return ToolResultFormatting.NormalizePath(Path.GetRelativePath(root, absolutePath));
    }
}
