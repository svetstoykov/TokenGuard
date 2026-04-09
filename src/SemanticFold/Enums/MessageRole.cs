namespace SemanticFold.Enums;

/// <summary>
/// Represents the role of a participant in a conversation turn.
/// </summary>
public enum MessageRole
{
    /// <summary>
    /// A message from the human user.
    /// </summary>
    User,

    /// <summary>
    /// A message from the language model.
    /// </summary>
    Assistant,

    /// <summary>
    /// A message containing tool execution output.
    /// </summary>
    Tool,
}
