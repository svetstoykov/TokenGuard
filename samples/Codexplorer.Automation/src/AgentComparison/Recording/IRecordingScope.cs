namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Provides serialized phase and budget controls for one captured runtime session.</summary>
internal interface IRecordingScope
{
    /// <summary>Gets the loopback endpoint assigned to this child session.</summary>
    Uri Endpoint { get; }

    /// <summary>Gets the disposable bearer credential assigned to this child session.</summary>
    string Credential { get; }

    /// <summary>Gets a token canceled after the main-agent allowance is consumed.</summary>
    CancellationToken MainBudgetExhausted { get; }

    /// <summary>Gets the accepted main-agent request count.</summary>
    int MainCallsConsumed { get; }

    /// <summary>Gets the combined main and reserved wrap-up request count.</summary>
    int AgentCallsConsumed { get; }

    /// <summary>Creates a credential tied to a positively identified overhead request category.</summary>
    /// <param name="kind">The helper, title or compaction request category.</param>
    /// <returns>A disposable credential that classifies only requests authenticated with that credential.</returns>
    string CredentialFor(RecordedRequestKind kind);

    /// <summary>Atomically enters the reserved wrap-up phase.</summary>
    void BeginWrapUp();

}
