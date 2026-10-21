using Codexplorer.Automation.AgentComparison;
using FluentAssertions;
using System.Security.Cryptography;

namespace Codexplorer.Automation.Tests.AgentComparison.Preparation;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class ComparisonBundleInventoryTests
{
    /// <summary>Verifies files uses one secret safe filter and digest is canonical.</summary>
    [Fact]
    public void Files_UsesOneSecretSafeFilterAndDigestIsCanonical()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tokenguard-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "app.dll"), "runtime");
            Directory.CreateDirectory(Path.Combine(root, "runtime"));
            File.WriteAllText(Path.Combine(root, "runtime", "appsettings.json"), "{} ");
            File.WriteAllText(Path.Combine(root, "appsettings.Development.json"), "secret");
            File.WriteAllText(Path.Combine(root, "token.secret.json"), "secret");
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(root, "logs", "app.log"), "log");
            Directory.CreateDirectory(Path.Combine(root, ".hidden"));
            File.WriteAllText(Path.Combine(root, ".hidden", "ignored"), "hidden");

            var files = ComparisonBundleInventory.Files(root);
            ComparisonBundleInventory.HasExcludedEntries(root).Should().BeTrue();
            var expected = new[]
            {
                new FileDigest("app.dll", Sha256("runtime")),
                new FileDigest(Path.Combine("runtime", "appsettings.json"), Sha256("{} "))
            };

            files.Should().Equal(expected);
            ComparisonBundleInventory.Digest(files)
                .Should().Be(ComparisonBundleInventory.Digest(files.Reverse().ToArray()));
            ComparisonBundleInventory.Digest(files)
                .Should().NotBe(ComparisonBundleInventory.Digest([new FileDigest("app.dll", Sha256("changed"))]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
