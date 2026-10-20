extern alias sample;

using FluentAssertions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using sample::Codexplorer.Diagnostics;
using sample::Codexplorer.Measurements;

namespace Codexplorer.Automation.Tests;

/// <summary>Verifies passive measurement identifies actual tool carriers and contamination.</summary>
public sealed class SampleMaskedResultProbeTests : IDisposable
{
    private readonly string _artifacts = Path.Combine(Path.GetTempPath(), "tg-probe-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this._artifacts))
            Directory.Delete(this._artifacts, true);
    }

    /// <summary>Verifies carrier identity distinguishes masking, removal, and remaining visibility.</summary>
    /// <param name="mode">The prepared view transformation.</param>
    /// <param name="state">The expected carrier state.</param>
    /// <param name="elsewhere">Whether another copy survives.</param>
    [Theory]
    [InlineData("masked", "masked", false)]
    [InlineData("visible", "visible", false)]
    [InlineData("removed", "removed", false)]
    [InlineData("summary", "removed", true)]
    [InlineData("otherTool", "masked", true)]
    [InlineData("arguments", "masked", true)]
    [InlineData("placeholderContainsFact", "masked", true)]
    public void ObservePrepare_TracksExposedCarriers(string mode, string state, bool elsewhere)
    {
        var tracker = new MaskedResultProbeTracker(["TG-UNIQUE", "TG-ALIAS"]);
        var carrier = Result("one", "**TG-UNIQUE**");
        tracker.ObservePrepare(new PrepareMeasurement(), [carrier], this._artifacts).ProbeValueState.Should().Be("visible");
        var masked = carrier with { State = CompactionState.Masked, Segments = [new ToolResultContent("one", "read_file", "[masked]")] };
        IReadOnlyList<ContextMessage> input = mode switch
        {
            "visible" => [carrier],
            "removed" => [],
            "summary" => [new ContextMessage { Role = MessageRole.System, Segments = [new TextContent("TG-ALIAS")] }],
            "otherTool" => [masked, Result("two", "TG-ALIAS")],
            "arguments" => [masked, new ContextMessage
            {
                Role = MessageRole.Model, Segments = [new ToolUseContent("two", "search", "{\"value\":\"TG-ALIAS\"}")]
            }],
            "placeholderContainsFact" => [carrier with { State = CompactionState.Masked }],
            _ => [masked]
        };

        var evidence = tracker.ObservePrepare(new PrepareMeasurement(), input, this._artifacts);

        evidence.ProbeValueState.Should().Be(state);
        evidence.ProbeFactVisibleElsewhere.Should().Be(elsewhere);
    }

    /// <summary>Verifies a result masked before any exposed prepare does not qualify as read.</summary>
    [Fact]
    public void ObservePrepare_NeverExposedCarrier_IsUnseen()
    {
        var tracker = new MaskedResultProbeTracker(["TG-UNIQUE"]);

        var evidence = tracker.ObservePrepare(new PrepareMeasurement(),
            [Result("one", "[masked]") with { State = CompactionState.Masked }], this._artifacts);

        evidence.ProbeValueState.Should().Be("unseen");
    }

    /// <summary>Verifies earlier assistant text and nested artifacts invalidate otherwise masked facts.</summary>
    /// <param name="source">The location of the contaminating copy.</param>
    [Theory]
    [InlineData("assistant")]
    [InlineData("artifact")]
    public void ObservePrepare_CopiedFact_RecordsContamination(string source)
    {
        var tracker = new MaskedResultProbeTracker(["TG-UNIQUE"]);
        tracker.ObservePrepare(new PrepareMeasurement(), [Result("one", "TG-UNIQUE")], this._artifacts);
        if (source == "assistant")
            tracker.ObserveAssistant("**TG-UNIQUE**");
        else
        {
            Directory.CreateDirectory(Path.Combine(this._artifacts, "nested"));
            File.WriteAllText(Path.Combine(this._artifacts, "nested", "notes.md"), "TG-UNIQUE");
        }

        var evidence = tracker.ObservePrepare(new PrepareMeasurement(),
            [Result("one", "[masked]") with { State = CompactionState.Masked }], this._artifacts);

        evidence.ProbeFactVisibleElsewhere.Should().BeTrue();
    }

    /// <summary>Verifies answering the question does not count as earlier assistant repetition.</summary>
    [Fact]
    public void Question_PostQuestionAnswerAndToolRefetch_RecordSeparateEvidence()
    {
        var tracker = new MaskedResultProbeTracker(["TG-UNIQUE"]);
        tracker.ObservePrepare(new PrepareMeasurement(), [Result("one", "TG-UNIQUE")], this._artifacts);
        tracker.ObserveToolResult("TG-UNIQUE");
        tracker.BeginQuestion();
        tracker.ObserveAssistant("TG-UNIQUE");
        var before = tracker.ObservePrepare(new PrepareMeasurement(), [], this._artifacts);

        tracker.ObserveToolResult("TG-UNIQUE");
        var after = tracker.ObservePrepare(new PrepareMeasurement(), [], this._artifacts);

        before.ProbeQuestionAsked.Should().BeTrue();
        before.ProbeRefetched.Should().BeFalse();
        before.ProbeFactVisibleElsewhere.Should().BeFalse();
        after.ProbeRefetched.Should().BeTrue();
        before.ProbeRefetched.Should().BeFalse();
    }

    private static ContextMessage Result(string id, string text) => new()
    {
        Role = MessageRole.Tool, Segments = [new ToolResultContent(id, "read_file", text)]
    };
}
