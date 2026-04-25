namespace Codexplorer.Workspace;

/// <summary>
/// Represents a clone rejected because its on-disk size exceeded the configured limit.
/// </summary>
/// <remarks>
/// The workspace manager throws this after clone completion and size measurement so callers can
/// distinguish policy-driven rejection from transport or filesystem failures.
/// </remarks>
public sealed class RepositoryTooLargeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryTooLargeException"/> class.
    /// </summary>
    /// <param name="ownerRepo">The repository identity that exceeded the size limit.</param>
    /// <param name="sizeBytes">The measured on-disk size in bytes.</param>
    /// <param name="maxSizeBytes">The configured maximum allowed size in bytes.</param>
    public RepositoryTooLargeException(string ownerRepo, long sizeBytes, long maxSizeBytes)
        : base(
            $"Repository '{ownerRepo}' is {sizeBytes:N0} bytes, which exceeds the configured maximum of {maxSizeBytes:N0} bytes.")
    {
        this.OwnerRepo = ownerRepo;
        this.SizeBytes = sizeBytes;
        this.MaxSizeBytes = maxSizeBytes;
    }

    /// <summary>
    /// Gets the repository identity that exceeded the configured limit.
    /// </summary>
    public string OwnerRepo { get; }

    /// <summary>
    /// Gets the measured repository size in bytes.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Gets the configured repository size limit in bytes.
    /// </summary>
    public long MaxSizeBytes { get; }
}
