namespace SemanticFold.Core.Models.Content;

/// <summary>
/// Represents a plain-text content segment inside a <see cref="SemanticMessage"/>.
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
/// The constructor enforces SemanticFold's invariant that content segments always carry meaningful payload data.
    /// </remarks>
    /// <param name="Text">The non-empty text payload carried by the segment.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="Text"/> is null or whitespace.</exception>
    public TextContent(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(Text));
        }

        this.Text = Text;
    }

    /// <summary>
    /// Gets the text payload carried by this segment.
    /// </summary>
    public string Text { get; init; }
}
