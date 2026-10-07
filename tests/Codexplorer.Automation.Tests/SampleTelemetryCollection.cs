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

/// <summary>Serializes sample tests that observe process-wide TokenGuard telemetry.</summary>
[CollectionDefinition("Sample telemetry", DisableParallelization = true)]
public sealed class SampleTelemetryCollection;
