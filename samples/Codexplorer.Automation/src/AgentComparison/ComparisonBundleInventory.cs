using System.Security.Cryptography;
using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Builds the canonical, secret-safe inventory used to retain and verify runtime bundles.</summary>
internal static class ComparisonBundleInventory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Returns the ordinally ordered, relative file inventory for one runtime directory.</summary>
    /// <param name="directory">The bundle directory.</param>
    /// <returns>The relative paths and SHA-256 digests of retained files.</returns>
    internal static IReadOnlyList<FileDigest> Files(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Runtime bundle directory does not exist: {root}");
        }

        var files = new List<FileDigest>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (IsReparsePoint(current))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (IsReparsePoint(entry))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(root, entry);
                if (IsExcluded(relativePath))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
                else if (File.Exists(entry))
                {
                    files.Add(new FileDigest(relativePath, HashFile(entry)));
                }
            }
        }

        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Computes the canonical digest of a relative-path inventory.</summary>
    /// <param name="files">The inventory returned by <see cref="Files(string)"/>.</param>
    /// <returns>The lowercase SHA-256 digest of its compact web-JSON representation.</returns>
    internal static string Digest(IReadOnlyList<FileDigest> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var canonicalFiles = files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonicalFiles, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Checks whether a retained bundle contains a filtered or symbolic-link entry.</summary>
    /// <param name="directory">The bundle directory.</param>
    /// <returns><see langword="true" /> when the directory contains an entry excluded from inventories.</returns>
    internal static bool HasExcludedEntries(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            return true;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var relativePath = Path.GetRelativePath(root, entry);
                if (IsReparsePoint(entry) || IsExcluded(relativePath))
                {
                    return true;
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }

        return false;
    }

    /// <summary>Determines whether a runtime path is omitted from retained bundles.</summary>
    /// <param name="relativePath">The path relative to the bundle root.</param>
    /// <returns><see langword="true" /> when the entry must not be retained.</returns>
    internal static bool IsExcluded(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = segments[^1];
        return segments.Any(segment => segment.Length > 0 && segment[0] == '.')
            || string.Equals(name, "appsettings.Development.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".user", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || segments.Any(segment => segment.Equals("logs", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("state", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("sessions", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
