using Codexplorer.Measurements;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace Codexplorer.Diagnostics;

/// <summary>Tracks tool-result exposure and independent fact copies for one automation probe.</summary>
/// <remarks>The session collector serializes access. Tool-call IDs preserve carrier identity across prepared views.</remarks>
internal sealed class MaskedResultProbeTracker
{
    private readonly string[] _values;
    private readonly HashSet<string> _carriers = new(StringComparer.Ordinal);
    private bool _questionAsked;
    private bool _assistantRepeated;
    private bool _refetched;

    /// <summary>Initializes a new instance of the <see cref="MaskedResultProbeTracker" /> class.</summary>
    /// <param name="values">The nonempty accepted fact literals.</param>
    public MaskedResultProbeTracker(IReadOnlyList<string> values) => this._values = values.Select(ProbeText.Normalize).ToArray();

    /// <summary>Marks the first submission of the probe question.</summary>
    public void BeginQuestion() => this._questionAsked = true;

    /// <summary>Records an earlier assistant copy, including text accompanying tool calls.</summary>
    /// <param name="text">The received assistant text.</param>
    public void ObserveAssistant(string text)
    {
        if (!this._questionAsked)
            this._assistantRepeated |= this.Matches(text);
    }

    /// <summary>Records a tool result containing the fact after the question.</summary>
    /// <param name="text">The raw tool result.</param>
    /// <returns>Whether any post-question tool result has returned the fact.</returns>
    public bool ObserveToolResult(string text)
    {
        if (this._questionAsked)
            this._refetched |= this.Matches(text);
        return this._refetched;
    }

    /// <summary>Records visibility and contamination in one completed prepare.</summary>
    /// <remarks>Artifact read failures propagate so incomplete evidence cannot certify a probe as eligible.</remarks>
    /// <param name="prepare">The completed prepare telemetry.</param>
    /// <param name="messages">The exact prepared model input.</param>
    /// <param name="artifactsPath">The directory containing task artifacts.</param>
    /// <returns>The prepare with passive probe evidence.</returns>
    public PrepareMeasurement ObservePrepare(PrepareMeasurement prepare, IReadOnlyList<ContextMessage> messages, string artifactsPath)
    {
        foreach (var result in messages.Where(message => message.State == CompactionState.Original)
            .SelectMany(message => message.Segments).OfType<ToolResultContent>().Where(result => this.Matches(result.Content)))
            this._carriers.Add(result.ToolCallId);

        var masked = messages.Any(message => message.State == CompactionState.Masked
            && message.Segments.OfType<ToolResultContent>().Any(result => this._carriers.Contains(result.ToolCallId)));
        var visibleCarrier = messages.SelectMany(message => message.Segments).OfType<ToolResultContent>()
            .Any(result => this._carriers.Contains(result.ToolCallId) && this.Matches(result.Content));
        var state = this._carriers.Count == 0 ? "unseen" : masked ? "masked" : visibleCarrier ? "visible" : "removed";
        var elsewhere = this._assistantRepeated || messages.Any(message => message.Segments.Any(segment => this.Matches(segment.Content)
            && (state == "masked" || segment is not ToolResultContent result || !this._carriers.Contains(result.ToolCallId))));
        if (Directory.Exists(artifactsPath))
            elsewhere |= Directory.EnumerateFiles(artifactsPath, "*", SearchOption.AllDirectories).Any(path => this.Matches(File.ReadAllText(path)));

        return prepare with
        {
            ProbeValueState = state, ProbeFactVisibleElsewhere = elsewhere,
            ProbeQuestionAsked = this._questionAsked, ProbeRefetched = this._refetched
        };
    }

    private bool Matches(string text)
    {
        var normalized = ProbeText.Normalize(text);
        return this._values.Any(value => ProbeText.IsMatch(normalized, value));
    }
}
