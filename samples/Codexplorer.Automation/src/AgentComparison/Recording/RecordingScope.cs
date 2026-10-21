namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents one authenticated child session and its request budget.</summary>
internal sealed class RecordingScope : IRecordingScope
{
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _mainBudgetExhausted = new();
    private readonly object _stateLock = new();
    private readonly RecordingScopeOptions _options;
    private int _mainCalls;
    private int _wrapUpCalls;
    private bool _wrapUp;
    private readonly Func<RecordedRequestKind, string> _credentialFactory;

    /// <summary>Initializes a serialized per-session request allowance.</summary>
    /// <param name="options">The session identity and main/wrap-up limits.</param>
    /// <param name="endpoint">The loopback recorder endpoint.</param>
    /// <param name="credentialFactory">The registrar for positively identified overhead credentials.</param>
    internal RecordingScope(RecordingScopeOptions options, Uri endpoint, Func<RecordedRequestKind, string> credentialFactory)
    {
        this._options = options;
        this.Endpoint = endpoint;
        this.Credential = options.Credential;
        this.SessionId = options.SessionId;
        this._credentialFactory = credentialFactory;
    }

    /// <inheritdoc />
    public Uri Endpoint { get; }

    /// <inheritdoc />
    public string Credential { get; }

    /// <summary>Gets the identifier attached to captured requests.</summary>
    internal string SessionId { get; }

    /// <summary>Gets a token that is canceled after the main-agent allowance is consumed.</summary>
    public CancellationToken MainBudgetExhausted => this._mainBudgetExhausted.Token;

    /// <summary>Gets the number of accepted main-agent calls.</summary>
    internal int ConsumedMainCalls => Volatile.Read(ref this._mainCalls);

    /// <inheritdoc />
    public int MainCallsConsumed => this.ConsumedMainCalls;

    /// <inheritdoc />
    public int AgentCallsConsumed => this.ConsumedMainCalls + this.ConsumedWrapUpCalls;

    /// <inheritdoc />
    public string CredentialFor(RecordedRequestKind kind)
    {
        if (kind is not (RecordedRequestKind.Helper or RecordedRequestKind.Title or RecordedRequestKind.Compaction))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return this._credentialFactory(kind);
    }

    /// <summary>Gets the number of accepted reserved wrap-up calls.</summary>
    internal int ConsumedWrapUpCalls => Volatile.Read(ref this._wrapUpCalls);

    /// <summary>Atomically enters wrap-up and directs later unclassified requests to the reserved budget.</summary>
    public void BeginWrapUp()
    {
        lock (this._stateLock)
        {
            this._wrapUp = true;
        }
    }

    /// <summary>Asynchronously reserves one call before any provider forwarding occurs.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="authenticatedKind">The credential-bound overhead category, or null for conservative main charging.</param>
    /// <returns>A task containing the accepted or rejected serialized request lease.</returns>
    internal async ValueTask<RecordingRequestLease> ReserveAsync(CancellationToken cancellationToken, RecordedRequestKind? authenticatedKind = null)
    {
        await this._requestGate.WaitAsync(cancellationToken);
        RecordedRequestKind kind;
        bool rejected;
        lock (this._stateLock)
        {
            kind = authenticatedKind ?? (this._wrapUp ? RecordedRequestKind.WrapUp : RecordedRequestKind.Unknown);
            rejected = kind switch
            {
                RecordedRequestKind.WrapUp => this._wrapUpCalls >= this._options.ReservedWrapUpCalls,
                RecordedRequestKind.MainAgent or RecordedRequestKind.Unknown => this._mainCalls >=
                    this._options.MaxAgentCalls - this._options.ReservedWrapUpCalls,
                _ => false,
            };

            if (!rejected)
            {
                if (kind is RecordedRequestKind.MainAgent or RecordedRequestKind.Unknown)
                    this._mainCalls++;
                else if (kind is RecordedRequestKind.WrapUp)
                    this._wrapUpCalls++;
            }
        }

        return new RecordingRequestLease(this, kind, rejected);
    }

    /// <summary>Releases the serialized request boundary and signals a completed main budget.</summary>
    /// <param name="kind">The reserved request category.</param>
    /// <param name="rejected">Whether the proxy rejected the request.</param>
    internal void Release(RecordedRequestKind kind, bool rejected)
    {
        if (!rejected && kind is (RecordedRequestKind.MainAgent or RecordedRequestKind.Unknown)
            && this.ConsumedMainCalls >= this._options.MaxAgentCalls - this._options.ReservedWrapUpCalls)
            this._mainBudgetExhausted.Cancel();
        this._requestGate.Release();
    }
}

/// <summary>Owns one serialized request reservation until response capture finishes.</summary>
internal sealed class RecordingRequestLease : IDisposable
{
    private readonly RecordingScope _scope;
    private readonly RecordedRequestKind _kind;
    private readonly bool _rejected;

    /// <summary>Initializes a lease for one categorized model request.</summary>
    /// <param name="scope">The session budget scope to release.</param>
    /// <param name="kind">The request category.</param>
    /// <param name="rejected">Whether the call exceeded its allowance.</param>
    internal RecordingRequestLease(RecordingScope scope, RecordedRequestKind kind, bool rejected)
    {
        this._scope = scope;
        this._kind = kind;
        this._rejected = rejected;
        this.Kind = kind;
        this.Rejected = rejected;
    }

    /// <summary>Gets the category assigned to this request.</summary>
    public RecordedRequestKind Kind { get; }

    /// <summary>Gets whether the proxy rejected this request before provider execution.</summary>
    public bool Rejected { get; }

    /// <summary>Releases the request gate and signals the main-call threshold when reached.</summary>
    public void Dispose() => this._scope.Release(this._kind, this._rejected);
}
