extern alias sample;

using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using sample::Codexplorer.Agent;
using sample::Codexplorer.Automation;
using sample::Codexplorer.Configuration;
using sample::Codexplorer.Measurements;
using sample::Codexplorer.Sessions;
using sample::Codexplorer.Tools;
using sample::Codexplorer.Workspace;

namespace Codexplorer.Automation.Tests;

/// <summary>Creates real sessions for protocol tests through the automation capability.</summary>
/// <param name="sessionFactory">The session construction operation.</param>
internal sealed class SampleExplorerAgent(Func<int?, IExplorerSession> sessionFactory) : IExplorerAgent, IAutomationExplorerAgent
{
    /// <summary>Gets or sets the session creation operation.</summary>
    public Func<int?, IExplorerSession> SessionFactory { get; set; } = sessionFactory;

    /// <inheritdoc />
    public IExplorerSession StartSession(Workspace workspace) => this.StartAutomationSession(workspace, null);

    /// <inheritdoc />
    public IExplorerSession StartAutomationSession(Workspace workspace, int? modelCallBudget, int? wrapUpWindow = null) =>
        this.SessionFactory(modelCallBudget);
}
