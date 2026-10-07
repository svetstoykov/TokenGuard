using Codexplorer.Measurements;

namespace Codexplorer.Automation.Runner;

/// <summary>Represents a received helper response and its provider usage.</summary>
/// <param name="Answer">The answer text, or null when the response was empty.</param>
/// <param name="Usage">The provider-reported usage.</param>
internal sealed record RunnerHelperAiResult(string? Answer, UsageMeasurement Usage);
