using SemanticFold.Core.Models.Content;

namespace SemanticFold.Core.Models;

/// <summary>
/// Represents the universal return type for provider adapter response conversions, carrying the
/// extracted content blocks and the provider-reported input token count.
/// </summary>
/// <param name="Content">The content blocks extracted from the provider response.</param>
/// <param name="InputTokens">The input token count reported by the provider, when available.</param>
public sealed record AdapterResult(ContentBlock[] Content, int? InputTokens);
