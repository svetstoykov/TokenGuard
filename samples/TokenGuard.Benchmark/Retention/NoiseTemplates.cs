namespace TokenGuard.Samples.Benchmark.Retention;

internal static class NoiseTemplates
{
    internal static IReadOnlyList<string> GetTemplates(NoiseStyle noiseStyle)
    {
        return noiseStyle switch
        {
            NoiseStyle.TechnicalDiscussion => TechnicalDiscussion,
            NoiseStyle.PlanningMeeting => PlanningMeeting,
            NoiseStyle.DebugSession => DebugSession,
            NoiseStyle.RequirementsGathering => RequirementsGathering,
            _ => throw new ArgumentOutOfRangeException(nameof(noiseStyle), noiseStyle, "Unknown noise style."),
        };
    }

    private static IReadOnlyList<string> TechnicalDiscussion { get; } =
    [
        "We reviewed the cache invalidation path again and agreed that write-through stays simpler for the hot route, but we still need better observability around stale reads during failover.",
        "The serialization layer is doing more work than expected because the DTO mapping still allocates intermediate objects, which makes the p95 jump whenever the batch importer runs.",
        "Yesterday's thread about retry policy landed on a compromise: short exponential backoff for transient network faults, but no silent retries for validation failures because that just hides bad inputs.",
        "The current migration plan is technically safe, although it leaves us with two naming conventions in the same subsystem until we can finish the cleanup pass next sprint.",
        "We kept the endpoint shape stable for now because downstream tooling already assumes that contract, and changing it early would create extra churn with no real product gain.",
        "Most of the complexity is not in the handler itself but in the surrounding orchestration, especially where background jobs and API requests compete for the same state transitions.",
        "The team is fine with a small amount of duplication here if it keeps the flow explicit, since the previous abstraction made debugging harder every time requests crossed service boundaries.",
        "We measured the new query plan against production-like data and it helps, but the biggest win still comes from reducing unnecessary fan-out before the request reaches persistence.",
        "Part of the confusion came from two different timeout values living in different layers, so logs made it look like one operation failed instantly when another layer was still waiting.",
        "We should keep the adapter thin because every extra convenience method there becomes another place where provider-specific behavior leaks into otherwise neutral code.",
        "There is still open debate about whether the compaction trigger should react to hard token counts only or include a safety margin for tool definitions that appear later in the request.",
        "The prototype proved the idea, but the naming still feels off because some types describe transport details while others describe domain intent, which makes the surface harder to teach.",
        "One reason the earlier branch felt brittle was that it mixed parsing, validation, and normalization in one pass, so a small rule change forced edits across unrelated sections.",
        "Our current test matrix covers normal flows well, but edge pressure shows up when long transcripts and pinned messages interact because those cases magnify small accounting errors.",
        "We chose not to hide the fallback path behind configuration magic because operationally it matters when the system is using degraded behavior instead of the preferred route.",
        "The rough consensus is that we should optimize for stable semantics first and prettier internals later, since benchmark regressions are more damaging than a little repetition in code.",
        "Nothing in the current output format is fundamentally wrong, but the ordering could be clearer because humans read it top to bottom while machines mostly key off field names.",
        "The provider adapter remains intentionally boring; almost every bug so far came from assumptions around sequencing rather than from the actual message conversion logic.",
        "We can probably reduce allocations further by reusing buffers, although that only matters if we confirm preparation overhead is meaningful compared to network latency.",
        "For now the safest move is to preserve the explicit transition steps because they make failure analysis easier when a run diverges from expected token budget behavior."
    ];

    private static IReadOnlyList<string> PlanningMeeting { get; } =
    [
        "We mapped the remaining work by dependency order and the only real blocker is that QA cannot validate the onboarding flow until the export format stops changing every other day.",
        "Most of the meeting focused on sequencing, because everyone agrees on outcome but not on whether we should spend this week stabilizing internals or push visible features first.",
        "The design review produced fewer action items than expected, although product still wants a short note explaining which pieces are safe to defer without affecting launch confidence.",
        "We should keep next sprint lighter than usual since infrastructure changes and documentation updates always consume more calendar time than the initial estimates suggest.",
        "No one objected to splitting the rollout into phases, mainly because that gives support and ops a cleaner story if we need to diagnose issues in the first few days.",
        "The action list is manageable, but several owners depend on the same shared environment, so the calendar risk comes from coordination more than from implementation complexity.",
        "We decided to treat the backlog cleanup as real project work instead of spare-time housekeeping because unclear tickets keep slowing estimation and create avoidable handoff friction.",
        "One useful takeaway from the review was that several tasks looked independent on paper but actually converge on the same configuration surface, so we should stage them carefully.",
        "The milestone still looks realistic if we stop reopening settled naming debates and keep the next set of tickets focused on execution rather than architecture philosophy.",
        "Product asked for clearer acceptance criteria because the last release had too many items that were technically done but still generated follow-up questions from adopters.",
        "We should reserve time for dry-run validation since the implementation path seems straightforward, but operational readiness usually breaks on assumptions nobody wrote down during planning.",
        "The group agreed that feature flags are worth the extra setup here because staged enablement gives us space to compare baseline behavior against the managed path safely.",
        "Resourcing is tight in the middle of the sprint, so work that requires cross-team review should start earlier even if the actual coding time is fairly small.",
        "There is broad alignment on outcomes, but we still need to decide which success numbers matter most so the post-release review does not drift into subjective impressions.",
        "The retrospective theme came up again: small ambiguities at ticket-writing time keep turning into larger execution delays later, especially when assumptions stay implicit.",
        "Nothing in the plan is unusually risky, but the order matters because upstream schema changes would invalidate several test fixtures if they land after client updates begin.",
        "We captured a shorter cut of the roadmap for leadership because the detailed engineering plan makes sense internally but is too granular for portfolio-level reporting.",
        "The schedule buffer is thin, so anything requiring procurement, credentials, or external approval should move now rather than wait until implementation is already underway.",
        "One advantage of the phased approach is that it lets us gather evidence early instead of arguing abstractly about whether the strategy will help under realistic workloads.",
        "The follow-up doc should state owners, timing assumptions, and rollback triggers plainly because those details disappear quickly once execution pressure rises."
    ];

    private static IReadOnlyList<string> DebugSession { get; } =
    [
        "The failing path only reproduces after several iterations, which suggests state accumulation rather than a single bad input on the first request.",
        "Logs show the request shape is mostly correct, but one field flips values between retries, so something in preprocessing is mutating data we expected to remain stable.",
        "We ruled out provider latency as primary cause because the local replay still drifts, even when the external call is stubbed and timing noise disappears completely.",
        "The stack trace points at validation, but that looks more like secondary damage because earlier warnings already show the object graph entering an inconsistent state.",
        "One clue is that the counters stay close until a long transcript appears, after which the prepared payload jumps more than the raw history alone would explain.",
        "We need a tighter repro because right now three variables change at once: seed, conversation length, and whether pinned instructions remain in front of the payload.",
        "The branch with extra diagnostics helped confirm ordering is wrong in one path, though we still have not proved whether the root cause sits in recording or in preparation.",
        "Nothing crashes in the happy path, which is why this lasted so long; the bug only surfaces when old messages and recent tool output interact under budget pressure.",
        "One suspicious detail is that the stale answer is technically valid for an earlier state, which means the update path may be missing rather than the retrieval path being broken.",
        "The timestamps are not enough to explain sequence here because message construction and later counting happen in different phases, and our logs collapse those boundaries together.",
        "The current trace is too noisy, so filtering by correlation id and turn index should make it easier to compare the failing run against the stable baseline side by side.",
        "We have evidence that the parser tolerates minor format drift, so the missing result probably comes from how the response is segmented rather than from simple whitespace issues.",
        "The temporary guard reduced blast radius, but it does not explain why the internal state ever becomes invalid in the first place, so we still need root cause not just containment.",
        "A useful next check is replaying the same seed with shortened noise blocks, because that will tell us whether token pressure itself is triggering the divergence.",
        "The benchmark did exactly what we wanted here: it exposed a subtle failure mode that ordinary functional tests would miss because the output still looks superficially reasonable.",
        "We should inspect the exact turn where the newer value appears because a stale recall result implies the superseding statement may never reach the final prepared payload.",
        "Another oddity is that the counts remain deterministic across runs, which usually means logic bug not race condition even though the symptom first looked timing-related.",
        "The earlier fix likely masked the symptom by preserving more recent turns, but that does not guarantee the same profile will remain stable once token budgets get tighter again.",
        "It is worth checking whether the relational fact wording became detached from its dependency, because that would make a correct answer impossible even with perfect recall.",
        "We can keep digging in logs, but at this point a focused unit test around exact turn placement will probably give a faster signal than another free-form replay."
    ];

    private static IReadOnlyList<string> RequirementsGathering { get; } =
    [
        "The stakeholder clarified that speed matters, but only if the team can still explain why the system kept or removed specific context during longer sessions.",
        "What they really want is not just lower token usage; they want predictable behavior when conversations become messy, repetitive, and full of details that stop mattering later.",
        "One recurring request is for setup to stay lightweight because teams evaluating the library do not want to redesign their whole agent loop just to try compaction.",
        "They also care about portability, which means any benchmark story has to avoid binding the workflow too tightly to one provider SDK or response shape.",
        "From the product side, the strongest ask is for evidence that saved tokens do not silently destroy recall of important earlier facts or later corrections.",
        "The conversation surfaced a difference between debugging support and benchmark reporting, so we should treat human-readable diagnostics as distinct from machine-friendly output.",
        "Several users described long sessions where tool output swamps everything else, which reinforces the need for examples grounded in realistic engineering conversations.",
        "They do not need endless customization first; they need credible defaults that demonstrate value before anyone invests time tuning thresholds or strategy combinations.",
        "Another expectation is reproducibility, because teams want to compare branch-to-branch behavior without wondering whether random prompt drift caused the change.",
        "The requirements discussion also made it clear that baseline comparisons are mandatory, since otherwise a weak score could mean either bad compaction or bad scenario design.",
        "One practical constraint is that benchmark pieces should stay composable so users can swap profile definitions, scoring rules, or model delegates without rewriting everything.",
        "Stakeholders repeatedly separated library behavior from sample-app behavior, which means sample benchmarks can be opinionated but core abstractions still need clean boundaries.",
        "They want output that answers two concrete questions fast: how much context did management save, and what did that saving cost in factual recall.",
        "There was also interest in scenarios that stress stale updates because keeping the wrong old value is often worse than forgetting the fact entirely.",
        "The discovery call suggested we should keep scenario definitions declarative, since that makes them easier to review and easier to trust in CI.",
        "Users seemed comfortable with synthetic conversations as long as the noise feels plausible enough that summarization strategies cannot trivially detect filler.",
        "Nobody asked for automatic profile generation yet, which is useful because hand-authored scenario shapes are easier to reason about while the benchmark layer matures.",
        "The reporting requirement is intentionally simple: deterministic metrics first, richer visualizations later if the core numbers prove meaningful over repeated runs.",
        "What matters most is that each benchmark run tells a clear story instead of producing a large blob of output that teams have to interpret manually.",
        "The strongest product signal remains confidence, because teams adopting context management need proof that compaction is preserving what should survive."
    ];
}
