namespace Codexplorer.Automation.Configuration;

internal sealed record CodexplorerAutomationOptions
{
    public const string SectionName = "CodexplorerAutomation";

    public string? CodexplorerExecutablePath { get; init; }

    public string? ManifestPath { get; init; } = "./tasks/initial-corpus.json";

    public IReadOnlyList<AutomationTaskDefinition> Tasks { get; init; } = [];

    public AutomationTurnBudgetOptions TurnBudgets { get; init; } = new();

    public AutomationHelperAiOptions HelperAi { get; init; } = new();

    public TurnBudgetProfile GetTurnBudget(RunnerTaskSize taskSize)
    {
        return taskSize switch
        {
            RunnerTaskSize.Small => this.TurnBudgets.Small,
            RunnerTaskSize.Medium => this.TurnBudgets.Medium,
            RunnerTaskSize.Large => this.TurnBudgets.Large,
            _ => throw new ArgumentOutOfRangeException(nameof(taskSize), taskSize, "Unsupported task size.")
        };
    }
}

internal sealed record AutomationTaskDefinition
{
    public string? TaskId { get; init; }

    public string? Title { get; init; }

    public string? RepositoryUrl { get; init; }

    public RunnerTaskSize TaskSize { get; init; } = RunnerTaskSize.Medium;

    public string? InitialPrompt { get; init; }
}

internal sealed record AutomationTaskManifest
{
    public IReadOnlyList<AutomationTaskDefinition> Tasks { get; init; } = [];
}

internal enum RunnerTaskSize
{
    Small,
    Medium,
    Large
}

internal sealed record AutomationTurnBudgetOptions
{
    public TurnBudgetProfile Small { get; init; } = new()
    {
        MaxTurns = 24,
        WrapUpWindow = 4
    };

    public TurnBudgetProfile Medium { get; init; } = new()
    {
        MaxTurns = 48,
        WrapUpWindow = 8
    };

    public TurnBudgetProfile Large { get; init; } = new()
    {
        MaxTurns = 80,
        WrapUpWindow = 12
    };
}

internal sealed record TurnBudgetProfile
{
    public int MaxTurns { get; init; } = 24;

    public int WrapUpWindow { get; init; } = 4;

    public int WrapUpTriggerTurns => this.MaxTurns - this.WrapUpWindow;
}

internal sealed record AutomationHelperAiOptions
{
    public string? Endpoint { get; init; } = "https://openrouter.ai/api/v1/chat/completions";

    public string? ModelName { get; init; } = "openai/gpt-5.4-mini";

    public string? ApiKey { get; init; } = string.Empty;

    public int MaxOutputTokens { get; init; } = 512;

    public double Temperature { get; init; } = 0.0;
}
