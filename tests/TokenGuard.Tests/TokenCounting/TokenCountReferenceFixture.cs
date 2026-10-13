using System.Text.Json;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.TokenCounting;

/// <summary>
///     Provides the samples and tolerances of <c>EstimatedTokenCounterReferenceCounts.json</c>.
/// </summary>
/// <remarks>
///     The file is copied next to the test assembly. Its header names the tokenizer that produced the counts and the way
///     to record them again.
/// </remarks>
internal static class TokenCountReferenceFixture
{
    private const string FileName = "EstimatedTokenCounterReferenceCounts.json";

    private static readonly Lazy<(IReadOnlyList<TokenCountReferenceSample> Samples, IReadOnlyDictionary<string, TokenCountTolerance> Tolerances)>
        Loaded = new(Load);

    /// <summary>
    ///     Gets every sample of the fixture in file order.
    /// </summary>
    public static IReadOnlyList<TokenCountReferenceSample> Samples => Loaded.Value.Samples;

    /// <summary>
    ///     Gets the tolerance of each text kind, keyed by kind.
    /// </summary>
    public static IReadOnlyDictionary<string, TokenCountTolerance> Tolerances => Loaded.Value.Tolerances;

    /// <summary>
    ///     Gets the sample with the given name.
    /// </summary>
    /// <param name="id">The sample name.</param>
    /// <returns>The sample.</returns>
    public static TokenCountReferenceSample Sample(string id) => Samples.Single(sample => sample.Id == id);

    private static (IReadOnlyList<TokenCountReferenceSample>, IReadOnlyDictionary<string, TokenCountTolerance>) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TokenCounting", FileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var tolerances = root.GetProperty("tolerances").EnumerateObject()
            .ToDictionary(property => property.Name, property => ReadTolerance(property.Value));

        var samples = root.GetProperty("samples").EnumerateArray().Select(ReadSample).ToArray();

        return (samples, tolerances);
    }

    private static TokenCountTolerance ReadTolerance(JsonElement tolerance)
    {
        var over = tolerance.GetProperty("over");

        return new TokenCountTolerance(tolerance.GetProperty("under").GetDouble(), over.ValueKind == JsonValueKind.Null ? null : over.GetDouble());
    }

    private static TokenCountReferenceSample ReadSample(JsonElement sample)
    {
        var message = new ContextMessage
        {
            Role = ReadRole(sample.GetProperty("role").GetString()!),
            Segments = sample.GetProperty("segments").EnumerateArray().Select(ReadSegment).ToArray(),
        };

        var id = sample.GetProperty("id").GetString()!;
        var kind = sample.GetProperty("kind").GetString()!;

        return new TokenCountReferenceSample(id, kind, message, sample.GetProperty("referenceTokens").GetInt32());
    }

    private static MessageRole ReadRole(string role) =>
        role switch
        {
            "user" => MessageRole.User,
            "assistant" => MessageRole.Model,
            "tool" => MessageRole.Tool,
            _ => throw new InvalidDataException($"The fixture names the unknown role '{role}'."),
        };

    private static ContentSegment ReadSegment(JsonElement segment)
    {
        var content = segment.GetProperty("content").GetString()!;
        var type = segment.GetProperty("type").GetString();

        return type switch
        {
            "text" => new TextContent(content),
            "toolUse" => new ToolUseContent(ReadString(segment, "toolCallId"), ReadString(segment, "toolName"), content),
            "toolResult" => new ToolResultContent(ReadString(segment, "toolCallId"), ReadString(segment, "toolName"), content),
            "custom" => new CustomContent(content),
            _ => throw new InvalidDataException($"The fixture names the unknown segment type '{type}'."),
        };
    }

    private static string ReadString(JsonElement element, string propertyName) => element.GetProperty(propertyName).GetString()!;

    /// <summary>
    ///     Represents a segment type that the estimator has no dedicated rule for.
    /// </summary>
    /// <param name="Content">The segment text.</param>
    private sealed record CustomContent(string Content) : ContentSegment(Content);
}
