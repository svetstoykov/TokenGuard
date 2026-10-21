using System.Text.Json.Serialization;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents one captured provider request and its response or failure.</summary>
/// <param name="Sequence">The attempt sequence within the proxy.</param>
/// <param name="SessionId">The associated comparison session.</param>
/// <param name="Kind">The request classification used for budgeting.</param>
/// <param name="StartedAt">The time the attempt began.</param>
/// <param name="EndedAt">The time the attempt ended.</param>
/// <param name="RequestBody">The exact incoming JSON request bytes.</param>
/// <param name="EffectiveBody">The exact JSON bytes forwarded or sent to the fake provider.</param>
/// <param name="ResponseBody">The exact provider response bytes observed by the child.</param>
/// <param name="StatusCode">The provider status code, when a response arrived.</param>
/// <param name="Model">The model identifier parsed from the request.</param>
/// <param name="GenerationId">The provider generation identifier, when present.</param>
/// <param name="Usage">The normalized provider usage, or null when absent or malformed.</param>
/// <param name="RawUsageJson">The raw usage JSON object, when present.</param>
/// <param name="Error">A safe failure description without request headers.</param>
/// <param name="BudgetRejected">Whether the proxy rejected the request before provider execution.</param>
/// <param name="CaptureFiles">Relative paths to exact body captures for this attempt.</param>
internal sealed record RecordedAttempt(
    long Sequence,
    string SessionId,
    RecordedRequestKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    [property: JsonIgnore] byte[] RequestBody,
    [property: JsonIgnore] byte[] EffectiveBody,
    [property: JsonIgnore] byte[]? ResponseBody,
    int? StatusCode,
    string? Model,
    string? GenerationId,
    RecordedUsage? Usage,
    string? RawUsageJson,
    string? Error,
    bool BudgetRejected,
    RecordedAttemptFiles? CaptureFiles)
{
    /// <summary>Gets the client-submitted model when available.</summary>
    public string? RequestedModel { get; init; }

    /// <summary>Gets the provider-reported returned model when available.</summary>
    public string? ReturnedModel { get; init; }

    /// <summary>Gets the provider-reported endpoint name when available.</summary>
    public string? ReturnedProvider { get; init; }

    /// <summary>Gets measured tool payload sizes visible in this request's submitted history.</summary>
    public IReadOnlyList<RecordedToolObservation> Tools { get; init; } = [];
}

/// <summary>Represents relative links to exact per-attempt body captures.</summary>
/// <param name="ClientRequest">The request body as received from the child.</param>
/// <param name="EffectiveRequest">The normalized body forwarded to the provider.</param>
/// <param name="Response">The response body as returned to the child, when any bytes arrived.</param>
internal sealed record RecordedAttemptFiles(string ClientRequest, string EffectiveRequest, string? Response)
{
    /// <summary>Gets the relative field-level normalization record.</summary>
    public string? Normalization { get; init; }
}
