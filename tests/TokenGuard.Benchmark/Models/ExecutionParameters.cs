using OpenAI.Chat;
using TokenGuard.E2E.Tasks;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Benchmark.Models;

/// <summary>
/// Bundles infrastructure state shared across both raw and managed benchmark execution paths.
/// </summary>
/// <param name="Task">Task definition being executed.</param>
/// <param name="Configuration">Benchmark configuration governing execution behavior.</param>
/// <param name="ChatClient">Chat client used to call the model.</param>
/// <param name="ChatOptions">Chat completion options including tool definitions.</param>
/// <param name="ToolMap">Tool instances keyed by function name for dispatch during tool calls.</param>
/// <param name="Turns">Mutable list accumulating per-turn telemetry during execution.</param>
/// <param name="WorkspaceDirectory">Absolute path to the seeded workspace directory.</param>
internal sealed record ExecutionParameters(
    AgentLoopTaskDefinition Task,
    BenchmarkConfiguration Configuration,
    ChatClient ChatClient,
    ChatCompletionOptions ChatOptions,
    IReadOnlyDictionary<string, ITool> ToolMap,
    List<TurnTelemetry> Turns,
    string WorkspaceDirectory);
