using System.Globalization;
using System.Text.Json;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.Comparison;

/// <summary>
///     Provides report comparison without constructing the automation host.
/// </summary>
/// <remarks>Comparison validates recorded measurements before checking compatibility and regression limits.</remarks>
internal static class ComparisonCommand
{
    /// <summary>
    ///     Asynchronously loads and compares two run reports with optional regression limits.
    /// </summary>
    /// <param name="args">The baseline/candidate paths and comparison options. Cannot be <see langword="null" />.</param>
    /// <param name="output">The destination for invariant comparison output. Cannot be <see langword="null" />.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains zero for success and one for failure.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintHelp(output);
            return 0;
        }

        if (args.Length < 2 || args[0].StartsWith("--", StringComparison.Ordinal) || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            output.WriteLine("Expected baseline and candidate report paths. Use compare --help.");
            return 1;
        }

        var allowIncompatible = false;
        var limits = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var index = 2; index < args.Length; index++)
        {
            if (args[index] == "--allow-incompatible")
            {
                allowIncompatible = true;
                continue;
            }

            if (args[index] != "--limit" || ++index == args.Length || !TryReadLimit(args[index], limits))
            {
                output.WriteLine("Invalid comparison option or limit. Limits require metric=nonnegative-finite-number.");
                return 1;
            }
        }

        RunReport baseline;
        RunReport candidate;
        try
        {
            baseline = await ReadReportAsync(args[0]).ConfigureAwait(false);
            candidate = await ReadReportAsync(args[1]).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException
            or OverflowException or NullReferenceException)
        {
            output.WriteLine("Malformed report JSON, unsupported schema, or invalid required report shape.");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            output.WriteLine("Could not read the report files.");
            return 1;
        }

        IReadOnlyList<string> validationErrors;
        try
        {
            validationErrors = ReportValidator.Validate(baseline).Select(error => "Baseline: " + error)
                .Concat(ReportValidator.Validate(candidate).Select(error => "Candidate: " + error)).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException
            or OverflowException or NullReferenceException)
        {
            output.WriteLine("Invalid or nonfinite report measurements.");
            return 1;
        }

        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                output.WriteLine(error);
            }

            return 1;
        }

        var mismatches = GetCompatibilityMismatches(baseline, candidate);
        foreach (var mismatch in mismatches)
        {
            output.WriteLine("Incompatible: " + mismatch);
        }

        if (mismatches.Count > 0 && !allowIncompatible)
        {
            return 1;
        }

        if (mismatches.Count > 0)
        {
            output.WriteLine("Compatibility override enabled; mismatches above remain applicable.");
        }

        var baselineMetrics = GetTotalsMetrics(baseline);
        var candidateMetrics = GetTotalsMetrics(candidate);
        foreach (var name in baselineMetrics.Keys.Union(candidateMetrics.Keys, StringComparer.Ordinal).ToArray())
        {
            baselineMetrics.TryAdd(name, 0);
            candidateMetrics.TryAdd(name, 0);
        }

        PrintMetrics(output, "totals", baselineMetrics, candidateMetrics);
        var baselineTasks = baseline.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        var candidateTasks = candidate.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        foreach (var id in baselineTasks.Keys.Intersect(candidateTasks.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var previous = baselineTasks[id];
            var current = candidateTasks[id];
            PrintMetrics(output, "task " + id, GetTaskMetrics(previous), GetTaskMetrics(current));
            output.WriteLine($"task {id} outcome: baseline={previous.Outcome} candidate={current.Outcome}");
            output.WriteLine($"task {id} protocolCompletion: baseline={previous.ProtocolCompletion} candidate={current.ProtocolCompletion}");
            output.WriteLine($"task {id} deliverableCompletion: baseline={previous.DeliverableCompletion} candidate={current.DeliverableCompletion}");
            output.WriteLine($"task {id} measurementsComplete: baseline={previous.MeasurementsComplete} candidate={current.MeasurementsComplete}");
            output.WriteLine($"task {id} summaryCrossCheck: baseline={previous.SummaryCrossCheck} candidate={current.SummaryCrossCheck}");
        }

        output.WriteLine("Baseline-only tasks: " + string.Join(", ", baselineTasks.Keys.Except(candidateTasks.Keys).Order(StringComparer.Ordinal)));
        output.WriteLine("Candidate-only tasks: " + string.Join(", ", candidateTasks.Keys.Except(baselineTasks.Keys).Order(StringComparer.Ordinal)));
        output.WriteLine("Estimator error unit: " + baseline.EstimatorErrorUnit);
        PrintMeasuredReduction(output, baseline, candidate, mismatches.Count == 0);
        var failedLimit = false;
        foreach (var (metric, limit) in limits)
        {
            if (metric is "estimatorSignedMean" or "estimatorSignedP95" || !baselineMetrics.TryGetValue(metric, out var previous)
                || !candidateMetrics.TryGetValue(metric, out var current))
            {
                output.WriteLine($"Unknown or informational metric cannot have a regression limit: {metric}.");
                failedLimit = true;
                continue;
            }

            if (previous is null || current is null)
            {
                output.WriteLine($"Regression limit metric is unavailable: {metric}.");
                failedLimit = true;
                continue;
            }

            var regression = metric is "protocolCompletionRate" or "estimatedPromptTokenReduction"
                ? previous.Value - current.Value : current.Value - previous.Value;
            if (regression > limit)
            {
                output.WriteLine($"Regression limit exceeded: {metric} delta={Format(regression)} limit={Format(limit)}.");
                failedLimit = true;
            }
        }

        return failedLimit ? 1 : 0;
    }

    private static async Task<RunReport> ReadReportAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        ValidateShape(document.RootElement, typeof(RunReport));
        return document.RootElement.Deserialize<RunReport>(ReportJson.Options) ?? throw new JsonException();
    }

    /// <summary>
    ///     Requires every writable contract property and rejects duplicate JSON object keys.
    /// </summary>
    /// <param name="element">The report JSON subtree.</param>
    /// <param name="type">The corresponding concrete contract type.</param>
    /// <exception cref="JsonException">A required property is absent or the JSON shape is invalid.</exception>
    private static void ValidateShape(JsonElement element, Type type)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        if (element.ValueKind == JsonValueKind.Object
            && element.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new JsonException();
        }

        if (type == typeof(string) || type.IsPrimitive || type == typeof(DateTimeOffset))
        {
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException();
            }

            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateShape(item, type.GetGenericArguments()[0]);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }

        foreach (var property in type.GetProperties().Where(property => property.SetMethod is not null))
        {
            if (!element.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(property.Name), out var value))
            {
                throw new JsonException();
            }

            ValidateShape(value, property.PropertyType);
        }
    }

    /// <summary>
    ///     Compares reproducibility settings, allowing a context-window difference between treatment and control.
    /// </summary>
    /// <param name="baseline">The validated baseline report.</param>
    /// <param name="candidate">The validated candidate report.</param>
    /// <returns>The names of settings that differ.</returns>
    private static IReadOnlyList<string> GetCompatibilityMismatches(RunReport baseline, RunReport candidate)
    {
        var mismatches = new List<string>();
        var pair = baseline.Run.Arm != candidate.Run.Arm;
        foreach (var property in baseline.Run.EffectiveSettings.GetType().GetProperties())
        {
            if (property.Name == "TokenGuardLogLevel" || (pair && property.Name == "ContextWindowTokens"))
            {
                continue;
            }

            if (!Equals(property.GetValue(baseline.Run.EffectiveSettings), property.GetValue(candidate.Run.EffectiveSettings)))
            {
                mismatches.Add("effectiveSettings." + JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            }
        }

        foreach (var property in typeof(RunMetadata).GetProperties().Where(property => property.Name is "HelperModel" or "HelperMaxOutputTokens"
            or "HelperTemperature" or "TurnBudgets" or "ManifestSha256"))
        {
            if (JsonSerializer.Serialize(property.GetValue(baseline.Run), ReportJson.Options)
                != JsonSerializer.Serialize(property.GetValue(candidate.Run), ReportJson.Options))
            {
                mismatches.Add(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            }
        }

        var previousTasks = baseline.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        foreach (var task in candidate.Tasks.Where(task => previousTasks.ContainsKey(task.TaskId)))
        {
            if (task.ModelCallBudget != previousTasks[task.TaskId].ModelCallBudget)
            {
                mismatches.Add("taskBudget." + task.TaskId);
            }
        }

        return mismatches;
    }

    private static Dictionary<string, double?> GetMetrics(ReportMetrics metrics)
    {
        var result = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (var property in typeof(ReportMetrics).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type != typeof(long) && type != typeof(double))
            {
                continue;
            }

            var value = property.GetValue(metrics);
            result.Add(JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture));
        }

        foreach (var (name, distribution) in new[]
        {
            ("prepareOutcomeCounts", metrics.PrepareOutcomeCounts), ("healthSignalCounts", metrics.HealthSignalCounts)
        })
        {
            foreach (var (key, count) in distribution)
            {
                result.Add(name + "." + key, count);
            }
        }

        return result;
    }

    private static Dictionary<string, double?> GetTotalsMetrics(RunReport report)
    {
        var metrics = GetMetrics(report.Totals.Metrics);
        metrics.Add("taskCount", report.Totals.TaskCount);
        metrics.Add("protocolCompletedTaskCount", report.Totals.ProtocolCompletedTaskCount);
        metrics.Add("protocolCompletionRate", report.Totals.ProtocolCompletionRate);
        return metrics;
    }

    private static Dictionary<string, double?> GetTaskMetrics(TaskReport task)
    {
        var metrics = GetMetrics(task.Metrics);
        metrics.Add("modelCallBudget", task.ModelCallBudget);
        metrics.Add("tokenGuardTranscriptOffset", task.TokenGuardTranscriptOffset);
        return metrics;
    }

    private static void PrintMetrics(TextWriter output, string label, IReadOnlyDictionary<string, double?> baseline,
        IReadOnlyDictionary<string, double?> candidate)
    {
        foreach (var name in baseline.Keys.Union(candidate.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var previous = baseline.TryGetValue(name, out var baselineValue) ? baselineValue : 0;
            var current = candidate.TryGetValue(name, out var candidateValue) ? candidateValue : 0;
            var delta = current.HasValue && previous.HasValue ? current.Value - previous.Value : (double?)null;
            output.WriteLine($"{label} {name}: baseline={Format(previous)} candidate={Format(current)} delta={Format(delta)}");
        }
    }

    /// <summary>
    ///     Prints measured input reduction only for compatible complete usage populations covering the same tasks.
    /// </summary>
    /// <param name="output">The comparison output.</param>
    /// <param name="baseline">The validated baseline report.</param>
    /// <param name="candidate">The validated candidate report.</param>
    /// <param name="compatible">Whether the pair satisfies the reproducibility setting requirements.</param>
    private static void PrintMeasuredReduction(TextWriter output, RunReport baseline, RunReport candidate, bool compatible)
    {
        if (baseline.Run.Arm == candidate.Run.Arm)
        {
            return;
        }

        var control = baseline.Run.Arm == "control" ? baseline : candidate;
        var treatment = baseline.Run.Arm == "treatment" ? baseline : candidate;
        var controlInput = control.Totals.Metrics.ProviderInputTokens;
        var treatmentInput = treatment.Totals.Metrics.ProviderInputTokens;
        if (!compatible || control.Partial || control.UnrunTaskIds.Count > 0 || treatment.UnrunTaskIds.Count > 0
            || control.Tasks.Any(task => !task.MeasurementsComplete) || treatment.Tasks.Any(task => !task.MeasurementsComplete)
            || !control.Tasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal).SetEquals(treatment.Tasks.Select(task => task.TaskId))
            || controlInput is null or <= 0 || treatmentInput is null)
        {
            output.WriteLine("measuredProviderInputReduction=unavailable (requires compatible complete arms and matching task coverage).");
            return;
        }

        var reduction = (controlInput.Value - (double)treatmentInput.Value) / controlInput.Value;
        output.WriteLine($"measuredProviderInputReduction={Format(reduction)} (measured)");
        var treatmentTasks = treatment.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        foreach (var task in control.Tasks.OrderBy(task => task.TaskId, StringComparer.Ordinal))
        {
            var treated = treatmentTasks[task.TaskId];
            output.WriteLine($"arm task {task.TaskId}: control input={Format(task.Metrics.ProviderInputTokens)} "
                + $"outcome={task.Outcome} calls={task.Metrics.ModelCallsMade}; treatment input={Format(treated.Metrics.ProviderInputTokens)} "
                + $"outcome={treated.Outcome} calls={treated.Metrics.ModelCallsMade}"
                + (task.Outcome == treated.Outcome ? "" : " OUTCOME DIFFERS"));
        }

        output.WriteLine("This is a difference in provider-reported input usage and does not establish equivalent work between the arms.");
    }

    private static bool TryReadLimit(string value, Dictionary<string, double> limits)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || !double.TryParse(value[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var limit)
            || !double.IsFinite(limit) || limit < 0)
        {
            return false;
        }

        return limits.TryAdd(value[..separator], limit);
    }

    private static string Format(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "unavailable";

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("compare <baseline.json> <candidate.json> [--allow-incompatible] [--limit metric=value]...");
        output.WriteLine("Limits use absolute metric-unit deltas, including ratios (0.05 means five percentage points).");
        output.WriteLine("Increases regress token/count metrics and estimatorAbsoluteMean/P95; decreases regress protocolCompletionRate "
            + "and estimatedPromptTokenReduction. Signed estimator statistics accept no limits.");
        output.WriteLine("Metrics: " + string.Join(", ", typeof(ReportMetrics).GetProperties()
            .Where(property => property.PropertyType == typeof(long) || property.PropertyType == typeof(long?)
                || property.PropertyType == typeof(double?) && property.Name is not ("EstimatorSignedMean" or "EstimatorSignedP95"))
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)))
            + ", taskCount, protocolCompletedTaskCount, protocolCompletionRate, prepareOutcomeCounts.<name>, healthSignalCounts.<name>.");
    }
}
