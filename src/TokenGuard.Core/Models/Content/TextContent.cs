namespace TokenGuard.Core.Models.Content;

/// <summary>
/// Represents a plain-text content segment inside a <see cref="ContextMessage"/>.
/// </summary>
/// <remarks>
/// Use <see cref="TextContent"/> for conversational text that does not require additional structure. This is the most
/// common segment type for system prompts, user turns, and free-form model responses.
/// </remarks>
public sealed record TextContent : ContentSegment
{
    /// <summary>
    /// Initializes a new <see cref="TextContent"/> instance.
    /// </summary>
    /// <remarks>
/// The constructor enforces TokenGuard's invariant that content segments always carry meaningful payload data.
    /// </remarks>
    /// <param name="Content">The non-empty text payload carried by the segment.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="Content"/> is null or whitespace.</exception>
    public TextContent(string Content) : base(Content)
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(Content));
        }

        this.Content = Content;
    }
}
