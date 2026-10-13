using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Tests.TokenCounting;

/// <summary>
///     Checks the estimator against token counts recorded from a real tokenizer.
/// </summary>
/// <remarks>
///     Every expected value comes from <see cref="TokenCountReferenceFixture" />. The fixture states a tolerance for each
///     text kind; an estimate outside it is a failure.
/// </remarks>
public class EstimatedTokenCounterTests
{
    private readonly EstimatedTokenCounter _counter = new();

    /// <summary>
    ///     Gets the name of every sample in the reference fixture.
    /// </summary>
    public static TheoryData<string> SampleIds => new(TokenCountReferenceFixture.Samples.Select(sample => sample.Id));

    /// <summary>
    ///     Gets every text kind in the reference fixture.
    /// </summary>
    public static TheoryData<string> Kinds => new(TokenCountReferenceFixture.Samples.Select(sample => sample.Kind).Distinct());

    [Theory]
    [MemberData(nameof(SampleIds))]
    public void Count_ForAReferenceSample_IsWithinTheToleranceOfItsKind(string sampleId)
    {
        // Arrange
        var sample = TokenCountReferenceFixture.Sample(sampleId);
        var tolerance = TokenCountReferenceFixture.Tolerances[sample.Kind];

        // Act
        var estimate = this._counter.Count(sample.Message);

        // Assert
        var reference = sample.ReferenceTokens;
        Assert.True(tolerance.Allows(estimate, reference), Describe(sample.Id, sample.Kind, estimate, reference, tolerance));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Count_ForEveryReferenceSampleOfAKindTogether_IsWithinTheToleranceOfTheirSummedReferenceCounts(string kind)
    {
        // Arrange
        var samples = TokenCountReferenceFixture.Samples.Where(sample => sample.Kind == kind).ToArray();
        var reference = samples.Sum(sample => sample.ReferenceTokens);
        var tolerance = TokenCountReferenceFixture.Tolerances[kind];

        // Act
        var estimate = this._counter.Count(samples.Select(sample => sample.Message));

        // Assert
        Assert.True(tolerance.Allows(estimate, reference), Describe("all samples", kind, estimate, reference, tolerance));
    }

    [Theory]
    [InlineData(TokenCountSafetyMode.Balanced, 1.00)]
    [InlineData(TokenCountSafetyMode.Safe, 1.05)]
    [InlineData(TokenCountSafetyMode.Conservative, 1.10)]
    [InlineData((TokenCountSafetyMode)99, 1.05)]
    public void Count_WithASafetyMode_IsWithinTheToleranceOfTheReferenceCountRaisedByTheMarginOfThatMode(TokenCountSafetyMode mode, double margin)
    {
        // Arrange
        var counter = new EstimatedTokenCounter(mode);

        // Act
        var outside = TokenCountReferenceFixture.Samples
            .Select(sample => (Sample: sample, Estimate: counter.Count(sample.Message), Reference: sample.ReferenceTokens * margin))
            .Where(counted => !TokenCountReferenceFixture.Tolerances[counted.Sample.Kind].Allows(counted.Estimate, counted.Reference, slackTokens: 1))
            .Select(counted => $"{counted.Sample.Id}: estimate {counted.Estimate}, reference with margin {counted.Reference:F1}")
            .ToArray();

        // Assert
        Assert.Empty(outside);
    }

    [Fact]
    public void Count_ForAnEmptyMessageList_ReturnsZero()
    {
        // Arrange
        var messages = Array.Empty<ContextMessage>();

        // Act
        var result = this._counter.Count(messages);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void Count_WhenTheMessageIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => this._counter.Count((ContextMessage)null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Count_WhenTheMessageListIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => this._counter.Count((IEnumerable<ContextMessage>)null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void MessageWithTokenCountSet_ReturnsCachedValue()
    {
        // Arrange
        var message = new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new TextContent("Some text")],
            TokenCount = 100,
        };

        // Act
        var result = this._counter.Count(message);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void MessageWithTokenCountSet_DoesNotApplySafetyMargin()
    {
        // Arrange
        var counter = new EstimatedTokenCounter(TokenCountSafetyMode.Conservative);
        var message = new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new TextContent("Some text")],
            TokenCount = 100,
        };

        // Act
        var result = counter.Count(message);

        // Assert
        Assert.Equal(100, result);
    }

    private static string Describe(string sampleId, string kind, int estimate, int reference, TokenCountTolerance tolerance) =>
        $"{sampleId} ({kind}): estimate {estimate}, reference {reference}, error {TokenCountTolerance.Error(estimate, reference):+0.0%;-0.0%}, "
        + $"tolerance {tolerance}.";
}
