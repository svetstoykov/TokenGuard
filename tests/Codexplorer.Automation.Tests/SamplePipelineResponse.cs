extern alias sample;

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Serilog;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using sample::Codexplorer.Diagnostics;

namespace Codexplorer.Automation.Tests;

/// <summary>Supplies the raw HTTP response required by SDK test completions.</summary>
internal sealed class SamplePipelineResponse : PipelineResponse
{
    /// <inheritdoc />
    public override int Status => 200;

    /// <inheritdoc />
    public override string ReasonPhrase => "OK";

    /// <inheritdoc />
    public override Stream? ContentStream { get; set; }

    /// <inheritdoc />
    public override BinaryData Content => BinaryData.FromString("{}");

    /// <inheritdoc />
    protected override PipelineResponseHeaders HeadersCore => null!;

    /// <inheritdoc />
    protected override bool IsErrorCore { get; set; }

    /// <inheritdoc />
    public override BinaryData BufferContent(CancellationToken cancellationToken) => this.Content;

    /// <inheritdoc />
    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(this.Content);

    /// <inheritdoc />
    public override void Dispose() => this.ContentStream?.Dispose();
}
