using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextFactoryTests
{
    private const int SmallMaxTokens = 60;

    [Fact]
    public void Constructor_WhenConfigurationIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => _ = new ConversationContextFactory(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_WhenTheBudgetIsTheDefaultValue_ThrowsArgumentException()
    {
        // Arrange
        var configuration = ConversationConfigBuilder.Default() with { Budget = default };

        // Act
        Action act = () => _ = new ConversationContextFactory(configuration);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("config").WithMessage("*default(ContextBudget)*");
    }

    [Fact]
    public void AddNamed_WhenTheBudgetIsTheDefaultValue_ThrowsArgumentException()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default());
        var configuration = ConversationConfigBuilder.Default() with { Budget = default };

        // Act
        Action act = () => factory.AddNamed("profile", configuration);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("config").WithMessage("*default(ContextBudget)*");
    }

    [Fact]
    public void Create_ReturnsContextsThatShareNoHistory()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default());

        // Act
        using var first = factory.Create();
        using var second = factory.Create();
        first.AddUserMessage("recorded in the first context only");

        // Assert
        second.Should().NotBeSameAs(first);
        first.History.Should().ContainSingle();
        second.History.Should().BeEmpty();
    }

    [Fact]
    public void Create_InvokesTheStrategyFactoryWithAFreshTokenCounterOnEachCall()
    {
        // Arrange
        var counters = new List<ITokenCounter>();
        var factory = new ConversationContextFactory(new ConversationContextConfiguration(
            ContextBudget.For(100_000),
            counter =>
            {
                counters.Add(counter);
                return StubCompactionStrategy.Unchanged();
            }));

        // Act
        using var first = factory.Create();
        using var second = factory.Create();

        // Assert
        counters.Should().HaveCount(2);
        counters[0].Should().NotBeSameAs(counters[1]);
    }

    [Fact]
    public void Create_PassesACounterThatCountsLikeTheBuiltInEstimatorToTheStrategyFactory()
    {
        // Arrange
        const string text = "The quick brown fox reads `src/Program.cs` and returns { \"lines\": 42 }.";
        ITokenCounter? supplied = null;
        var factory = new ConversationContextFactory(new ConversationContextConfiguration(
            ContextBudget.For(100_000),
            counter =>
            {
                supplied = counter;
                return StubCompactionStrategy.Unchanged();
            }));

        // Act
        using var context = factory.Create();

        // Assert
        supplied.Should().NotBeNull();
        supplied!.Count(ContextMessage.FromText(MessageRole.User, text))
            .Should().Be(new EstimatedTokenCounter().Count(ContextMessage.FromText(MessageRole.User, text)));
    }

    [Fact]
    public async Task Create_WhenStrategyFactoryIsReplacedOnBuiltConfiguration_PreparesWithTheReplacement()
    {
        // Arrange
        var configuration = ConversationConfigBuilder.Default(SmallMaxTokens) with { StrategyFactory = _ => StubCompactionStrategy.KeepNewest(1) };
        var factory = new ConversationContextFactory(configuration);
        using var context = factory.Create();
        RecordTwoTurns(context);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.Messages.Should().ContainSingle().Which.Should().BeSameAs(context.History[^1]);
    }

    [Fact]
    public async Task Create_GivesEachContextItsOwnSummaryCheckpoint()
    {
        // Arrange
        var summarizer = ScriptedSummarizer.Returning("Earlier turns covered the release notes.");
        var builder = new ConversationConfigBuilder().WithMaxTokens(SmallMaxTokens);
        builder.SetLlmSummarizer(() => summarizer, "Test", new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 40));
        var factory = new ConversationContextFactory(builder.Build());
        using var first = factory.Create();
        using var second = factory.Create();
        RecordTwoTurns(first);
        RecordTwoTurns(second);

        // Act
        var firstResult = await first.PrepareAsync();
        var secondResult = await second.PrepareAsync();

        // Assert
        firstResult.Messages[0].State.Should().Be(CompactionState.Summarized);
        secondResult.Messages[0].State.Should().Be(CompactionState.Summarized);
        summarizer.Calls.Should().Be(2);
    }

    [Fact]
    public async Task CreateNamed_PreparesWithTheConfigurationRegisteredUnderThatName()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default())
            .AddNamed("small", new ConversationConfigBuilder().WithMaxTokens(SmallMaxTokens).Build());
        using var defaultContext = factory.Create();
        using var namedContext = factory.Create("small");
        RecordTwoTurns(defaultContext);
        RecordTwoTurns(namedContext);

        // Act
        var defaultResult = await defaultContext.PrepareAsync();
        var namedResult = await namedContext.PrepareAsync();

        // Assert
        defaultResult.Outcome.Should().Be(PrepareOutcome.Ready);
        defaultResult.Messages.Should().HaveCount(3);
        namedResult.Outcome.Should().Be(PrepareOutcome.Compacted);
        namedResult.MessagesDropped.Should().Be(2);
        namedResult.Messages.Should().ContainSingle().Which.Should().BeSameAs(namedContext.History[^1]);
    }

    [Fact]
    public void CreateNamed_ReturnsContextsThatShareNoHistory()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default())
            .AddNamed("small", new ConversationConfigBuilder().WithMaxTokens(50_000).Build());

        // Act
        using var first = factory.Create("small");
        using var second = factory.Create("small");
        first.AddUserMessage("recorded in the first context only");

        // Assert
        second.Should().NotBeSameAs(first);
        first.History.Should().ContainSingle();
        second.History.Should().BeEmpty();
    }

    [Fact]
    public void CreateNamed_WithUnregisteredName_ThrowsInvalidOperationExceptionNamingIt()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default());

        // Act
        Action act = () => factory.Create("unknown");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown*");
    }

    [Fact]
    public void CreateNamed_WithNameThatDiffersOnlyInCase_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default())
            .AddNamed("analysis", ConversationConfigBuilder.Default());

        // Act
        Action act = () => factory.Create("Analysis");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Analysis*");
    }

    [Fact]
    public async Task AddNamed_WhenTheNameIsAlreadyRegistered_ReplacesTheConfiguration()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default())
            .AddNamed("profile", new ConversationConfigBuilder().WithMaxTokens(SmallMaxTokens).Build())
            .AddNamed("profile", ConversationConfigBuilder.Default());
        using var context = factory.Create("profile");
        RecordTwoTurns(context);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Ready);
    }

    [Fact]
    public void AddNamed_ReturnsTheSameFactory()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default());

        // Act
        var returned = factory.AddNamed("default", ConversationConfigBuilder.Default());

        // Assert
        returned.Should().BeSameAs(factory);
    }

    [Fact]
    public void AddNamed_WhenNameIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default());

        // Act
        Action act = () => factory.AddNamed(null!, ConversationConfigBuilder.Default());

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("name");
    }

    [Fact]
    public void AddNamed_WhenConfigurationIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new ConversationContextFactory(ConversationConfigBuilder.Default());

        // Act
        Action act = () => factory.AddNamed("profile", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    /// <summary>
    ///     Records a user message, a model reply, and a second user message that together exceed the compaction trigger of a
    ///     <see cref="SmallMaxTokens" /> window while the last message alone fits in it.
    /// </summary>
    /// <param name="context">The context to record into.</param>
    private static void RecordTwoTurns(IConversationContext context)
    {
        context.AddUserMessage("Please list every change that went into the last release and say which of them touched the public surface.");
        context.RecordModelResponse(
            [new TextContent("The last release renamed two options, removed one obsolete overload, and added a builder method for logging.")]);
        context.AddUserMessage("Thanks. Which of those three changes would break a caller that upgrades without recompiling?");
    }
}
