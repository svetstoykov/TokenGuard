using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;

namespace TokenGuard.Tests.Diagnostics;

public sealed class LogMessageCatalogTests
{
    [Fact]
    public void EventIds_AreUniqueAcrossTheCoreAssembly()
    {
        // Arrange
        var messages = ReadCatalog();

        // Act
        var duplicates = messages.GroupBy(message => message.EventId).Where(group => group.Count() > 1).Select(group => group.Key);

        // Assert
        messages.Should().NotBeEmpty();
        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void EventNames_AreSetAndUniqueAcrossTheCoreAssembly()
    {
        // Arrange
        var messages = ReadCatalog();

        // Act
        var names = messages.Select(message => message.EventName).ToArray();

        // Assert
        names.Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name));
        names.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    ///     Reads every <see cref="LoggerMessageAttribute" /> declared in the <c>TokenGuard.Core</c> assembly.
    /// </summary>
    /// <returns>The declared log message attributes.</returns>
    internal static IReadOnlyList<LoggerMessageAttribute> ReadCatalog() =>
        typeof(ConversationContext).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .ToArray();
}
