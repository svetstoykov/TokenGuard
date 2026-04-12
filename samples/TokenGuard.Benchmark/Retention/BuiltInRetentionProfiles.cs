namespace TokenGuard.Samples.Benchmark.Retention;

/// <summary>
/// Provides public access to built-in retention benchmark scenario batteries.
/// </summary>
/// <remarks>
/// <para>
/// This catalog defines stable, named <see cref="ScenarioProfile"/> instances that cover shallow, moderate, deep,
/// dense, and update-heavy retention conditions. Together they form standard benchmark battery for comparing
/// compaction strategies under repeatable pressure profiles.
/// </para>
/// <para>
/// Each property returns newly created <see cref="ScenarioProfile"/> so concurrent benchmark runs do not share mutable
/// collections. Seed values are fixed and documented per profile to keep retention reports traceable and reproducible.
/// </para>
/// </remarks>
public static class BuiltInRetentionProfiles
{
    /// <summary>
    /// Gets shallow, dense retention profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seed <c>1001</c>. This profile produces roughly 10,000 tokens across 5 turns with 8 uniformly distributed facts:
    /// 4 <see cref="FactCategory.Anchor"/>, 2 <see cref="FactCategory.Reinforced"/>, 1
    /// <see cref="FactCategory.Superseded"/>, and 1 <see cref="FactCategory.Buried"/>.
    /// </para>
    /// <para>
    /// It acts as baseline sanity check with minimal compaction pressure. Strategy failures here usually mean recall,
    /// synthesis, or scoring behavior is fundamentally broken rather than merely degraded by context age.
    /// </para>
    /// </remarks>
    public static ScenarioProfile ShallowDense => CreateShallowDense();

    /// <summary>
    /// Gets mid-depth, evenly spread retention profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seed <c>1002</c>. This profile produces roughly 25,000 tokens across 20 turns with 15 uniformly distributed
    /// facts: 5 <see cref="FactCategory.Anchor"/>, 3 <see cref="FactCategory.Reinforced"/>, 3
    /// <see cref="FactCategory.Superseded"/>, 2 <see cref="FactCategory.Relational"/>, and 2
    /// <see cref="FactCategory.Buried"/>.
    /// </para>
    /// <para>
    /// It models normal working-session drift where facts appear throughout conversation and age at moderate pace. This
    /// profile is reference case for sliding-window stress without extreme front-loading or density spikes.
    /// </para>
    /// </remarks>
    public static ScenarioProfile MidSpread => CreateMidSpread();

    /// <summary>
    /// Gets deep profile with aggressively front-loaded facts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seed <c>1003</c>. This profile produces roughly 50,000 tokens across 40 turns with 20 facts, where 80 percent of
    /// facts are planted in first 10 turns: 8 <see cref="FactCategory.Anchor"/>, 4
    /// <see cref="FactCategory.Reinforced"/>, 3 <see cref="FactCategory.Superseded"/>, 3
    /// <see cref="FactCategory.Relational"/>, and 2 <see cref="FactCategory.Buried"/>.
    /// </para>
    /// <para>
    /// This is sliding-window killer. Early facts must survive long stretches of later noise, so profile isolates how
    /// sharply strategy degrades when important information ages out of recency window.
    /// </para>
    /// </remarks>
    public static ScenarioProfile DeepFront => CreateDeepFront();

    /// <summary>
    /// Gets deep profile with high fact density spread across full conversation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seed <c>1004</c>. This profile produces roughly 50,000 tokens across 40 turns with 30 uniformly distributed facts:
    /// 10 <see cref="FactCategory.Anchor"/>, 6 <see cref="FactCategory.Reinforced"/>, 5
    /// <see cref="FactCategory.Superseded"/>, 5 <see cref="FactCategory.Relational"/>, and 4
    /// <see cref="FactCategory.Buried"/>.
    /// </para>
    /// <para>
    /// It maximizes breadth pressure rather than age pressure. Compaction strategy must preserve many unrelated facts at
    /// once instead of collapsing around small subset of salient details.
    /// </para>
    /// </remarks>
    public static ScenarioProfile DeepScattered => CreateDeepScattered();

    /// <summary>
    /// Gets mid-depth profile dominated by value updates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seed <c>1005</c>. This profile produces roughly 25,000 tokens across 20 turns with 12 uniformly distributed facts:
    /// 2 <see cref="FactCategory.Anchor"/>, 2 <see cref="FactCategory.Reinforced"/>, 6
    /// <see cref="FactCategory.Superseded"/>, 1 <see cref="FactCategory.Relational"/>, and 1
    /// <see cref="FactCategory.Buried"/>.
    /// </para>
    /// <para>
    /// It targets update correctness. Strategies that remember original values but lose later corrections should fail
    /// sharply here even if they perform acceptably on simple recall.
    /// </para>
    /// </remarks>
    public static ScenarioProfile SupersedeHeavy => CreateSupersedeHeavy();

    /// <summary>
    /// Gets all built-in retention benchmark profiles.
    /// </summary>
    /// <returns>
    /// New list containing standard benchmark battery in stable order: <see cref="ShallowDense"/>,
    /// <see cref="MidSpread"/>, <see cref="DeepFront"/>, <see cref="DeepScattered"/>, then
    /// <see cref="SupersedeHeavy"/>.
    /// </returns>
    public static IReadOnlyList<ScenarioProfile> All() =>
    [
        CreateShallowDense(),
        CreateMidSpread(),
        CreateDeepFront(),
        CreateDeepScattered(),
        CreateSupersedeHeavy()
    ];

    private static ScenarioProfile CreateShallowDense() => new(
        Name: nameof(ShallowDense),
        TargetTokenCount: 10_000,
        TurnCount: 5,
        Facts:
        [
            Anchor("shallow-codename", "What is the project codename?", "Orion", 0),
            Anchor("shallow-api-port", "What port does the API run on?", "8443", 1),
            Anchor("shallow-primary-region", "What is the primary deployment region?", "eu-west-1", 2),
            Anchor("shallow-default-branch", "What is the default branch name?", "main", 3),
            Reinforced("shallow-repo-host", "Where is the main repository hosted?", "GitHub Enterprise", 0),
            Reinforced("shallow-alert-channel", "Which Slack channel receives production alerts?", "#ops-alerts", 2),
            Superseded("shallow-sprint-budget", "What is the current sprint budget?", "$75,000", "$50,000", 0, 4),
            Buried("shallow-incident-tag", "What incident tag should on-call use for auth outages?", "AUTH-SEV2", 4)
        ],
        NoiseStyle: NoiseStyle.TechnicalDiscussion,
        Seed: 1001);

    private static ScenarioProfile CreateMidSpread() => new(
        Name: nameof(MidSpread),
        TargetTokenCount: 25_000,
        TurnCount: 20,
        Facts:
        [
            Anchor("mid-codename", "What is the project codename?", "Atlas", 0),
            Anchor("mid-api-port", "What port does the API run on?", "8443", 3),
            Anchor("mid-runtime", "Which .NET runtime version is targeted?", "net10.0", 6),
            Anchor("mid-log-index", "Which Elasticsearch index stores audit logs?", "tg-audit-live", 9),
            Anchor("mid-default-timeout", "What is the default outbound HTTP timeout?", "30 seconds", 14),
            Reinforced("mid-primary-db", "What is the primary database engine?", "PostgreSQL 17", 1),
            Reinforced("mid-package-feed", "Which package feed hosts internal prereleases?", "pkgs.dev.azure.com/tokenguard/prerelease", 7),
            Reinforced("mid-incident-bridge", "Which Zoom room is reserved for sev-1 incidents?", "Bridge 402", 12),
            Superseded("mid-sprint-budget", "What is the current sprint budget?", "$75,000", "$60,000", 2, 8),
            Superseded("mid-cache-ttl", "What is the current cache TTL for provider metadata?", "20 minutes", "10 minutes", 4, 10),
            Superseded("mid-release-window", "What is the approved release window?", "Thursday 18:00 UTC", "Wednesday 16:00 UTC", 5, 15),
            Relational("mid-health-endpoint", "What is the health check endpoint?", "https://localhost:8443/health", 11, "mid-api-port"),
            Relational("mid-audit-dashboard", "What Kibana dashboard tracks audit logs?", "tg-audit-live-overview", 16, "mid-log-index"),
            Buried("mid-drill-ticket", "Which Jira ticket tracks chaos drill follow-up work?", "OPS-214", 13),
            Buried("mid-breakglass-role", "Which IAM role is used for break-glass access?", "tg-prod-breakglass", 18)
        ],
        NoiseStyle: NoiseStyle.PlanningMeeting,
        Seed: 1002);

    private static ScenarioProfile CreateDeepFront() => new(
        Name: nameof(DeepFront),
        TargetTokenCount: 50_000,
        TurnCount: 40,
        Facts:
        [
            Anchor("front-codename", "What is the project codename?", "Helios", 0),
            Anchor("front-api-port", "What port does the API run on?", "9443", 1),
            Anchor("front-primary-region", "What is the primary deployment region?", "us-east-2", 1),
            Anchor("front-default-branch", "What is the default branch name?", "main", 2),
            Anchor("front-queue-name", "What queue handles background compaction jobs?", "tg-compaction-jobs", 2),
            Anchor("front-runbook", "Which runbook covers emergency prompt rollback?", "RB-117", 3),
            Anchor("front-metrics-namespace", "What CloudWatch namespace records retention metrics?", "TokenGuard/Retention", 4),
            Anchor("front-sso-tenant", "Which SSO tenant handles employee login?", "tokenguard-prod", 7),
            Reinforced("front-package-feed", "Which package feed hosts nightly builds?", "packages.tokenguard.dev/nightly", 3),
            Reinforced("front-status-page", "What is the public status page host?", "status.tokenguard.dev", 5),
            Reinforced("front-kafka-topic", "Which Kafka topic carries benchmark events?", "retention-benchmark-events", 6),
            Reinforced("front-support-rotation", "Which PagerDuty schedule owns benchmark incidents?", "Benchmark Primary", 8),
            Superseded("front-sprint-budget", "What is the current sprint budget?", "$90,000", "$65,000", 0, 12),
            Superseded("front-release-cutoff", "What is the current release code freeze cutoff?", "Monday 12:00 UTC", "Friday 17:00 UTC", 2, 14),
            Superseded("front-cache-ttl", "What is the current cache TTL for model pricing data?", "45 minutes", "15 minutes", 4, 16),
            Relational("front-health-endpoint", "What is the health check endpoint?", "https://localhost:9443/health", 5, "front-api-port"),
            Relational("front-metrics-dashboard", "Which Grafana dashboard tracks retention metrics?", "TokenGuard/Retention overview", 6, "front-metrics-namespace"),
            Relational("front-nightly-feed-doc", "Which handbook page documents the nightly feed?", "Nightly feed playbook for packages.tokenguard.dev/nightly", 9, "front-package-feed"),
            Buried("front-sandbox-subscription", "Which Azure subscription hosts benchmark sandboxes?", "tg-benchmark-sbx", 7),
            Buried("front-secrets-vault", "Which vault stores provider API keys?", "kv-tg-prod-shared", 9)
        ],
        NoiseStyle: NoiseStyle.DebugSession,
        Seed: 1003);

    private static ScenarioProfile CreateDeepScattered() => new(
        Name: nameof(DeepScattered),
        TargetTokenCount: 50_000,
        TurnCount: 40,
        Facts:
        [
            Anchor("scatter-codename", "What is the project codename?", "Nova", 0),
            Anchor("scatter-api-port", "What port does the API run on?", "8088", 2),
            Anchor("scatter-primary-region", "What is the primary deployment region?", "ap-southeast-1", 4),
            Anchor("scatter-default-branch", "What is the default branch name?", "trunk", 6),
            Anchor("scatter-log-index", "Which Elasticsearch index stores trace logs?", "tg-trace-live", 8),
            Anchor("scatter-runbook", "Which runbook covers queue backpressure mitigation?", "RB-204", 10),
            Anchor("scatter-sso-tenant", "Which SSO tenant handles contractor login?", "tokenguard-partners", 14),
            Anchor("scatter-object-store", "Which bucket stores retention exports?", "tg-retention-exports", 18),
            Anchor("scatter-workspace", "Which Linear workspace tracks platform work?", "TokenGuard Platform", 24),
            Anchor("scatter-proxy", "Which reverse proxy fronts benchmark API traffic?", "envoy-edge-01", 30),
            Reinforced("scatter-package-feed", "Which package feed hosts validated builds?", "packages.tokenguard.dev/validated", 1),
            Reinforced("scatter-support-rotation", "Which PagerDuty schedule owns retention incidents?", "Retention Primary", 7),
            Reinforced("scatter-ci-pool", "Which agent pool runs benchmark CI jobs?", "linux-benchmark-pool", 13),
            Reinforced("scatter-schema", "What is the migration schema name?", "retention_ops", 19),
            Reinforced("scatter-release-room", "Which Teams room hosts release review?", "Release Control", 25),
            Reinforced("scatter-wiki", "Which wiki space stores operator notes?", "OPS/Retention", 31),
            Superseded("scatter-sprint-budget", "What is the current sprint budget?", "$82,000", "$58,000", 3, 12),
            Superseded("scatter-cache-ttl", "What is the current cache TTL for tokenizer metadata?", "25 minutes", "12 minutes", 5, 15),
            Superseded("scatter-release-window", "What is the approved release window?", "Friday 19:00 UTC", "Thursday 17:00 UTC", 9, 20),
            Superseded("scatter-concurrency", "What is the benchmark worker concurrency limit?", "24 workers", "12 workers", 11, 22),
            Superseded("scatter-retention-days", "How many days are raw benchmark transcripts retained?", "21 days", "14 days", 17, 28),
            Relational("scatter-health-endpoint", "What is the health check endpoint?", "https://localhost:8088/health", 16, "scatter-api-port"),
            Relational("scatter-log-dashboard", "Which Kibana dashboard tracks trace logs?", "tg-trace-live-overview", 21, "scatter-log-index"),
            Relational("scatter-export-prefix", "What prefix do retention exports use in object storage?", "s3://tg-retention-exports/daily/", 23, "scatter-object-store"),
            Relational("scatter-runbook-url", "How is the queue mitigation runbook referenced in docs?", "runbooks/RB-204-queue-backpressure", 27, "scatter-runbook"),
            Relational("scatter-validated-feed-doc", "Which playbook documents validated builds feed?", "Validated feed playbook for packages.tokenguard.dev/validated", 33, "scatter-package-feed"),
            Buried("scatter-drill-ticket", "Which Jira ticket tracks benchmark failover drill work?", "PLAT-882", 20),
            Buried("scatter-breakglass-role", "Which IAM role is used for emergency database access?", "tg-db-breakglass", 26),
            Buried("scatter-sandbox-subscription", "Which Azure subscription hosts retention load tests?", "tg-retention-loadtest", 34),
            Buried("scatter-export-key", "Which KMS key encrypts retention exports?", "alias/tg-retention-exports", 38)
        ],
        NoiseStyle: NoiseStyle.RequirementsGathering,
        Seed: 1004);

    private static ScenarioProfile CreateSupersedeHeavy() => new(
        Name: nameof(SupersedeHeavy),
        TargetTokenCount: 25_000,
        TurnCount: 20,
        Facts:
        [
            Anchor("sup-codename", "What is the project codename?", "Mercury", 0),
            Anchor("sup-api-port", "What port does the API run on?", "7001", 5),
            Reinforced("sup-primary-db", "What is the primary database engine?", "SQL Server 2025", 1),
            Reinforced("sup-alert-channel", "Which Slack channel receives release alerts?", "#release-alerts", 8),
            Superseded("sup-sprint-budget", "What is the current sprint budget?", "$78,000", "$52,000", 0, 7),
            Superseded("sup-cache-ttl", "What is the current cache TTL for provider capability snapshots?", "18 minutes", "8 minutes", 2, 10),
            Superseded("sup-release-window", "What is the approved release window?", "Tuesday 20:00 UTC", "Monday 18:00 UTC", 3, 11),
            Superseded("sup-oncall-region", "Which region currently owns overnight on-call?", "EMEA", "AMER", 4, 12),
            Superseded("sup-worker-cap", "What is the benchmark worker cap?", "16 workers", "10 workers", 6, 14),
            Superseded("sup-transcript-retention", "How many days are raw transcripts retained?", "30 days", "14 days", 9, 17),
            Relational("sup-health-endpoint", "What is the health check endpoint?", "https://localhost:7001/health", 13, "sup-api-port"),
            Buried("sup-breakglass-role", "Which IAM role is used for release break-glass access?", "tg-release-breakglass", 18)
        ],
        NoiseStyle: NoiseStyle.TechnicalDiscussion,
        Seed: 1005);

    private static PlantedFact Anchor(string id, string question, string groundTruth, int plantedAtTurn) =>
        new(id, FactCategory.Anchor, question, groundTruth, null, plantedAtTurn, null, null);

    private static PlantedFact Reinforced(string id, string question, string groundTruth, int plantedAtTurn) =>
        new(id, FactCategory.Reinforced, question, groundTruth, null, plantedAtTurn, null, null);

    private static PlantedFact Buried(string id, string question, string groundTruth, int plantedAtTurn) =>
        new(id, FactCategory.Buried, question, groundTruth, null, plantedAtTurn, null, null);

    private static PlantedFact Superseded(
        string id,
        string question,
        string groundTruth,
        string originalValue,
        int plantedAtTurn,
        int supersededAtTurn) =>
        new(id, FactCategory.Superseded, question, groundTruth, originalValue, plantedAtTurn, supersededAtTurn, null);

    private static PlantedFact Relational(
        string id,
        string question,
        string groundTruth,
        int plantedAtTurn,
        string dependsOn) =>
        new(id, FactCategory.Relational, question, groundTruth, null, plantedAtTurn, null, dependsOn);
}
