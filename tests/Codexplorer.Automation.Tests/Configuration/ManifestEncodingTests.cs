using Codexplorer.Automation.Scoring;
using System.Security.Cryptography;
using System.Text;
using Codexplorer.Automation.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies UTF-8 encoding preserves the executed manifest's provenance.</summary>
public sealed class ManifestEncodingTests
{
    /// <summary>Verifies a UTF-8 byte-order mark is accepted and included in the byte hash.</summary>
    [Fact]
    public void LoadSnapshot_Utf8ByteOrderMark_HashesTheOriginalFileBytes()
    {
        var path = Path.GetTempFileName();
        const string manifest = """
            {"tasks":[{"taskId":"bom-task","title":"Task","repositoryUrl":"https://github.com/example/repo",
            "initialPrompt":"Write notes with the artifact tools."}]}
            """;
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(manifest)).ToArray();
        File.WriteAllBytes(path, bytes);
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
        try
        {
            var snapshot = loader.LoadSnapshot();

            snapshot.Tasks.Single().TaskId.Should().Be("bom-task");
            snapshot.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
