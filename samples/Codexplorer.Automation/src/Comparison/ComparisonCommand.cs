using System.Globalization;
using System.Text.Json;
using Codexplorer.Automation.Reporting;
using Codexplorer.Measurements;

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

        output.WriteLine($"Report schema version: baseline={baseline.SchemaVersion} candidate={candidate.SchemaVersion}");
        output.WriteLine($"Baseline partial: {baseline.Partial}");
        output.WriteLine($"Candidate partial: {candidate.Partial}");
        output.WriteLine("Baseline unrun tasks: " + string.Join(", ", baseline.UnrunTaskIds));
        output.WriteLine("Candidate unrun tasks: " + string.Join(", ", candidate.UnrunTaskIds));

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
        foreach (var metric in limits.Keys.Where(IsKnownDistributionMetric))
        {
            baselineMetrics.TryAdd(metric, 0);
            candidateMetrics.TryAdd(metric, 0);
        }

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
            PrintScoring(output, id, previous, current);
            PrintLostCheckAttribution(output, id, previous, current, baseline.Run.Arm, candidate.Run.Arm);
        }

        output.WriteLine("Baseline-only tasks: " + string.Join(", ", baselineTasks.Keys.Except(candidateTasks.Keys).Order(StringComparer.Ordinal)));
        output.WriteLine("Candidate-only tasks: " + string.Join(", ", candidateTasks.Keys.Except(baselineTasks.Keys).Order(StringComparer.Ordinal)));
        output.WriteLine("Estimator error unit: " + baseline.EstimatorErrorUnit);
        PrintCachedInputAvailability(output, baseline, candidate);
        PrintProbeApplicabilityAvailability(output, baseline, candidate);
        if (baseline.SchemaVersion < 7 || candidate.SchemaVersion < 7)
            output.WriteLine("Masked-result probe comparison unavailable: reports before schema version 7 record no masked-result population.");
        PrintMeasuredReduction(output, baseline, candidate, mismatches.Count == 0);
        if (limits.Count > 0 && (baseline.Partial || candidate.Partial || baseline.UnrunTaskIds.Count > 0 || candidate.UnrunTaskIds.Count > 0
            || baseline.Tasks.Any(task => !task.MeasurementsComplete) || candidate.Tasks.Any(task => !task.MeasurementsComplete)
            || !baselineTasks.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(candidateTasks.Keys)))
        {
            output.WriteLine("Regression limits require complete runs with matching task coverage.");
            if (!allowIncompatible)
            {
                return 1;
            }

            output.WriteLine("Compatibility override enabled; regression limits use incomplete or differing task coverage.");
        }

        var failedLimit = false;
        foreach (var (metric, limit) in limits)
        {
            if (metric is "estimatorSignedMean" or "estimatorSignedP95" or "checksTotal" or "checksPassed" or "evaluatedTaskCount"
                or "deliverableCompletedTaskCount" or "probeCount" or "passedProbeCount" or "canaryPresentCount"
                or "providerCachedInputTokens" or "notApplicableProbeCount"
                or "maskedResultProbes.probeCount" or "maskedResultProbes.passedProbeCount" or "maskedResultProbes.notApplicableProbeCount"
                or "maskedResultProbes.canaryPresentCount" or "maskedResultProbes.refetchedCount"
                || !baselineMetrics.TryGetValue(metric, out var previous)
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
                or "checkPassRate" or "deliverableCompletionRate" or "probePassRate" or "maskedResultProbes.probePassRate"
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
        var root = document.RootElement;
        var shapeVersion = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemaVersion", out var version)
            && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number is 4 or 5 or 6 ? number : 7;
        ValidateShape(root, typeof(RunReport), shapeVersion);
        return root.Deserialize<RunReport>(ReportJson.Options) ?? throw new JsonException();
    }

    /// <summary>
    ///     Requires every writable contract property of the report's schema version and rejects duplicate JSON object keys.
    /// </summary>
    /// <remarks>
    ///     A field that a later schema version added must be absent from an older report and is read as <see langword="null" />.
    ///     A report of that version or a later one must carry it, with <see langword="null" /> for a value that was not reported.
    /// </remarks>
    /// <param name="element">The report JSON subtree.</param>
    /// <param name="type">The corresponding concrete contract type.</param>
    /// <param name="shapeVersion">The schema version whose shape the report must have.</param>
    /// <exception cref="JsonException">
    ///     A required property is absent.
    ///     -or-
    ///     The report carries a field that its schema version does not have.
    ///     -or-
    ///     The JSON shape is invalid.
    /// </exception>
    private static void ValidateShape(JsonElement element, Type type, int shapeVersion)
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
                ValidateShape(item, type.GetGenericArguments()[0], shapeVersion);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }

        var properties = type.GetProperties().Where(property => property.SetMethod is not null).ToArray();
        var names = type.GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToHashSet(StringComparer.Ordinal);
        if (element.EnumerateObject().Any(property => !names.Contains(property.Name)))
            throw new JsonException();
        foreach (var property in properties)
        {
            var present = element.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(property.Name), out var value);
            if (present != (shapeVersion >= GetFirstSchemaVersion(type, property.Name)))
            {
                throw new JsonException();
            }

            if (present)
            {
                ValidateShape(value, property.PropertyType, shapeVersion);
            }
        }
    }

    /// <summary>Gets the first schema version whose reports carry a field.</summary>
    /// <param name="type">The contract type declaring the property.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>
    ///     5 for cached input tokens, 6 for not-applicable counts, 7 for masked-result evidence, and 4 for other fields.
    /// </returns>
    private static int GetFirstSchemaVersion(Type type, string propertyName)
    {
        if ((type == typeof(ProviderCallMeasurement) && propertyName == nameof(ProviderCallMeasurement.CachedInputTokens))
            || (type == typeof(ReportMetrics) && propertyName == nameof(ReportMetrics.ProviderCachedInputTokens)))
        {
            return 5;
        }

        if ((type == typeof(RunTotals) && propertyName == nameof(RunTotals.MaskedResultProbes))
            || (type == typeof(ProbeResult) && propertyName is nameof(ProbeResult.Kind) or nameof(ProbeResult.Refetched))
            || (type == typeof(PrepareMeasurement) && propertyName.StartsWith("Probe", StringComparison.Ordinal)))
            return 7;
        return type == typeof(RunTotals) && propertyName == nameof(RunTotals.NotApplicableProbeCount) ? 6 : 4;
    }

    /// <summary>
    ///     Prints why the probe counts cannot be compared when a report that declares probes has no not-applicable status.
    /// </summary>
    /// <param name="output">The comparison output.</param>
    /// <param name="baseline">The validated baseline report.</param>
    /// <param name="candidate">The validated candidate report.</param>
    private static void PrintProbeApplicabilityAvailability(TextWriter output, RunReport baseline, RunReport candidate)
    {
        var reasons = new[] { (Label: "baseline", Report: baseline), (Label: "candidate", Report: candidate) }
            .Where(side => side.Report.Totals.NotApplicableProbeCount is null && side.Report.Totals.ProbeCount > 0)
            .Select(side => $"{side.Label} is a schema version {side.Report.SchemaVersion} report, which counts a probe of a task "
                + "without summarization or drop as invalid").ToArray();
        if (reasons.Length > 0)
        {
            output.WriteLine("Not-applicable probe comparison skipped: " + string.Join("; ", reasons) + ".");
        }
    }

    /// <summary>
    ///     Prints why the cached input token totals cannot be compared when either report has none.
    /// </summary>
    /// <param name="output">The comparison output.</param>
    /// <param name="baseline">The validated baseline report.</param>
    /// <param name="candidate">The validated candidate report.</param>
    private static void PrintCachedInputAvailability(TextWriter output, RunReport baseline, RunReport candidate)
    {
        static string? MissingReason(string label, RunReport report)
        {
            if (report.Totals.Metrics.ProviderCachedInputTokens is not null)
            {
                return null;
            }

            if (report.SchemaVersion == 4)
            {
                return $"{label} is a schema version 4 report, which records no cached input tokens";
            }

            var calls = report.Tasks.SelectMany(task => task.ProviderCalls).ToArray();
            return $"{label} provider reported no cached input tokens for {calls.Count(call => call.CachedInputTokens is null)} of "
                + $"{calls.Length} calls";
        }

        var reasons = new[] { MissingReason("baseline", baseline), MissingReason("candidate", candidate) }.Where(reason => reason is not null)
            .ToArray();
        if (reasons.Length > 0)
        {
            output.WriteLine("Cached input token comparison skipped: " + string.Join("; ", reasons) + ".");
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
            var previous = previousTasks[task.TaskId];
            if (!previous.Checks.Select(check => check.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(task.Checks.Select(check => check.Id)))
                mismatches.Add("taskChecks." + task.TaskId);
            if ((previous.Probe is null) != (task.Probe is null) || previous.Probe?.Requires != task.Probe?.Requires
                || previous.Probe?.Kind != task.Probe?.Kind)
                mismatches.Add("taskProbe." + task.TaskId);
            if (task.ModelCallBudget != previous.ModelCallBudget)
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
        metrics.Add("evaluatedTaskCount", report.Totals.EvaluatedTaskCount);
        metrics.Add("deliverableCompletedTaskCount", report.Totals.DeliverableCompletedTaskCount);
        metrics.Add("deliverableCompletionRate", report.Totals.DeliverableCompletionRate);
        var masked = report.Totals.MaskedResultProbes;
        metrics.Add("maskedResultProbes.probeCount", masked?.ProbeCount);
        metrics.Add("maskedResultProbes.invalidProbeCount", masked?.InvalidProbeCount);
        metrics.Add("maskedResultProbes.notApplicableProbeCount", masked?.NotApplicableProbeCount);
        metrics.Add("maskedResultProbes.passedProbeCount", masked?.PassedProbeCount);
        metrics.Add("maskedResultProbes.probePassRate", masked?.ProbePassRate);
        metrics.Add("maskedResultProbes.canaryPresentCount", masked?.CanaryPresentCount);
        metrics.Add("maskedResultProbes.refetchedCount", masked?.RefetchedCount);
        metrics.Add("probeCount", report.Totals.ProbeCount);
        metrics.Add("invalidProbeCount", report.Totals.InvalidProbeCount);
        metrics.Add("notApplicableProbeCount", report.Totals.NotApplicableProbeCount);
        metrics.Add("passedProbeCount", report.Totals.PassedProbeCount);
        metrics.Add("probePassRate", report.Totals.ProbePassRate);
        metrics.Add("canaryPresentCount", report.Totals.CanaryPresentCount);
        return metrics;
    }

    private static Dictionary<string, double?> GetTaskMetrics(TaskReport task)
    {
        var metrics = GetMetrics(task.Metrics);
        metrics.Add("modelCallBudget", task.ModelCallBudget);
        metrics.Add("tokenGuardTranscriptOffset", task.TokenGuardTranscriptOffset);
        return metrics;
    }

    private static void PrintScoring(TextWriter output, string id, TaskReport baseline, TaskReport candidate)
    {
        static string ProbeText(ProbeResult? probe) => probe is null ? "none"
            : $"{probe.Status}({probe.Reason ?? "none"}) CanaryPresent={probe.CanaryPresent}"
                + (probe.Kind == "maskedResult" ? $" Kind=maskedResult Refetched={probe.Refetched}" : "");
        static string CheckText(CheckResult? check) => check is null ? "missing" : check.Passed ? "pass" : $"fail({check.Reason})";
        static string Counts(TaskReport task) =>
            $"masked={task.Metrics.MessagesMasked} summarized={task.Metrics.MessagesSummarized} dropped={task.Metrics.MessagesDropped}";
        output.WriteLine($"task {id} probe: baseline={ProbeText(baseline.Probe)} candidate={ProbeText(candidate.Probe)}");
        if (baseline.Probe?.Status == "failed")
            output.WriteLine($"task {id} failed baseline probe: {Counts(baseline)}");
        if (candidate.Probe?.Status == "failed")
            output.WriteLine($"task {id} failed candidate probe: {Counts(candidate)}");
        var previous = baseline.Checks.ToDictionary(check => check.Id, StringComparer.OrdinalIgnoreCase);
        var current = candidate.Checks.ToDictionary(check => check.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var checkId in previous.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var left = previous.GetValueOrDefault(checkId);
            var right = current.GetValueOrDefault(checkId);
            if (left is not null && right is not null && left.Passed == right.Passed && left.Reason == right.Reason)
                continue;
            output.WriteLine($"task {id} check {checkId}: baseline={CheckText(left)} candidate={CheckText(right)}");
            if (left?.Passed == false)
                output.WriteLine($"task {id} failed baseline check {checkId}: {Counts(baseline)}");
            if (right?.Passed == false)
                output.WriteLine($"task {id} failed candidate check {checkId}: {Counts(candidate)}");
        }
    }

    /// <summary>
    ///     Prints, for a task that lost a check, whether compaction ran in the task that lost it and which kinds ran.
    /// </summary>
    /// <remarks>
    ///     Between a control and a treatment report, a lost check passed in the control task and failed in the treatment task, in either
    ///     argument order. Between two reports of the same arm, it passed in the baseline task and failed in the candidate task.
    ///     A check that one side does not declare is a declaration difference and is not a lost check.
    /// </remarks>
    /// <param name="output">The comparison output.</param>
    /// <param name="id">The task identifier.</param>
    /// <param name="baseline">The baseline task.</param>
    /// <param name="candidate">The candidate task.</param>
    /// <param name="baselineArm">The arm of the baseline report.</param>
    /// <param name="candidateArm">The arm of the candidate report.</param>
    private static void PrintLostCheckAttribution(TextWriter output, string id, TaskReport baseline, TaskReport candidate,
        string baselineArm, string candidateArm)
    {
        var pair = baselineArm != candidateArm;
        var (reference, losing) = pair && baselineArm == "treatment" ? (candidate, baseline) : (baseline, candidate);
        var passed = reference.Checks.Where(check => check.Passed).Select(check => check.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lost = losing.Checks.Where(check => !check.Passed && passed.Contains(check.Id)).Select(check => check.Id)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (lost.Length == 0)
        {
            return;
        }

        var metrics = losing.Metrics;
        var kinds = new[]
        {
            (Name: "masking", Ran: metrics.MessagesMasked > 0), (Name: "summarization", Ran: metrics.MessagesSummarized > 0),
            (Name: "emergency truncation", Ran: metrics.EmergencyTruncations > 0 || metrics.MessagesDropped > 0)
        }.Where(kind => kind.Ran).Select(kind => kind.Name).ToArray();
        var compaction = kinds.Length > 0
            ? $"{string.Join(", ", kinds)} (masked={metrics.MessagesMasked} summarized={metrics.MessagesSummarized} "
                + $"dropped={metrics.MessagesDropped} emergencyTruncations={metrics.EmergencyTruncations})"
            : metrics.StrategyRuns > 0 ? $"ran without masking, summarizing or dropping a message (strategyRuns={metrics.StrategyRuns})"
            : "none";
        output.WriteLine($"task {id} lost checks against {(pair ? "control" : "baseline")}: {string.Join(", ", lost)}; "
            + $"compaction in {(pair ? "treatment" : "candidate")}: {compaction}");
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

    /// <summary>Recognizes distribution counters defined by the report schema.</summary>
    /// <param name="metric">The full distribution metric name. Cannot be <see langword="null" />.</param>
    /// <returns>Whether the metric names a known prepare outcome or health signal.</returns>
    private static bool IsKnownDistributionMetric(string metric) => metric is
        "prepareOutcomeCounts.Ready" or "prepareOutcomeCounts.Compacted" or "prepareOutcomeCounts.CompactionInsufficient"
        or "prepareOutcomeCounts.CannotCompact" or "healthSignalCounts.EstimatorDrift" or "healthSignalCounts.RepeatedCompaction"
        or "healthSignalCounts.LowCompactionYield" or "healthSignalCounts.SummarizationFailureStreak" or "healthSignalCounts.RepeatedOverBudget"
        or "healthSignalCounts.PinnedPressure" or "healthSignalCounts.CheckpointChurn";

    private static string Format(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "unavailable";

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("compare <baseline.json> <candidate.json> [--allow-incompatible] [--limit metric=value]...");
        output.WriteLine("Limits use absolute metric-unit deltas, including ratios (0.05 means five percentage points).");
        output.WriteLine("Limits require complete runs with matching task coverage unless --allow-incompatible is supplied.");
        output.WriteLine("Absent known prepare outcomes and health signals count as zero; unknown names are rejected.");
        output.WriteLine("Increases regress token/count metrics and estimatorAbsoluteMean/P95; decreases regress protocolCompletionRate "
            + "and estimatedPromptTokenReduction. Signed estimator statistics accept no limits.");
        output.WriteLine("Decreases regress checkPassRate, deliverableCompletionRate, probePassRate; "
            + "increases regress invalidProbeCount, messagesMasked, messagesSummarized.");
        output.WriteLine("Informational exclusions: checksTotal, checksPassed, evaluatedTaskCount, deliverableCompletedTaskCount, "
            + "probeCount, passedProbeCount, notApplicableProbeCount, canaryPresentCount, providerCachedInputTokens; "
            + "signed estimator statistics.");
        output.WriteLine("Unavailable limited rates fail. Compare probePassRate only between the same arm's valid-probe populations.");
        output.WriteLine("Schema version 4, 5, 6 and 7 reports compare with each other. A version 4 report records no cached input tokens, "
            + "so that comparison is skipped and reported.");
        output.WriteLine("A probe of a task where nothing was summarized or dropped is notApplicable and outside probePassRate. "
            + "A version 4 or 5 report counts such a probe as invalid, which is reported when it declares probes.");
        output.WriteLine("Masked-result probes use separate maskedResultProbes.* counts and pass rates. "
            + "Reports before version 7 have no population.");
        output.WriteLine("Masked-result rate decreases and invalid-count increases accept regression limits; "
            + "other masked-result counts are informational.");
        output.WriteLine("For each task that lost a check, one line names the lost checks and the compaction that ran in the task that "
            + "lost them: masking, summarization, emergency truncation, or none. Between a control and a treatment report that task "
            + "is the treatment task; between two reports of one arm it is the candidate task.");
        output.WriteLine("Metrics: " + string.Join(", ", typeof(ReportMetrics).GetProperties()
            .Where(property => property.Name != nameof(ReportMetrics.ProviderCachedInputTokens))
            .Where(property => property.PropertyType == typeof(long) || property.PropertyType == typeof(long?)
                || property.PropertyType == typeof(double?) && property.Name is not ("EstimatorSignedMean" or "EstimatorSignedP95"))
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)))
            + ", taskCount, protocolCompletedTaskCount, protocolCompletionRate, deliverableCompletionRate, probePassRate, invalidProbeCount, "
            + "maskedResultProbes.probePassRate, maskedResultProbes.invalidProbeCount, prepareOutcomeCounts.<name>, healthSignalCounts.<name>.");
    }
}
