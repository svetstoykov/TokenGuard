using Codexplorer.Automation.Scoring;
using Codexplorer.Measurements;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Verifies probe eligibility uses the greatest completed prepare index.</summary>
public sealed class ProbeValidityTests
{
    /// <summary>Verifies counters cannot substitute for opening-message evidence.</summary>
    [Fact]
    public void NoCompletedPrepare_IsInvalidRegardlessOfCounters()
    {
        ProbeValidity.GetInvalidReason(null, new SessionMeasurements { MessagesDropped = 99 }).Should().Be("instructionNotCompacted");
    }

    /// <summary>Verifies incomplete and out-of-order records do not hide the last completed evidence.</summary>
    /// <param name="kind">The required kind.</param>
    [Theory]
    [InlineData("masked")]
    [InlineData("summarized")]
    [InlineData("dropped")]
    [InlineData(null)]
    public void LastCompletedPrepare_DeterminesEligibility(string? kind)
    {
        var measurements = new SessionMeasurements
        {
            MessagesMasked = 1, MessagesSummarized = 1, MessagesDropped = 1,
            PrepareRecords =
            [
                new PrepareMeasurement { Index = 2, Status = "completed", OpeningMessagePresent = false },
                new PrepareMeasurement { Index = 1, Status = "completed", OpeningMessagePresent = true },
                new PrepareMeasurement { Index = 3 },
            ]
        };
        ProbeValidity.GetInvalidReason(kind, measurements).Should().BeNull();
        ProbeValidity.GetInvalidReason(kind, measurements with { MessagesMasked = 0, MessagesSummarized = 0, MessagesDropped = 0 })
            .Should().Be(kind is null ? null : "requiredKindAbsent");
    }
}
