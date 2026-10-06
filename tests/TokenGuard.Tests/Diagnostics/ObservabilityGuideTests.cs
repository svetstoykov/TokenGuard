using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using TokenGuard.Core.Diagnostics;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Verifies that the tables in <c>docs/observability.md</c> list exactly the event IDs and instruments in the code.
/// </summary>
public sealed partial class ObservabilityGuideTests
{
    [Fact]
    public void EventIdTable_ListsExactlyTheLogMessagesDeclaredInCode()
    {
        // Arrange
        var declared = LogMessageCatalogTests.ReadCatalog()
            .Select(message => $"{message.EventId} {message.EventName} {message.Level}")
            .Order()
            .ToArray();

        // Act
        var documented = EventRow().Matches(ReadGuide())
            .Select(match => $"{match.Groups["id"].Value} {match.Groups["name"].Value} {match.Groups["level"].Value}")
            .Order()
            .ToArray();

        // Assert
        documented.Should().Equal(declared);
    }

    [Fact]
    public void InstrumentTable_ListsExactlyTheInstrumentsDeclaredInCode()
    {
        // Arrange
        var declared = typeof(TokenGuardTelemetry)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.GetValue(null))
            .OfType<Instrument>()
            .Select(instrument => $"{instrument.Name} {instrument.GetType().Name.Split('`')[0]} {instrument.Unit}")
            .Order()
            .ToArray();

        // Act
        var documented = InstrumentRow().Matches(ReadGuide())
            .Select(match => $"{match.Groups["name"].Value} {match.Groups["type"].Value} {match.Groups["unit"].Value}")
            .Order()
            .ToArray();

        // Assert
        declared.Should().NotBeEmpty();
        documented.Should().Equal(declared);
    }

    [GeneratedRegex(@"^\| (?<id>\d{4}) \| (?<name>\w+) \| (?<level>\w+) \|", RegexOptions.Multiline)]
    private static partial Regex EventRow();

    [GeneratedRegex(@"^\| `(?<name>tokenguard\.[a-z_.]+)` \| (?<type>Counter|Histogram) \| `(?<unit>[^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex InstrumentRow();

    private static string ReadGuide()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run from inside the repository to find docs/observability.md");
        return File.ReadAllText(Path.Combine(directory!.FullName, "docs", "observability.md"));
    }
}
