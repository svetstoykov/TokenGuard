using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Provides strict report JSON serialization settings.
/// </summary>
/// <remarks>Serialization uses camel-case properties and rejects unknown report fields.</remarks>
internal static class ReportJson
{
    /// <summary>
    ///     Gets the strict camel-case report JSON settings.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
