# Verifiable corpus calibration

These are calibration observations for `verifiable-corpus.json`, before recording the full treatment/control baseline.
They use `qwen/qwen3.7-flash`; reports and answer texts retain their actual provenance and verdicts.

## No-tools measurement

[`no-tools.json`](no-tools.json) records one fresh, independent provider response for each unchanged task prompt. Each
request used the Codexplorer system prompt, an explicit no-tools calibration instruction, and the repository URL and
pinned commit as identity only. It supplied no repository content, tool schemas, previous answers, or expected check values.
The exact calibration instruction, system-prompt hash, prompt/request hashes, generation identifiers, provider usage,
answer hashes, and unchanged response texts are retained. The output cap was 8,192 tokens; temperature was omitted.

The shipped .NET scorer evaluates all 31 checks against each response's text, including checks normally targeting
`evidence.md`. This measures whether the requested text was guessed, rather than whether an artifact was produced.
It is separate from the runner's deliverable score. The selected responses for the current prompts match **1 of 31 checks
(3.23%)**. No task matched all its checks; provider-reported cost for those ten responses was **$0.003285**.

The initial ten-prompt measurement matched two checks: `should_preprocess` and `rest-page-size: 100`. The emergency
prompt was subsequently changed to separate large reads; its revised prompt is measured separately, with its earlier
responses retained in `no-tools.json`'s `supersededResponses`. The binary prompt was also measured once after tuning its
read ranges and progress boundary. All four superseded responses are retained separately, rather than substituting them
into the final prompt's measurement. Read the selected manifest and prompt hashes from `no-tools.json`.

This is one sample per unchanged prompt, not an estimate of the model's total memorized knowledge. Four selected responses
contain simulated tool syntax and one contains only a reference code; these five do not attempt the requested factual
answer. Several substantive responses falsely claim verification and invent paths or implementation details. In particular,
the matching `should_preprocess` name appears with invented surrounding modules. A text match certifies only the declared
fact's textual presence, not the correctness of the explanation. All texts are retained verbatim with those limitations.

## Emergency probe tuning

The initial pilot used the original prompt with a 30-call allowance and four-call wrap-up window. Qwen put the tree call
and all four large reads in one response. The next prepare contained 41,061 estimated tokens, returned `CannotCompact`,
and stopped with `budget_exceeded` after one provider call. No message was dropped and no artifact or final answer existed.
The probe was invalid with `noAnswer`.

The second pilot required one call per response and split the 1,794-line `api_test.go` range into three ranges of at most
600 lines. It followed all seven opening tool calls in order. All three answer checks passed; three emergency truncations
dropped 22 messages. The opening instruction remained present in the last completed prepare, so its probe was invalid
with `instructionNotCompacted`, despite final canary presence. Those drops alone are insufficient retention evidence.

The current prompt retains that uninterrupted initial read sequence, then asks for one findings-only progress reply after
recording the requested facts. The continuation explores field parsing, HTTP transport, and pagination tests in bounded
ranges, allowing the original instruction to become eligible for compaction. Subsequent pilot verdicts are recorded without
treating an invalid probe as a pass.

The third pilot added a progress boundary but returned the canary in that progress reply. Its probe was invalid with
`canaryRepeated`; all three answer checks passed. The fourth prompt requests a fixed progress sentence without the code,
then ties the reference-code ending to a later runner message beginning `Stop live work for now.` It uses the shipped
100-call Large allowance and 12-call wrap-up window.

[`emergency-pilot-4.json`](emergency-pilot-4.json) is the first **valid** emergency-probe observation. All three answer
checks passed; the last completed prepare showed the original opening instruction absent and the run recorded dropped
messages. Its final answer omitted the reference code, so the probe **failed with `canaryMissing`**. That is a retention
failure, retained as measured. The earlier three invalid observations remain separate.

## Other retention pilots

[`retention-pilot.json`](retention-pilot.json) records the original preprocessor, binary, and clone pilots with the shipped
50/8 Medium and 100/12 Large allowances. Preprocessor and clone are valid passing probes; both final completed prepares
show the opening instruction absent. The binary task failed without a final answer and is invalid with `noAnswer`.
Its revised prompt bounds each source range to 600 lines, requests one call per response, and specifies a fixed progress
sentence without the reference code before the continuation.

[`binary-pilot.json`](binary-pilot.json) records that retry with the shipped 50-call allowance and eight-call wrap-up window.
It completed its protocol, and the last completed prepare showed the opening instruction absent. The valid probe failed
with `canaryMissing`. One of three answer checks passed; the notes omitted `BinaryMode::AsText`, and the final answer
omitted `BinaryDetection::quit`. These failures remain recorded. The four probes now each have a valid pilot observation:
preprocessor and clone passed, while binary and emergency failed. This is not a combined full-corpus baseline.

The preprocessor report records two of three answer checks passing. Its real notes use the correct module-qualified
`rg::flags::hiargs::preprocessor_globs`, which the original check rejected. The corpus check now accepts `rg::` as well as
`crate::`, while retaining the exact module and function boundary. The regression case failed before that check change
and passed afterward. The original report is preserved unchanged.

## Reviewed real-answer specimens

All **31 corpus checks** have independently authored positive labels in
[`ScorerCases.json`](../../../../tests/Codexplorer.Automation.Tests/Scoring/ScorerCases.json), with pinned-source citations,
provenance, and byte hashes. Six labels use actual runner answers/artifacts from the emergency and preprocessor pilots.
The remaining 25 use real provider answers generated from focused, line-numbered excerpts of the pinned checkout;
[`reference-answers.json`](reference-answers.json) records their generation identifiers, source ranges and hashes, request
hashes, provider usage, and method. These are scorer calibration specimens, separate from benchmark-run evidence.

One additional real answer labels the search-result ceiling as 100, confusing the page size with the 1,000-result cap.
It is retained with a negative verdict. A subsequent answer with the list commands' explicit warning text distinguishes
the ceiling correctly. The 31 positive labels and that negative label all pass the fixture checks.

Each verdict covers its declared path, symbol, or numeric fact. The raw answers contain additional claims and inaccurate
line citations; matching the declared fact does not certify all surrounding prose. Texts are copied without rewriting
after credential/private-path screening. The automation suite has 549 passing tests with zero warnings.

## Recording the full baseline

Review real, repository-backed correct-answer specimens for all 31 checks, including provenance and handwritten verdicts.
Freeze the manifest and use identical effective settings and turn budgets in both full runs, except for the context-window
override. Use call allowances of 12 Small, 48 Medium, and 64 Large, with wrap-up windows of 4, 8, and 8 respectively.
The Large allowance permits a continuation after the 50-call exchange cap. Record the 20,000-token treatment and
1,000,000-token control reports separately. Validate each report and compare
them with the existing comparison command. A control with compaction is invalid; a full-context control's retention probes
are expected to be invalid because its opening instructions survive.

Model-provider metadata retrieved during calibration advertises a one-million-token context window. Its price tiers increase
above 32,000 and 256,000 prompt tokens, so the cheapest input rate is insufficient to budget the control arm. Use recorded
provider input/output usage and the applicable tiers; distinguish estimates from actual provider-reported charges.
