using TokenGuard.Core.Models;

namespace TokenGuard.Benchmark.Retention;

/// <summary>
/// Represents synthesized conversation output ready for retention benchmarking.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SyntheticConversation"/> carries both generated message history and recall probe text so a benchmark can
/// feed the conversation directly into <see cref="TokenGuard.Core.Contexts.ConversationContext"/> or a baseline request
/// path without any additional mapping layer.
/// </para>
/// <para>
/// The message list is copied during construction to preserve immutability and keep token estimates tied to a stable
/// payload.
/// </para>
/// </remarks>
/// <param name="Profile">Scenario profile that produced this conversation. Cannot be null.</param>
/// <param name="Messages">Full synthesized alternating user and model message history. Cannot be null.</param>
/// <param name="RecallProbe">Formatted recall question block appended as final user input. Cannot be null or whitespace.</param>
/// <param name="EstimatedTokenCount">Estimated total tokens across all synthesized messages. Cannot be negative.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Profile"/> or <paramref name="Messages"/> is null.</exception>
/// <exception cref="ArgumentException">Thrown when <paramref name="RecallProbe"/> is null or whitespace.</exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="EstimatedTokenCount"/> is negative.</exception>
public sealed record SyntheticConversation(
    ScenarioProfile Profile,
    IReadOnlyList<ContextMessage> Messages,
    string RecallProbe,
    int EstimatedTokenCount)
{
    /// <summary>
    /// Gets scenario profile that produced this conversation.
    /// </summary>
    public ScenarioProfile Profile { get; } = Profile ?? throw new ArgumentNullException(nameof(Profile));

    /// <summary>
    /// Gets full synthesized alternating user and model message history.
    /// </summary>
    /// <remarks>
    /// The assigned sequence is copied during construction so downstream benchmark code observes a stable payload even if
    /// the original source collection was mutable.
    /// </remarks>
    public IReadOnlyList<ContextMessage> Messages { get; } = Messages is null
        ? throw new ArgumentNullException(nameof(Messages))
        : Messages.ToArray();

    /// <summary>
    /// Gets formatted recall question block appended after conversation playback.
    /// </summary>
    public string RecallProbe { get; } = string.IsNullOrWhiteSpace(RecallProbe)
        ? throw new ArgumentException("Recall probe cannot be null or whitespace.", nameof(RecallProbe))
        : RecallProbe;

    /// <summary>
    /// Gets estimated total tokens across all synthesized messages.
    /// </summary>
    public int EstimatedTokenCount { get; } = EstimatedTokenCount < 0
        ? throw new ArgumentOutOfRangeException(nameof(EstimatedTokenCount), "Estimated token count cannot be negative.")
        : EstimatedTokenCount;
}
