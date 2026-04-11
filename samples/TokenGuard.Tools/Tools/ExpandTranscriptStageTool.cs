using System.Text;
using System.Text.Json;

namespace TokenGuard.Tools.Tools;

/// <summary>
/// Expands a compact stage recipe into a large deterministic transcript chunk inside the task workspace.
/// </summary>
/// <remarks>
/// This tool lets E2E tasks seed small control files while still forcing the live model to read and reason over
/// substantial back-and-forth artefacts. The generated content is deterministic so assertions remain stable.
/// </remarks>
public sealed class ExpandTranscriptStageTool(string workspaceDirectory) : ITool
{
    /// <summary>
    /// Gets tool name exposed to model.
    /// </summary>
    public string Name => "expand_transcript_stage";

    /// <summary>
    /// Gets tool description surfaced to model.
    /// </summary>
    public string Description => "Expands a compact stage recipe into a large deterministic transcript file in the task workspace.";

    /// <summary>
    /// Gets JSON schema for accepted arguments.
    /// </summary>
    public JsonDocument? ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "recipeFile": { "type": "string", "description": "Relative path to the JSON recipe file inside the task workspace." },
                "outputFile": { "type": "string", "description": "Relative path for the generated transcript file." }
            },
            "required": ["recipeFile", "outputFile"],
            "additionalProperties": false
        }
        """);

    /// <summary>
    /// Generates transcript chunk from recipe file.
    /// </summary>
    /// <param name="argumentsJson">Raw JSON arguments emitted by model.</param>
    /// <returns>Status text describing generated output.</returns>
    public string Execute(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (!doc.RootElement.TryGetProperty("recipeFile", out var recipeFileProperty))
            {
                return "Error: Missing 'recipeFile' argument.";
            }

            if (!doc.RootElement.TryGetProperty("outputFile", out var outputFileProperty))
            {
                return "Error: Missing 'outputFile' argument.";
            }

            var recipeFile = recipeFileProperty.GetString();
            var outputFile = outputFileProperty.GetString();

            if (string.IsNullOrWhiteSpace(recipeFile))
            {
                return "Error: 'recipeFile' cannot be empty.";
            }

            if (string.IsNullOrWhiteSpace(outputFile))
            {
                return "Error: 'outputFile' cannot be empty.";
            }

            var recipePath = WorkspacePathResolver.Resolve(workspaceDirectory, recipeFile);
            if (!File.Exists(recipePath))
            {
                return $"File not found: {recipeFile}";
            }

            var outputPath = WorkspacePathResolver.Resolve(workspaceDirectory, outputFile);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var recipeJson = File.ReadAllText(recipePath);
            var recipe = JsonSerializer.Deserialize<TranscriptStageRecipe>(recipeJson, SerializerOptions);
            if (recipe is null)
            {
                return "Error: Could not deserialize transcript stage recipe.";
            }

            var transcript = BuildTranscript(recipe);
            File.WriteAllText(outputPath, transcript);

            return $"Generated transcript stage '{recipe.StageId}' at {outputFile}";
        }
        catch (Exception ex)
        {
            return $"Error expanding transcript stage: {ex.Message}";
        }
    }

    private static string BuildTranscript(TranscriptStageRecipe recipe)
    {
        var iterationCount = Math.Max(recipe.IterationCount, 1);
        var debateCount = Math.Max(recipe.DebatePointCount, 1);
        var contextLines = recipe.ContextLines?.Count > 0
            ? recipe.ContextLines
            : ["No additional context supplied."];
        var codeTargets = recipe.CodeTargets?.Count > 0
            ? recipe.CodeTargets
            : ["workspace/unknown.cs"];

        var builder = new StringBuilder(capacity: iterationCount * 900);
        builder.AppendLine($"# Stage {recipe.StageId}: {recipe.Title}");
        builder.AppendLine();
        builder.AppendLine($"Token Target Hint: {recipe.TokenTargetHint}");
        builder.AppendLine($"Primary Objective: {recipe.PrimaryObjective}");
        builder.AppendLine($"Escalation Theme: {recipe.EscalationTheme}");
        builder.AppendLine();
        builder.AppendLine("## Shared Context");

        foreach (var line in contextLines)
        {
            builder.AppendLine($"- {line}");
        }

        builder.AppendLine();
        builder.AppendLine("## Back-and-Forth Transcript");
        builder.AppendLine();

        for (var cycle = 1; cycle <= iterationCount; cycle++)
        {
            var target = codeTargets[(cycle - 1) % codeTargets.Count];
            builder.AppendLine($"### Cycle {cycle:000}");
            builder.AppendLine($"Developer: Re-open {target}. Reconcile current implementation with previous review notes before any patch is proposed. Capture exact failure mode, then list constraints that would break if the fix is too narrow.");
            builder.AppendLine($"Agent: I inspected {target} again. Current hypothesis {cycle}: issue spans control flow, error handling, and state propagation. I will compare implementation, tests, and CI evidence before drafting change plan.");

            for (var point = 1; point <= debateCount; point++)
            {
                builder.AppendLine($"Developer: Debate point {cycle:000}.{point:00} - prove whether regression thread '{recipe.EscalationTheme}' is caused by stale assumptions, missing guards, or conflicting acceptance criteria. Tie answer to concrete evidence and state what still needs re-checking.");
                builder.AppendLine($"Agent: Evidence pass {cycle:000}.{point:00} - stale assumption risk remains because prior patch narrowed behaviour around {target}; missing guard risk persists under stress path {point}; acceptance criteria conflict appears between reviewer note {point} and CI signal {cycle}. Need another read of generated artefacts before locking fix scope.");
            }

            builder.AppendLine($"Reviewer: Your last conclusion still under-specifies blast radius. Re-read dependency boundaries touching {target}, describe downstream invariants, then update remediation proposal with rollback and verification coverage.");
            builder.AppendLine($"Agent: Updated plan for cycle {cycle:000} - patch minimal surface first, preserve public contract, add verification steps for parser, coordinator, retry path, and reporting output. I will append artefact deltas after next inspection round.");
            builder.AppendLine();
        }

        builder.AppendLine("## Required Outputs Reminder");
        builder.AppendLine("- Summarize findings in structured artefacts, not free-form completion prose.");
        builder.AppendLine("- Re-read generated outputs after each major write.");
        builder.AppendLine("- Treat each cycle as additive: do not discard earlier contradictions until explicitly resolved.");

        return builder.ToString();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record TranscriptStageRecipe(
        string StageId,
        string Title,
        string PrimaryObjective,
        string EscalationTheme,
        string TokenTargetHint,
        int IterationCount,
        int DebatePointCount,
        IReadOnlyList<string>? CodeTargets,
        IReadOnlyList<string>? ContextLines);
}
