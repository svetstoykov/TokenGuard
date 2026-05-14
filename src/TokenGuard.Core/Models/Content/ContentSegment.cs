namespace TokenGuard.Core.Models.Content;

/// <summary>
/// Represents a structured content segment that can appear inside a <see cref="ContextMessage"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContentSegment"/> is the transport-neutral base abstraction for multi-part message payloads. It lets
/// TokenGuard preserve provider-native structures such as plain text, tool calls, and tool results without collapsing
/// them into a single serialized string.
/// </para>
/// <para>
/// Derived types define the semantic meaning of <paramref name="Content"/> and may apply stricter validation rules.
/// The base record only stores the raw payload so adapters and compaction logic can operate over a uniform segment
/// model.
/// </para>
/// </remarks>
/// <param name="Content">The raw payload carried by this segment.</param>
public abstract record ContentSegment(string Content);
