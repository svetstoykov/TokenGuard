namespace Codexplorer.Automation.AgentComparison;

/// <summary>Provides fail-closed filesystem link checks before native repository tools start.</summary>
internal static class ComparisonRepositorySafety
{
    /// <summary>Rejects repository links that escape the pinned checkout.</summary>
    /// <param name="checkout">The isolated pinned checkout directory.</param>
    /// <exception cref="InvalidDataException">A link escapes, is broken or resolves ambiguously.</exception>
    internal static void Validate(string checkout)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(checkout));
        if (new DirectoryInfo(root).LinkTarget is not null)
            throw new InvalidDataException("A comparison checkout cannot be a filesystem link.");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (Path.GetFileName(entry) == ".git")
                    continue;
                FileSystemInfo info = Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry);
                if (info.LinkTarget is not null)
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                    if (target is null || !Path.GetFullPath(target).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("A repository link escapes its isolated checkout.");
                    continue;
                }
                if (Directory.Exists(entry))
                    pending.Push(entry);
            }
        }
    }
}
