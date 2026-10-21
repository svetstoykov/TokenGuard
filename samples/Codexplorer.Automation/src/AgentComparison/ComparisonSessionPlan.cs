using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents ComparisonSessionPlan data for the captured comparison.</summary>
/// <param name="Id">The unique future session identifier.</param>
/// <param name="Tier">The workload size level.</param>
/// <param name="Repetition">The one-based repetition.</param>
/// <param name="Arm">The comparison arm.</param>
/// <param name="Task">The original task and checks.</param>
/// <param name="Prompt">The arm-specific prompt.</param>
/// <param name="SourcePromptSha256">The original prompt digest.</param>
/// <param name="AdaptedPromptSha256">The effective prompt digest.</param>
/// <param name="MaxAgentCalls">The total main-agent allowance.</param>
/// <param name="WrapUpCalls">The reserved wrap-up allowance.</param>
/// <param name="ContextWindowTokens">The configured window, or null for native OpenCode policy.</param>
internal sealed record ComparisonSessionPlan(
    string Id,
    string Tier,
    int Repetition,
    string Arm,
    AutomationTaskDefinition Task,
    string Prompt,
    string SourcePromptSha256,
    string AdaptedPromptSha256,
    int MaxAgentCalls,
    int WrapUpCalls,
    int? ContextWindowTokens);
