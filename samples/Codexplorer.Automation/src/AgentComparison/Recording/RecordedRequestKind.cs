using System.Text.Json.Serialization;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Defines model-request categories used for call budgeting.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecordedRequestKind>))]
internal enum RecordedRequestKind
{
    /// <summary>A main agent request charged to its allowance.</summary>
    MainAgent,

    /// <summary>A reserved wrap-up request.</summary>
    WrapUp,

    /// <summary>A positively identified context-compaction request.</summary>
    Compaction,

    /// <summary>A positively identified title-generation request.</summary>
    Title,

    /// <summary>A positively identified helper request.</summary>
    Helper,

    /// <summary>A request whose role is unknown and is charged to the main allowance.</summary>
    Unknown,
}
