namespace SemanticFold.Core.Models.Content;

/// <summary>
/// Represents a plain-text content block inside a <see cref="SemanticFold.Core.Models.Message"/>.
/// </summary>
/// <remarks>
/// Use <see cref="TextContent"/> for conversational text that does not require additional structure. This is the most
/// common block type for system prompts, user turns, and free-form model responses.
/// </remarks>
/// <param name="Text">The non-empty text payload carried by the block.</param>
public sealed record TextContent : ContentBlock
{
    /// <summary>
    /// Initializes a new <see cref="TextContent"/> instance.
    /// </summary>
    /// <remarks>
    /// The constructor enforces SemanticFold's invariant that content blocks always carry meaningful payload data.
    /// </remarks>
    /// <param name="Text">The non-empty text payload carried by the block.</param>
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
    /// Gets the text payload carried by this block.
    /// </summary>
    public string Text { get; init; }
}
