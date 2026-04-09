namespace SemanticFold.Enums;

/// <summary>
/// Represents the role of a participant in a conversation turn.
/// </summary>
public enum MessageRole
{
    /// <summary>
    /// A system-level instruction or context message.
    /// </summary>
    System,

    /// <summary>
    /// A message from the human user.
    /// </summary>
    User,

    /// <summary>
    /// A message from the language model.
    /// </summary>
    Model,

    /// <summary>
    /// A message containing tool execution output.
    /// </summary>
    Tool,
}
