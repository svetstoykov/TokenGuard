namespace SemanticFold.Models.Content;

/// <summary>
/// A block of plain text content.
/// </summary>
/// <param name="Text">The text content.</param>
public sealed record TextContent : ContentBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextContent"/> record.
    /// </summary>
    /// <param name="Text">The text content.</param>
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
    /// Gets the text content.
    /// </summary>
    public string Text { get; init; }
}
