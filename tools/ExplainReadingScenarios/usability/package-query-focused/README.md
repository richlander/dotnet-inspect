# Prefix preparation and focused HAL reading trials

One of four fresh agents reads the landed HAL comparison rule and prepares a
valid prefix query without query-help recovery. The other three choose `-Q`.
All four execute `depends starts-with Polly` with the requested dependency
scope and give supported, qualified package answers. The HAL reader still
views 18,199 B of explanation data. Neither recipient of the field-selection
hint chooses HAL, so this sample does not test that hint's reading benefit.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Supporting syntax contract: [Package Query term binding](../../../../docs/design/package-query-cli.md#package-query-term-binding).
Focused issue: [#9971](https://github.com/richlander/dotnet-inspect/issues/9971).
Overall theme: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Fixed design

Four fresh GPT-6 Sol/high participants use the same landed source, executable,
[task](package-task.txt), and [recording protocol](participant.txt). Readers 1
and 4 get the unchanged [prior guide](guide-prior.txt), 2,153 B. Readers 2 and
3 get that guide plus a 564 B [field-selection hint](reading-hint.txt), totaling
2,717 B. The hint names HAL state fields and canonical-key selection; it
supplies no task predicate, jq recipe, expected answer, or required explain
request. [Criteria](criteria.txt) and assignments are fixed before launch.
Two balanced waves run readers 1+2, then 3+4; assignments are not randomized.

Source is main `df4c7de7a7963b3b2e44c8146d0d341ccae3b73e`, after
[#9966](https://github.com/richlander/dotnet-inspect/pull/9966). The explicit
prefix task differs from the earlier exact-Polly task so the non-equality
operator is needed. Historical source, guide, task and live populations differ;
older studies are descriptive context, not a before/after intervention.

The real motivating asset remains Microsoft.Extensions.Http.Polly@10.0.12,
with Polly 7.2.4 and Polly.Extensions.Http 3.0.0 declarations in the
.NETStandard2.0 group selected for net10.0. SDK, executable, inputs, configs
and ten metadata probes are pinned in [dispatch](dispatch.json). All use live
nuget.org; search populations are not snapshotted. The preflight checks the
landed syntax and admission, without executing the participants' search.

The recorder and freeze wrapper reuse the existing command-bound
implementations unchanged. Product stdout is unchanged; recorded local jq or
text views are what agents read. All original answers, requests, responses,
views and events were frozen before any separate neutral question.

## Observed paths and bytes

Guide bytes are excluded from product totals and added separately in the last
column. Guidance includes help, query help and explanation failures;
inspection totals include completion warnings. Raw retrieval and local
rendering are different measurements.

| Reader | Guide | Guidance calls / raw B | HAL views B | Inspection calls / raw B | Product rendered B | Rendered B with guide |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | prior | 3 / 6,272 | 0 | 1 / 5,831 | 9,976 | 12,129 |
| 2 | focused | 3 / 6,272 | 0 | 3 / 21,455 | 14,671 | 17,388 |
| 3 | focused | 3 / 6,272 | 0 | 1 / 5,831 | 10,001 | 12,718 |
| 4 | prior | 3 / 22,129 | 18,199 | 1 / 5,831 | 25,262 | 27,415 |

All start with `package query --help`. Readers 1–3 then read `-Q`, followed by
`-Q Packages`. Reader 4 tries `--explain true --json`; `true` is interpreted as
a package argument and the command-only terminal rejects it. The already read
guide shows the bare gesture and says to omit the package argument. The reader
recovers with `--explain --json`, reads HAL and performs the prefix query
without `-Q`. Help nevertheless displays `--explain <explain>` although the
terminal admits no operand. [#9973](https://github.com/richlander/dotnet-inspect/issues/9973)
records that help/admission mismatch separately. The trace does not establish
that the placeholder caused the guess; the failed invocation is gesture
friction, not missing prefix-comparison registration.

The HAL reader first inspects root and embedded keys, including a guessed
`members` property, then prints all embedded facets and bindings (17,997 B in
that view). Its total HAL views are 18,199 B. The main facet/binding view omits
`data_scope`, although an earlier key view retained it. After reading the
corrected rule it executes the prefix spelling, follows no links, and fetches
no schema or package-detail resource.
Requested explanation and admitted HAL are separate: two primary requests,
one successful emission. Legacy `selected_hal_requests` counts root-selected
reads and is zero; `admitted_command_hal_requests` is one for reader 4.

There are no jq errors or reported truncation. Only reader 4 has a failed
product request. No root explain, companion, contract or link traversal occurs.
[Measurements](measurements.json) retain every category and total.

## Answers, work scope and stopping

All queries use the actual prefix operator and `dependency-target=net10.0`.
All four report Microsoft.Extensions.Http.Polly@10.0.12 and its two matching
declarations in the selected .NETStandard2.0 group. Every broad family search
hits `CandidateLimitReached` at 40/40; answers avoid family-wide exhaustion,
indirect traversal and application-restore claims, and distinguish inspected
versions from declared ranges. Different prefix spellings and live search
prevent an assertion of identical candidate populations.

Readers 1, 3 and 4 each execute one 40-candidate family query. Reader 2 executes
three queries: the 40-candidate family search, a four-candidate Http prefix
rescan, and a one-candidate Resilience prefix search. That is 45 candidate
evaluations, not necessarily 45 distinct packages. The Http rescan repeats
matching package/group facts already present in its first response. The last
search adds Microsoft.Extensions.Resilience@10.10.0: the selected net10.0
group declares Polly.Extensions and Polly.RateLimiting, each with range 8.4.2.
Neither narrower scope establishes family completeness.

The protocol explicitly permits at most 40 candidates **per query**, while the
task says to execute a search of at most 40 candidates. This leaves cumulative
work ambiguous. The extra operations are retained and costed; this report does
not claim reader 2 stays within a cumulative 40-evaluation budget. Future
work-budget trials need an explicit cumulative rule.

The [assessment](assessment.json) separates supported answer rows, requested
search semantics, work/stopping behavior and available guidance. Three readers
stop after one sufficient result; reader 2 spends two more requests, with one
additional package but also repeated evidence. A correct final answer does
not establish that every operation was necessary.

## Neutral retrospective

The unchanged [question](post-task-question.txt) is asked only after all four
originals are frozen. Accounts from readers 1–3 say query help supplied the
prefix operator and dependency scope. Reader 4 says command explanation
supplied those facts after correcting its first invocation. Readers 1, 3 and
4 describe stopping once the bounded result answered the requested fields.
Reader 2 groups both narrower searches as attempts to find additional matches;
that account does not establish that the Http rescan supplied a missing fact.
These are self-reports, not causal explanations.

The evidence shows one usable HAL preparation path with no `-Q` recovery, and
three effective query-help paths. It does not show reliable explain adoption,
that the syntax correction caused less recovery, or that the field-selection
hint reduces reading. Both hint recipients chose another entrance, and only
one prior-guide reader chose HAL. No format or guide-efficiency ranking follows.

## Replay and boundaries

```bash
python3 tools/ExplainReadingScenarios/usability/package-query-focused/verify.py
```

The final manifest pins the complete archives, manual assessment and replay
helper. Replay reuses existing measurement helpers and checks input hashes,
preflight, assigned guide bytes, original frozen inventories, raw output
identity, jq/error reproduction, serialized read order, retrospective
chronology and arithmetic including guide overhead. It is a standalone
observation verifier, not a security sandbox, automatic answer scorer or CI
regression gate. Captured guides are archived as byte-identical `guide.txt`;
the original recorder read `guide.md`. The helper was written after dispatch
and could not influence participant guidance.

CLI bytes are not NuGet download size, tokens, delivered context, latency or
production transfer. This four-participant nonrandomized diagnostic sample
cannot establish causal or population effects. NativeAOT pins the observed
product, but no runtime performance comparison is claimed.

This is one evidence slice for the existing ExplainReadingScenarios harness.
All six planned production slices are already landed. No product substrate,
host gesture, renderer, shipped skill, architecture migration or retirement
changes here; any later guide/product correction needs its own scope.
