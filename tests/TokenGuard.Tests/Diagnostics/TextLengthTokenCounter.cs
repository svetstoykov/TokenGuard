using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents a token counter that charges one token for each character of message content.
/// </summary>
internal sealed class TextLengthTokenCounter : ITokenCounter
{
    /// <inheritdoc />
    public int Count(ContextMessage contextMessage) => contextMessage.Segments.Sum(segment => segment.Content.Length);

    /// <inheritdoc />
    public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(this.Count);
}
