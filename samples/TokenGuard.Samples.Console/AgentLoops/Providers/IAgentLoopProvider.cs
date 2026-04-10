using TokenGuard.Core.Models;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops.Providers;

internal interface IAgentLoopProvider
{
    string Name { get; }

    string ModelId { get; }

    Task<ProviderTurnResult> ExecuteTurnAsync(
        IReadOnlyList<ContextMessage> preparedMessages,
        IReadOnlyList<ITool> tools,
        CancellationToken cancellationToken = default);
}
