namespace SemanticFold.Core.Enums;

/// <summary>
/// Identifies which participant produced a <see cref="SemanticFold.Core.Models.Message"/>.
/// </summary>
/// <remarks>
/// Roles drive how SemanticFold records and prepares history. For example, system messages are preserved at the front
/// of the request payload, and tool messages are used to continue provider-native function-call chains.
/// </remarks>
public enum MessageRole
{
    /// <summary>
    /// A system-level instruction that establishes persistent model behavior.
    /// </summary>
    System,

    /// <summary>
    /// A message supplied by the end user or calling application.
    /// </summary>
    User,

    /// <summary>
    /// A response produced by the language model.
    /// </summary>
    Model,

    /// <summary>
    /// A message carrying the output of a tool invocation.
    /// </summary>
    Tool,
}
