using System.Text.Json.Serialization;

namespace Codexplorer.Automation.Configuration;

/// <summary>Represents the effective runner configuration.</summary>
internal sealed record CodexplorerAutomationOptions
{
    /// <summary>Defines the configuration section name.</summary>
    public const string SectionName = "CodexplorerAutomation";

    /// <summary>Gets the absolute child executable path.</summary>
    public string? CodexplorerExecutablePath { get; init; }

    /// <summary>Gets the report output directory, defaulting to ./reports.</summary>
    public string OutputDirectory { get; init; } = "./reports";

    /// <summary>Gets the treatment or control arm, defaulting to treatment.</summary>
    public string Arm { get; init; } = "treatment";

    /// <summary>Gets the control context-window override, defaulting to one million tokens.</summary>
    public int ControlContextWindowTokens { get; init; } = 1_000_000;

    /// <summary>Gets the optional explicit TokenGuard checkout path.</summary>
    public string? RepositoryPath { get; init; }

    /// <summary>Gets the manifest path, or null to use inline tasks.</summary>
    public string? ManifestPath { get; init; } = "./tasks/initial-corpus.json";

    /// <summary>Gets the inline tasks used when no manifest path is supplied.</summary>
    public IReadOnlyList<AutomationTaskDefinition> Tasks { get; init; } = [];

    /// <summary>Gets the task model-call allowances and wrap-up windows.</summary>
    public AutomationTurnBudgetOptions TurnBudgets { get; init; } = new();

    /// <summary>Gets the helper model configuration.</summary>
    public AutomationHelperAiOptions HelperAi { get; init; } = new();

    /// <summary>Gets the budget for the requested task size.</summary>
    /// <param name="taskSize">The task size selecting the configured budget.</param>
    /// <returns>The task budget profile.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The task size is unsupported.</exception>
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

    [JsonConverter(typeof(JsonStringEnumConverter<RunnerTaskSize>))]
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
    public string? Endpoint { get; init; } = "https://openrouter.ai/api/v1";

    public string? ModelName { get; init; } = "deepseek/deepseek-v4.1-flash";

    public string? ApiKey { get; init; } = string.Empty;

    public int MaxOutputTokens { get; init; } = 512;

    public double Temperature { get; init; } = 0.0;
}
