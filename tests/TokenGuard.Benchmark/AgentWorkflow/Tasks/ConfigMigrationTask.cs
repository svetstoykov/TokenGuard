using FluentAssertions;

namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Defines a config-migration scenario that forces the model to process six small INI files sequentially,
/// converting each to YAML and logging the result — generating many short turns with small tool outputs.
/// </summary>
internal static class ConfigMigrationTask
{
    private const string CompletionMarker = "CONFIG_MIGRATION_COMPLETE";

    /// <summary>
    /// Creates the config-migration task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "ConfigMigration",
        conversationName: "e2e-config-migration",
        systemPrompt:
            "You are a configuration migration assistant running inside a TokenGuard E2E test. " +
            "Your job is to convert ten legacy INI config files to the YAML format defined in migration-spec.txt. " +
            "You MUST use the provided tools for every file operation — never invent or skip content. " +
            "Process config files strictly in order: config-01.ini through config-10.ini. " +
            "Complete all steps for one file before moving to the next. " +
            "When all migrations and the report are written, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: migrate ten legacy config files to the new YAML format.\n" +
            "Step 1 – list all workspace files.\n" +
            "Step 2 – read migration-spec.txt to understand the mapping rules.\n" +
            "Step 3 – for each config file in order (config-01.ini through config-10.ini), do exactly:\n" +
            "  a. Read the source INI file.\n" +
            "  b. Create the YAML output file (same name, .yaml extension) using the rules from migration-spec.txt.\n" +
            "  c. Read back the YAML file to confirm it was written correctly.\n" +
            "Step 4 – create migration-report.txt with one line per migrated file:\n" +
            "  config-01.ini -> config-01.yaml: OK\n" +
            "  (repeat for all ten files)\n" +
            "Step 5 – read migration-report.txt to confirm all ten lines are present.\n" +
            "Do not claim completion until all ten .yaml files exist and migration-report.txt lists all ten migrations.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync,
        size: TaskSize.Small);

    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "migration-spec.txt"),
            "Config Migration Specification\n" +
            "==============================\n" +
            "Source: INI format (.ini)\n" +
            "Target: YAML format (.yaml)\n\n" +
            "Mapping rules:\n" +
            "  [feature_flags] section -> top-level key 'features:'\n" +
            "    flag=1 -> '  <flag>: true'\n" +
            "    flag=0 -> '  <flag>: false'\n" +
            "  [limits] section -> top-level key 'limits:'\n" +
            "    key=value -> '  <key>: <value>'\n\n" +
            "Example input:\n" +
            "  [feature_flags]\n" +
            "  dark_mode=1\n" +
            "  beta=0\n" +
            "  [limits]\n" +
            "  max_retries=3\n\n" +
            "Example output:\n" +
            "  features:\n" +
            "    dark_mode: true\n" +
            "    beta: false\n" +
            "  limits:\n" +
            "    max_retries: 3\n");

        var configs = new (string File, string Comment, string[] Flags, string[] Limits)[]
        {
            ("config-01.ini", "service: payment",       ["checkout=1", "express_pay=0", "split_bill=1", "refund=1", "partial_pay=0"],       ["max_retries=3", "timeout_ms=5000", "idempotency_ttl=86400", "max_amount=99999"]),
            ("config-02.ini", "service: auth",          ["sso=1", "mfa=0", "remember_me=1", "passkey=1", "biometric=0"],                   ["session_ttl=3600", "max_attempts=5", "lockout_ms=60000", "token_refresh=900"]),
            ("config-03.ini", "service: inventory",     ["live_stock=1", "reserve_check=0", "bulk_import=1", "low_stock_alert=1", "backorder=0"], ["sync_interval=60", "batch_size=200", "lock_ttl=30", "reorder_point=50"]),
            ("config-04.ini", "service: notifications", ["email=1", "sms=0", "push=1", "in_app=1", "digest=0"],                            ["retry_count=2", "delay_ms=1000", "max_payload=4096", "rate_per_min=120"]),
            ("config-05.ini", "service: reporting",     ["pdf_export=0", "csv_export=1", "dashboard=1", "scheduled=1", "realtime=0"],       ["max_rows=10000", "cache_ttl=900", "query_timeout=30", "max_charts=20"]),
            ("config-06.ini", "service: gateway",       ["rate_limit=1", "circuit_breaker=1", "logging=0", "tracing=1", "cors=1"],          ["rps_limit=500", "timeout_ms=3000", "burst_limit=1000", "retry_budget=10"]),
            ("config-07.ini", "service: search",        ["fuzzy=1", "autocomplete=1", "spell_check=0", "facets=1", "synonyms=0"],           ["max_results=100", "index_refresh=5", "shard_count=4", "replica_count=2"]),
            ("config-08.ini", "service: media",         ["transcoding=1", "streaming=1", "watermark=0", "cdn=1", "thumbnail=1"],            ["max_file_mb=2048", "chunk_size=8192", "expire_hours=72", "quality=85"]),
            ("config-09.ini", "service: billing",       ["invoice=1", "auto_charge=0", "proration=1", "tax=1", "discounts=1"],              ["grace_days=7", "retry_schedule=24", "max_invoice_items=500", "vat_rate=20"]),
            ("config-10.ini", "service: analytics",     ["event_tracking=1", "heatmaps=0", "funnels=1", "cohorts=1", "ab_testing=0"],       ["retention_days=90", "sample_rate=10", "batch_flush=60", "max_events=1000"]),
        };

        foreach (var (file, comment, flags, limits) in configs)
        {
            var content =
                $"# {comment}\n" +
                "[feature_flags]\n" +
                string.Join("\n", flags) + "\n" +
                "[limits]\n" +
                string.Join("\n", limits) + "\n";

            await File.WriteAllTextAsync(Path.Combine(dir, file), content);
        }
    }

    private static async Task AssertAsync(string dir, string? finalText)
    {
        var report = await File.ReadAllTextAsync(Path.Combine(dir, "migration-report.txt"));

        for (var i = 1; i <= 10; i++)
        {
            var iniName  = $"config-{i:D2}.ini";
            var yamlName = $"config-{i:D2}.yaml";
            var yamlPath = Path.Combine(dir, yamlName);

            File.Exists(yamlPath).Should().BeTrue(
                because: $"{yamlName} must be created during migration");

            var yaml = await File.ReadAllTextAsync(yamlPath);
            yaml.Should().Contain("features:",
                because: $"{yamlName} must use the target YAML format");

            report.Should().Contain(iniName,
                because: $"migration-report.txt must include an entry for {iniName}");
        }

        report.Should().Contain("OK",
            because: "migration-report.txt must mark each migration with OK status");
    }
}
