using Codexplorer.Agent;

namespace Codexplorer.Configuration;

/// <summary>
/// Represents startup configuration bound from the <c>Codexplorer</c> section.
/// </summary>
/// <remarks>
/// <para>
/// This record is the single application-facing contract for Codexplorer sample configuration.
/// Later subsystems resolve one validated <see cref="CodexplorerOptions"/> snapshot from dependency
/// injection instead of reading raw keys throughout the application.
/// </para>
/// <para>
/// Each subsection is initialized with documented defaults so omitted subsections in
/// <c>appsettings.json</c> still produce complete, predictable configuration state.
/// </para>
/// </remarks>
public sealed record CodexplorerOptions
{
    /// <summary>
    /// Gets configuration section name used for binding.
    /// </summary>
    public const string SectionName = "Codexplorer";

    /// <summary>
    /// Gets token-budget settings that govern when Codexplorer should compact context.
    /// </summary>
    public BudgetOptions? Budget { get; init; } = new();

    /// <summary>
    /// Gets model-selection settings for Codexplorer's LLM requests.
    /// </summary>
    public ModelOptions? Model { get; init; } = new();

    /// <summary>
    /// Gets workspace-management settings for repository checkout and size limits.
    /// </summary>
    public WorkspaceOptions? Workspace { get; init; } = new();

    /// <summary>
    /// Gets agent-loop settings that bound exploration turns.
    /// </summary>
    public AgentOptions? Agent { get; init; } = new();

    /// <summary>
    /// Gets logging settings for Codexplorer diagnostics and per-session output.
    /// </summary>
    public LoggingOptions? Logging { get; init; } = new();

    /// <summary>
    /// Gets OpenRouter provider settings used for authenticated API calls.
    /// </summary>
    public OpenRouterOptions? OpenRouter { get; init; } = new();
}

/// <summary>
/// Represents context-budget settings for Codexplorer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContextWindowTokens"/> is total token budget Codexplorer later passes into
/// TokenGuard's context-management pipeline. <see cref="SoftThresholdRatio"/> and
/// <see cref="HardThresholdRatio"/> are applied to that total budget to derive soft and hard
/// compaction thresholds, so consumers can reason about percentages without re-deriving raw token
/// counts.
/// </para>
/// <para>
/// <see cref="WindowSize"/> maps to compaction strategy's protected recent-message window. Keeping
/// this value small makes Codexplorer compaction aggressive and visible, which matches sample's demo
/// focus.
/// </para>
/// </remarks>
public sealed record BudgetOptions
{
    /// <summary>
    /// Gets total token budget allocated to conversation context before output tokens are considered.
    /// </summary>
    public int ContextWindowTokens { get; init; } = 16_000;

    /// <summary>
    /// Gets ratio of <see cref="ContextWindowTokens"/> that triggers normal compaction pressure.
    /// </summary>
    public double SoftThresholdRatio { get; init; } = 0.70;

    /// <summary>
    /// Gets ratio of <see cref="ContextWindowTokens"/> that triggers emergency compaction pressure.
    /// </summary>
    public double HardThresholdRatio { get; init; } = 0.90;

    /// <summary>
    /// Gets minimum count of recent messages preserved by the compaction window.
    /// </summary>
    public int WindowSize { get; init; } = 5;
}

/// <summary>
/// Represents model-selection settings for Codexplorer.
/// </summary>
/// <remarks>
/// These values define default provider model identity and generation parameters for Codexplorer's
/// LLM requests. The sample binds them once at startup so later runtime components consume a stable,
/// validated snapshot.
/// </remarks>
public sealed record ModelOptions
{
    /// <summary>
    /// Gets model identifier sent to OpenRouter.
    /// </summary>
    public string? Name { get; init; } = "google/gemini-2.5-flash";

    /// <summary>
    /// Gets upper bound for model-generated output tokens.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 2_048;

    /// <summary>
    /// Gets sampling temperature used for model generation.
    /// </summary>
    public double Temperature { get; init; } = 0.0;
}

/// <summary>
/// Represents workspace settings for repository checkout and local file constraints.
/// </summary>
/// <remarks>
/// These values define where Codexplorer stores working repositories and what repository size and
/// clone-depth limits later workspace services should enforce.
/// </remarks>
public sealed record WorkspaceOptions
{
    /// <summary>
    /// Gets root directory where Codexplorer-managed workspaces live.
    /// </summary>
    public string? RootDirectory { get; init; } = "./workspace";

    /// <summary>
    /// Gets git clone depth requested for workspace checkouts.
    /// </summary>
    public int CloneDepth { get; init; } = 1;

    /// <summary>
    /// Gets maximum allowed repository size in megabytes.
    /// </summary>
    public int MaxRepoSizeMB { get; init; } = 500;
}

/// <summary>
/// Represents logging settings for Codexplorer diagnostics.
/// </summary>
/// <remarks>
/// These values describe where session logs should be stored and which minimum log level should be
/// used when later logging infrastructure begins honoring the Codexplorer logging subsection.
/// </remarks>
public sealed record LoggingOptions
{
    /// <summary>
    /// Gets directory where per-session log files should be written.
    /// </summary>
    public string? SessionLogsDirectory { get; init; } = "./logs/sessions";

    /// <summary>
    /// Gets configured minimum log level name.
    /// </summary>
    public string? MinimumLevel { get; init; } = "Information";
}

/// <summary>
/// Represents OpenRouter connectivity settings for Codexplorer.
/// </summary>
/// <remarks>
/// <para>
/// This subsection carries provider-specific authentication material that the sample binds from local
/// development configuration instead of reading ad-hoc keys at runtime.
/// </para>
/// <para>
/// <see cref="ApiKey"/> is intentionally validated on startup and should live in
/// <c>appsettings.Development.json</c> so contributors can keep secrets outside committed sample
/// configuration.
/// </para>
/// </remarks>
public sealed record OpenRouterOptions
{
    /// <summary>
    /// Gets OpenRouter API key used to authenticate requests.
    /// </summary>
    public string? ApiKey { get; init; } = string.Empty;
}
