using System.Text.Json;
using SemanticFold.Models.Content;

namespace SemanticFold.Abstractions;

/// <summary>
/// Defines a callable tool that can be invoked by an LLM during an agentic loop.
/// </summary>
public interface ITool
{
    /// <summary>
    /// The unique name of the tool, matching the function name the LLM will call.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A description of the tool's purpose, used by the LLM to decide when to invoke it.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The JSON Schema for the tool's parameters, used by the LLM to construct valid calls.
    /// </summary>
    JsonDocument? ParametersSchema { get; }

    /// <summary>
    /// Executes the tool with the provided arguments and returns the result.
    /// </summary>
    /// <param name="argumentsJson">The JSON arguments provided by the LLM.</param>
    /// <returns>The result of the tool execution as a string.</returns>
    string Execute(string argumentsJson);
}
