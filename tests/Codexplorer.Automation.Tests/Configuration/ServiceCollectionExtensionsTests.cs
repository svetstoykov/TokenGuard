extern alias sample;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using sample::Codexplorer.Configuration;

namespace Codexplorer.Automation.Tests.Configuration;

public sealed class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureServices_WithACustomCounter_UsesItForResolvedServicesAndCreatedContexts(bool registerAfter)
    {
        // Arrange
        var counter = new FixedCounter();
        var services = new ServiceCollection();
        if (!registerAfter)
            services.AddSingleton<ITokenCounter>(counter);
        var settings = new Dictionary<string, string?> { ["Codexplorer:LlmSummarization:Enabled"] = "false" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        services.ConfigureServices(configuration);
        if (registerAfter)
            services.AddSingleton<ITokenCounter>(counter);
        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<IConversationContextFactory>().Create();
        context.AddUserMessage("Read the release notes.");

        // Act
        var prepared = await context.PrepareAsync();

        // Assert
        provider.GetRequiredService<ITokenCounter>().Should().BeSameAs(counter);
        prepared.TokensAfterCompaction.Should().Be(123);
        prepared.Messages.Should().ContainSingle().Which.TokenCount.Should().Be(123);
    }

    /// <summary>Represents a distinctive counting scale for container override regression coverage.</summary>
    private sealed class FixedCounter : ITokenCounter
    {
        /// <inheritdoc />
        public int Count(ContextMessage contextMessage) => 123;

        /// <inheritdoc />
        public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(this.Count);
    }
}
