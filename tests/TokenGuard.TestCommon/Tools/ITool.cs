using System.Text.Json;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.TestCommon.Tools;

/// <summary>
/// Defines a callable tool that a live agent-loop test can expose to the model.
/// </summary>
/// <remarks>
/// <para>
/// Implement <see cref="ITool"/> for deterministic filesystem-style operations that should round-trip through
/// TokenGuard as tool calls and tool results during tests.
/// </para>
/// <para>
/// Keeping this contract in a shared test project lets multiple E2E suites exercise the same realistic tool layer
/// without copying execution logic between projects.
/// </para>
/// </remarks>
public interface ITool
{
    /// <summary>
    /// Gets the stable tool name exposed to the model.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the natural-language description surfaced to the model.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON Schema describing the accepted argument payload.
    /// </summary>
    JsonDocument? ParametersSchema { get; }

    /// <summary>
    /// Executes the tool for the provided JSON argument payload.
    /// </summary>
    /// <param name="argumentsJson">The raw JSON arguments emitted by the model.</param>
    /// <returns>The textual tool result recorded back into conversation history.</returns>
    string Execute(string argumentsJson);
}
