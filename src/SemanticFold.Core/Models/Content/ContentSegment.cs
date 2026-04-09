namespace SemanticFold.Core.Models.Content;

/// <summary>
/// Serves as the base type for all structured payload segments that can appear inside a <see cref="SemanticMessage"/>.
/// </summary>
/// <remarks>
/// SemanticFold models message content as a segment sequence rather than a single string so adapters can preserve
/// provider-native structures such as tool calls and tool results alongside plain text.
/// </remarks>
public abstract record ContentSegment;
