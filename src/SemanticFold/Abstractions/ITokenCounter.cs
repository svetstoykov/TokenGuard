using SemanticFold.Models;

namespace SemanticFold.Abstractions;

/// <summary>
/// Defines a contract for estimating or counting tokens in messages.
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// Estimates the number of tokens for a single message.
    /// </summary>
    /// <param name="message">The message to estimate tokens for.</param>
    /// <returns>The estimated number of tokens.</returns>
    int Count(Message message);

    /// <summary>
    /// Counts the total number of tokens for a collection of messages.
    /// </summary>
    /// <param name="messages">The messages to count tokens for.</param>
    /// <returns>The total number of tokens.</returns>
    int Count(IEnumerable<Message> messages);
}
