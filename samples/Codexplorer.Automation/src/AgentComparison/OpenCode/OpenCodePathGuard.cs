namespace Codexplorer.Automation.AgentComparison.OpenCode;

/// <summary>Validates adapter-owned paths beneath the isolated artifact root.</summary>
internal static class OpenCodePathGuard
{
    /// <summary>Creates and validates a canonical artifact directory.</summary>
    /// <param name="path">The intended artifact root.</param>
    /// <returns>The validated absolute directory.</returns>
    /// <exception cref="UnauthorizedAccessException">A filesystem link changes the root.</exception>
    public static string PrepareArtifactRoot(string path)
    {
        return PrepareIsolatedRoot(path, "artifact");
    }

    /// <summary>Creates an isolated directory and rejects symbolic-link ancestors.</summary>
    /// <param name="path">The intended absolute root.</param>
    /// <param name="purpose">The diagnostic name of this root.</param>
    /// <returns>The validated absolute directory.</returns>
    /// <exception cref="UnauthorizedAccessException">A filesystem link changes the root.</exception>
    public static string PrepareIsolatedRoot(string path, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var root = Path.GetFullPath(path);
        Directory.CreateDirectory(root);
        var resolved = ResolveLinks(root);
        if (!string.Equals(root, resolved, PathComparison))
            throw new UnauthorizedAccessException($"The isolated {purpose} root resolves through a symbolic link.");
        return root;
    }

    /// <summary>Resolves one flat adapter-owned artifact name without traversal or links.</summary>
    /// <param name="root">The authorized root.</param>
    /// <param name="fileName">The flat artifact file name.</param>
    /// <returns>The contained absolute file path.</returns>
    /// <exception cref="UnauthorizedAccessException">The name or existing path is outside the authorized root.</exception>
    public static string ChildFile(string root, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName is "." or "..")
            throw new UnauthorizedAccessException("The artifact file name is invalid.");

        var fullPath = Path.GetFullPath(Path.Combine(root, fileName));
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || string.Equals(relative, "..", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The artifact path escapes the authorized root.");

        if (File.Exists(fullPath) && File.ResolveLinkTarget(fullPath, returnFinalTarget: true) is not null)
            throw new UnauthorizedAccessException("The artifact file is a symbolic link.");
        return fullPath;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string ResolveLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new IOException("The artifact root has no filesystem root.");
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget is null)
                continue;
            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new IOException("The artifact root contains a dangling symbolic link.");
        }

        return Path.GetFullPath(current);
    }
}
