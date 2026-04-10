using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.TestCommon.Tools;

namespace TokenGuard.E2E.OpenAI;

public sealed class OpenRouterAgentLoopE2ETests
{
    private const string ConversationName = "openrouter-e2e";
    private const string ModelName = "qwen/qwen3.6-plus";
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    [Fact]
    [Trait("Category", "E2E")]
    public async Task AgentLoop_WhenTaskRequiresRealToolWork_CompactsContextAndCompletesTask()
    {
        // Arrange
        var apiKey = RequireOpenRouterApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        using var workspace = CreateWorkspace();
        var workspaceDirectory = workspace.DirectoryPath;
        await SeedWorkspaceAsync(workspaceDirectory);

        var chatClient = CreateChatClient(apiKey);
        var tools = CreateTools(workspaceDirectory);
        var chatOptions = CreateChatOptions(tools);

        var services = new ServiceCollection();
        services.AddConversationContext(ConversationName, builder => builder
            .WithMaxTokens(2_200)
            .WithCompactionThreshold(0.55));

        using var serviceProvider = services.BuildServiceProvider();
        using var conversationContext = serviceProvider
            .GetRequiredService<IConversationContextFactory>()
            .Create(ConversationName);

        conversationContext.SetSystemPrompt(
            "You are running inside a TokenGuard E2E test. " +
            "Fully complete the assigned workspace task using the available tools. " +
            "You must inspect the workspace before changing files, use real tool calls, and finish only when the task is done. " +
            "When the task is complete, respond with exactly three bullet points. " +
            "The final bullet must be 'TASK_COMPLETE'.");

        conversationContext.AddUserMessage(
            "Task: inspect the workspace, read TODO.txt, create summary.txt with a concise summary of the requirements, " +
            "then edit draft.txt so it contains exactly the final answer requested by TODO.txt. " +
            "Use tools for the file operations and do not claim completion until both files are correct.");

        var toolMap = tools.ToDictionary(static tool => tool.Name, static tool => tool);
        var toolExecutions = 0;
        var observedCompaction = false;
        var completed = false;
        string? finalResponseText = null;

        // Act
        for (var iteration = 0; iteration < 8 && !completed; iteration++)
        {
            var preparedMessages = await conversationContext.PrepareAsync();
            observedCompaction |= preparedMessages.Any(message => message.State == CompactionState.Masked);

            var response = await chatClient.CompleteChatAsync(preparedMessages.ForOpenAI(), chatOptions);
            var completion = response.Value;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                conversationContext.RecordModelResponse(completion.ResponseSegments(), completion.InputTokens());

                foreach (var call in completion.ToolCalls)
                {
                    toolExecutions++;

                    var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    conversationContext.RecordToolResult(call.Id, call.FunctionName, resultText);
                }

                continue;
            }

            var textSegments = completion.TextSegments();
            conversationContext.RecordModelResponse(textSegments, completion.InputTokens());
            finalResponseText = string.Join(Environment.NewLine, textSegments.Select(segment => segment.Text));
            completed = finalResponseText.Contains("TASK_COMPLETE", StringComparison.Ordinal);

            if (!completed)
            {
                conversationContext.AddUserMessage(
                    "The task is not complete yet. Continue using tools until both workspace files satisfy the instructions. " +
                    "Only the final completion message may contain TASK_COMPLETE.");
            }
        }

        var finalPreparedMessages = await conversationContext.PrepareAsync();
        observedCompaction |= finalPreparedMessages.Any(message => message.State == CompactionState.Masked);

        var summaryPath = Path.Combine(workspaceDirectory, "summary.txt");
        var draftPath = Path.Combine(workspaceDirectory, "draft.txt");
        var summaryContents = await File.ReadAllTextAsync(summaryPath);
        var draftContents = await File.ReadAllTextAsync(draftPath);

        // Assert
        toolExecutions.Should().BeGreaterThanOrEqualTo(3, because: "the live model should inspect, read, and modify the workspace through actual tool calls");
        observedCompaction.Should().BeTrue(because: "the accumulated tool results and loop turns should force TokenGuard to mask older context before completion");
        completed.Should().BeTrue(because: "the loop should reach the instructed completion marker");
        finalResponseText.Should().NotBeNullOrWhiteSpace();
        finalResponseText.Should().Contain("TASK_COMPLETE", because: "the completion marker is the structural success signal for this E2E loop");
        finalResponseText!.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Should().HaveCount(3, because: "the model is instructed to return exactly three bullets in the final answer");

        summaryContents.Should().Contain("release checklist", because: "the created summary should reflect the TODO requirements");
        summaryContents.Should().Contain("regression coverage", because: "the summary should capture the testing instruction from the task file");
        draftContents.Should().Be(
            "Release checklist:\n- Verify E2E coverage for the tool-driven agent loop.\n- Confirm TokenGuard compaction occurs before the final report.",
            because: "the model should use the edit tool to replace the draft with the requested final content");
    }

    private static ChatClient CreateChatClient(string apiKey)
    {
        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = OpenRouterEndpoint });

        return client.GetChatClient(ModelName);
    }

    private static string? RequireOpenRouterApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return apiKey;
    }

    private static ITool[] CreateTools(string workspaceDirectory) =>
    [
        new ListFilesTool(workspaceDirectory),
        new ReadFileTool(workspaceDirectory),
        new CreateTextFileTool(workspaceDirectory),
        new EditTextFileTool(workspaceDirectory),
    ];

    private static ChatCompletionOptions CreateChatOptions(IEnumerable<ITool> tools)
    {
        var chatOptions = new ChatCompletionOptions();

        foreach (var tool in tools)
        {
            chatOptions.Tools.Add(tool.ParametersSchema is null
                ? ChatTool.CreateFunctionTool(tool.Name, tool.Description)
                : ChatTool.CreateFunctionTool(
                    functionName: tool.Name,
                    functionDescription: tool.Description,
                    functionParameters: BinaryData.FromString(tool.ParametersSchema.RootElement.GetRawText())));
        }

        return chatOptions;
    }

    private static async Task SeedWorkspaceAsync(string workspaceDirectory)
    {
        await File.WriteAllTextAsync(
            Path.Combine(workspaceDirectory, "TODO.txt"),
            "Project: TokenGuard release checklist\n" +
            "\n" +
            "Requirements:\n" +
            "1. Create summary.txt with two short lines that mention the release checklist and regression coverage.\n" +
            "2. Replace the contents of draft.txt with exactly this text:\n" +
            "Release checklist:\n" +
            "- Verify E2E coverage for the tool-driven agent loop.\n" +
            "- Confirm TokenGuard compaction occurs before the final report.\n" +
            "3. Inspect the existing workspace files before editing them.\n");

        await File.WriteAllTextAsync(
            Path.Combine(workspaceDirectory, "draft.txt"),
            "Placeholder draft that still needs to be replaced after the task is understood.\n" +
            string.Join(Environment.NewLine, Enumerable.Range(1, 60).Select(static index =>
                $"Background note {index}: this line exists to expand tool output and exercise compaction during the E2E loop.")));

        await File.WriteAllTextAsync(
            Path.Combine(workspaceDirectory, "notes.txt"),
            string.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(static index =>
                $"Regression note {index}: ensure the release checklist keeps tool-driven execution and compaction behavior aligned.")));
    }

    private static TestWorkspace CreateWorkspace()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "tokenguard-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TestWorkspace(directoryPath);
    }

    private sealed record TestWorkspace(string DirectoryPath) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
