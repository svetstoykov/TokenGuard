namespace TokenGuard.Samples.Console.AgentLoops;

public interface IAgentLoop
{
    string Name { get; }

    Task RunAsync();
}
