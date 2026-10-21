using System.Security.Cryptography;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Provides strict relative artifact resolution and inventories.</summary>
internal static class ComparisonArtifacts
{
    /// <summary>Resolves a path while rejecting traversal and filesystem links.</summary>
    /// <param name="root">The authorized artifact directory.</param>
    /// <param name="relativePath">The untrusted relative name.</param>
    /// <returns>The contained absolute path.</returns>
    /// <exception cref="InvalidDataException">The path is rooted, traverses parents or contains a link.</exception>
    internal static string Resolve(string root, string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(relativePath) || segments.Any(segment => segment == "..") || relativePath.Contains(':'))
            throw new InvalidDataException("Artifact path escapes its authorized directory.");
        var current = Path.GetFullPath(root);
        RejectLink(current);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
        return current;
    }

    /// <summary>Inventories regular files without following directory or file links.</summary>
    /// <param name="root">The authorized artifact directory.</param>
    /// <returns>The ordered relative paths and content hashes.</returns>
    internal static IReadOnlyList<FileDigest> Inventory(string root)
    {
        var files = new List<FileDigest>();
        if (Directory.Exists(root))
            Visit(root, root, files);
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Computes the SHA-256 digest of regular file bytes.</summary>
    /// <param name="path">The file to hash.</param>
    /// <returns>The lowercase hexadecimal digest.</returns>
    internal static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private static void Visit(string root, string directory, List<FileDigest> files)
    {
        RejectLink(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLink(entry);
            if (Directory.Exists(entry))
                Visit(root, entry, files);
            else
                files.Add(new FileDigest(Path.GetRelativePath(root, entry), Hash(entry)));
        }
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
            throw new InvalidDataException("Artifact links are forbidden.");
    }
}
