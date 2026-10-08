using System.Diagnostics;

namespace Codexplorer.Automation.Configuration;

/// <summary>Captures git provenance from an explicit or autodetected TokenGuard checkout.</summary>
internal sealed class GitRepositoryIdentityReader : IRepositoryIdentityReader
{
    /// <inheritdoc />
    public async Task<RepositoryIdentity> ReadAsync(string? repositoryPath, CancellationToken ct)
    {
        var path = repositoryPath ?? FindCheckout(Environment.CurrentDirectory) ?? FindCheckout(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("TokenGuard checkout could not be found; configure RepositoryPath.");
        var sha = await ReadGitAsync(path, "rev-parse", "HEAD", ct).ConfigureAwait(false);
        var status = await ReadGitAsync(path, "status", "--porcelain", ct).ConfigureAwait(false);
        return new RepositoryIdentity(sha.Trim(), !string.IsNullOrWhiteSpace(status));
    }

    private static string? FindCheckout(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) && File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static async Task<string> ReadGitAsync(string path, string command, string argument, CancellationToken ct)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var value in new[] { "-C", Path.GetFullPath(path), command, argument })
        {
            start.ArgumentList.Add(value);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not start.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Git could not read the configured repository identity.");
            }

            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
