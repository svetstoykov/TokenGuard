using System.Text.Json;
using Codexplorer.Configuration;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Workspace;

/// <summary>
/// Manages local GitHub repository workspaces under the configured workspace root.
/// </summary>
/// <remarks>
/// This service owns GitHub URL validation, stable workspace folder naming, repository size
/// enforcement, and lightweight metadata persistence so later Codexplorer features can discover
/// already-cloned repositories without re-contacting GitHub.
/// </remarks>
public sealed class WorkspaceManager : IWorkspaceManager
{
    private const string MetadataFileName = ".codexplorer-workspace.json";
    private const string AcceptedUrlFormatsMessage =
        "Accepted GitHub URL formats are https://github.com/{owner}/{repo}[.git] and git@github.com:{owner}/{repo}[.git].";

    private static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly StringComparison HostComparison = StringComparison.OrdinalIgnoreCase;

    private readonly IGitCloner _gitCloner;
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly WorkspaceOptions _workspaceOptions;
    private readonly string _workspaceRootDirectory;
    private readonly SemaphoreSlim _cloneGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceManager"/> class.
    /// </summary>
    /// <param name="gitCloner">The git clone adapter used for repository checkout.</param>
    /// <param name="options">The validated Codexplorer options snapshot.</param>
    /// <param name="logger">The logger used for clone and cleanup diagnostics.</param>
    public WorkspaceManager(
        IGitCloner gitCloner,
        IOptions<CodexplorerOptions> options,
        ILogger<WorkspaceManager> logger)
    {
        ArgumentNullException.ThrowIfNull(gitCloner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this._gitCloner = gitCloner;
        this._logger = logger;
        this._workspaceOptions = options.Value.Workspace;
        this._workspaceRootDirectory = Path.GetFullPath(this._workspaceOptions.RootDirectory);
    }

    /// <inheritdoc />
    public async Task<Workspace> CloneAsync(string githubUrl, bool forceReclone = false, CancellationToken ct = default)
    {
        var repositoryReference = ParseGitHubUrl(githubUrl);
        var destinationPath = this.GetDestinationPath(repositoryReference);

        await this._cloneGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(this._workspaceRootDirectory);

            if (Directory.Exists(destinationPath))
            {
                if (Repository.IsValid(destinationPath))
                {
                    if (!forceReclone)
                    {
                        return this.EnsureTrackedWorkspace(repositoryReference, destinationPath);
                    }

                    this.DeleteDirectory(destinationPath);
                }
                else if (forceReclone)
                {
                    this.DeleteDirectory(destinationPath);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Destination '{destinationPath}' already exists but is not a valid git repository. Use forceReclone to replace it.");
                }
            }

            var clonedAt = DateTime.UtcNow;

            try
            {
                await this._gitCloner.CloneAsync(githubUrl, destinationPath, this._workspaceOptions.CloneDepth, ct).ConfigureAwait(false);

                var sizeBytes = ComputeDirectorySize(destinationPath);
                var maxSizeBytes = this.GetMaximumSizeBytes();

                if (sizeBytes > maxSizeBytes)
                {
                    throw new RepositoryTooLargeException(repositoryReference.OwnerRepo, sizeBytes, maxSizeBytes);
                }

                var workspace = new Workspace(
                    repositoryReference.Repository,
                    repositoryReference.OwnerRepo,
                    destinationPath,
                    clonedAt,
                    sizeBytes);

                this.WriteMetadata(workspace);
                return workspace;
            }
            catch
            {
                this.TryDeleteDirectory(destinationPath);
                throw;
            }
        }
        finally
        {
            this._cloneGate.Release();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Workspace> ListExisting()
    {
        if (!Directory.Exists(this._workspaceRootDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(this._workspaceRootDirectory)
            .Where(Repository.IsValid)
            .Select(this.TryLoadWorkspace)
            .OfType<Workspace>()
            .OrderByDescending(workspace => workspace.ClonedAt)
            .ToArray();
    }

    /// <inheritdoc />
    public Workspace? Find(string ownerRepo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerRepo);

        return this.ListExisting()
            .FirstOrDefault(workspace => string.Equals(workspace.OwnerRepo, ownerRepo, StringComparison.OrdinalIgnoreCase));
    }

    private Workspace EnsureTrackedWorkspace(RepositoryReference repositoryReference, string destinationPath)
    {
        var existingWorkspace = this.TryLoadWorkspace(destinationPath);

        if (existingWorkspace is not null)
        {
            return existingWorkspace;
        }

        var clonedAt = Directory.GetCreationTimeUtc(destinationPath);
        var workspace = new Workspace(
            repositoryReference.Repository,
            repositoryReference.OwnerRepo,
            destinationPath,
            clonedAt,
            ComputeDirectorySize(destinationPath));

        this.WriteMetadata(workspace);
        this._logger.LogInformation("Tracked existing repository workspace {OwnerRepo} at {LocalPath}", workspace.OwnerRepo, workspace.LocalPath);
        return workspace;
    }

    private Workspace? TryLoadWorkspace(string destinationPath)
    {
        var metadataPath = GetMetadataPath(destinationPath);

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(metadataPath);
            var metadata = JsonSerializer.Deserialize<WorkspaceMetadata>(stream, MetadataSerializerOptions);

            if (metadata is null || string.IsNullOrWhiteSpace(metadata.OwnerRepo) || string.IsNullOrWhiteSpace(metadata.Repository))
            {
                this._logger.LogWarning("Workspace metadata at {MetadataPath} is missing required values.", metadataPath);
                return null;
            }

            return new Workspace(
                metadata.Repository,
                metadata.OwnerRepo,
                destinationPath,
                metadata.ClonedAt,
                ComputeDirectorySize(destinationPath));
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to read workspace metadata from {MetadataPath}", metadataPath);
            return null;
        }
    }

    private void WriteMetadata(Workspace workspace)
    {
        var metadata = new WorkspaceMetadata(workspace.OwnerRepo, workspace.Name, workspace.ClonedAt);
        var metadataPath = GetMetadataPath(workspace.LocalPath);
        var directoryPath = Path.GetDirectoryName(metadataPath);

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, MetadataSerializerOptions));
    }

    private string GetDestinationPath(RepositoryReference repositoryReference)
    {
        return Path.Combine(this._workspaceRootDirectory, $"{repositoryReference.Owner}-{repositoryReference.Repository}");
    }

    private static string GetMetadataPath(string destinationPath)
    {
        return Path.Combine(destinationPath, MetadataFileName);
    }

    private static RepositoryReference ParseGitHubUrl(string githubUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(githubUrl);

        if (TryParseHttpsUrl(githubUrl, out var httpsReference))
        {
            return httpsReference;
        }

        if (TryParseSshUrl(githubUrl, out var sshReference))
        {
            return sshReference;
        }

        throw new ArgumentException(
            $"GitHub URL '{githubUrl}' is invalid. {AcceptedUrlFormatsMessage}",
            nameof(githubUrl));
    }

    private static bool TryParseHttpsUrl(string githubUrl, out RepositoryReference repositoryReference)
    {
        repositoryReference = default;

        if (!Uri.TryCreate(githubUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, HostComparison)
            || !string.Equals(uri.Host, "github.com", HostComparison)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var pathSegments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathSegments.Length != 2)
        {
            return false;
        }

        return TryCreateRepositoryReference(pathSegments[0], pathSegments[1], out repositoryReference);
    }

    private static bool TryParseSshUrl(string githubUrl, out RepositoryReference repositoryReference)
    {
        repositoryReference = default;
        const string prefix = "git@github.com:";

        if (!githubUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = githubUrl[prefix.Length..].TrimEnd('/');
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathSegments.Length != 2)
        {
            return false;
        }

        return TryCreateRepositoryReference(pathSegments[0], pathSegments[1], out repositoryReference);
    }

    private static bool TryCreateRepositoryReference(string owner, string repository, out RepositoryReference repositoryReference)
    {
        repositoryReference = default;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        var normalizedRepository = repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repository[..^4]
            : repository;

        if (string.IsNullOrWhiteSpace(normalizedRepository)
            || ContainsInvalidSegmentCharacters(owner)
            || ContainsInvalidSegmentCharacters(normalizedRepository))
        {
            return false;
        }

        repositoryReference = new RepositoryReference(owner, normalizedRepository);
        return true;
    }

    private static long ComputeDirectorySize(string path)
    {
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        return Directory.EnumerateFiles(path, "*", enumerationOptions)
            .Where(filePath => !string.Equals(Path.GetFileName(filePath), MetadataFileName, StringComparison.Ordinal))
            .Select(filePath => new FileInfo(filePath).Length)
            .Sum();
    }

    private static bool ContainsInvalidSegmentCharacters(string value)
    {
        return value.IndexOfAny(['/', ':', '?', '#', '\\']) >= 0
            || value.Any(char.IsWhiteSpace);
    }

    private long GetMaximumSizeBytes()
    {
        return checked((long)this._workspaceOptions.MaxRepoSizeMB * 1024L * 1024L);
    }

    private void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            this.DeleteDirectory(path);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to clean up workspace directory {LocalPath}", path);
        }
    }

    private sealed record WorkspaceMetadata(string OwnerRepo, string Repository, DateTime ClonedAt);

    private readonly record struct RepositoryReference(string Owner, string Repository)
    {
        public string OwnerRepo => $"{this.Owner}/{this.Repository}";
    }
}
