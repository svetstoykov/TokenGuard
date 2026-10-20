using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Summarization;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class CustomConfigurationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_CustomStrategyWithBuiltInOptions_Throws(bool summarization, bool customFirst)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000);
        if (customFirst)
            builder.WithCompactionStrategy(_ => StubCompactionStrategy.Unchanged());
        if (summarization)
            builder.UseLlmSummarization(new EmptySummarizer());
        else
            builder.WithSlidingWindowOptions(SlidingWindowOptions.Default);
        if (!customFirst)
            builder.WithCompactionStrategy(_ => StubCompactionStrategy.Unchanged());

        // Act
        var act = () => builder.Build();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*WithCompactionStrategy*WithSlidingWindowOptions*UseLlmSummarization*");
    }

    [Fact]
    public void WithCompactionStrategy_SecondRegistrationReplacesFirst()
    {
        // Arrange
        var names = new List<string>();
        var configuration = new ConversationConfigBuilder().WithMaxTokens(1_000)
            .WithCompactionStrategy(_ => throw new InvalidOperationException("Replaced factory must not run."))
            .WithCompactionStrategy(context =>
            {
                names.Add(context.ContextName);
                context.LoggerFactory.Should().BeSameAs(NullLoggerFactory.Instance);
                return StubCompactionStrategy.Unchanged();
            }).Build();

        // Act
        using var context = new ConversationContextFactory(configuration).Create();

        // Assert
        names.Should().Equal("default");
    }

    [Fact]
    public void WithTokenCounter_Null_ThrowsArgumentNullException()
    {
        var act = () => new ConversationConfigBuilder().WithTokenCounter(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("tokenCounter");
    }

    [Fact]
    public void WithCompactionStrategy_Null_ThrowsArgumentNullException()
    {
        var act = () => new ConversationConfigBuilder().WithCompactionStrategy(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("strategyFactory");
    }

    [Fact]
    public void WithTokenCounter_SharedInstanceServesEachCreatedContext()
    {
        // Arrange
        var counter = new TextLengthTokenCounter();
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000).WithTokenCounter(new EstimatedTokenCounter());
        var configuration = builder.WithTokenCounter(counter).Build();
        var factory = new ConversationContextFactory(configuration);
        using var first = factory.Create();
        using var second = factory.Create();

        // Act
        first.AddUserMessage("first");
        second.AddUserMessage("second");

        // Assert
        first.History.Single().TokenCount.Should().Be(5);
        second.History.Single().TokenCount.Should().Be(6);
    }

    /// <summary>Represents a summarizer boundary for conflict validation.</summary>
    private sealed class EmptySummarizer : ILlmSummarizer
    {
        /// <inheritdoc />
        public Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SummarizerResponse("Summary", SummarizerFinishReason.Completed));
    }
}
