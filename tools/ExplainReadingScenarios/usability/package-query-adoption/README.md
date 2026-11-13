# Landed Package Query explanation trials

Three of four fresh agents use the landed command-level HAL explanation and
all four produce supported bounded direct-dependency answers. None follows a
link or fetches a contract. The entrance is usable, but local reading remains
broad: HAL readers retrieve 21.9–24.9 KB of guidance versus 6.3 KB for the
agent using `-Q`. This is observed adoption with a reading-cost tradeoff,
not evidence that HAL or the new entrance is more efficient.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Supporting entrance contract: [Package Query installed explanation](../../../../docs/design/package-query-cli.md#installed-capability-explanation).
Focused issue: [#9961](https://github.com/richlander/dotnet-inspect/issues/9961).
Overall theme: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Fixed design

Four fresh GPT-6 Sol/high participants receive the same
[task](package-task.txt), [guide](guide.txt), and [protocol](participant.txt).
The task and neutral post-task question are unchanged from the
[command-bound trials](../command-bound/README.md). The guide updates Package
Query admission and teaches completeness-aware local HAL selection, without
task predicates or jq recipes. The existing low-latency policy forbids
verification-only requests. [Criteria](criteria.txt) are fixed before dispatch.

The source is main `813406e7fa0c023fd2845c64c00c56d17a958b4d`.
SDK, executable, inputs, assignments, and preflight responses are pinned before
participants start. All use the same executable and live nuget.org population.
The motivating retained asset is Microsoft.Extensions.Http.Polly@10.0.12;
fresh inspections may observe different current versions and candidate sets.
Historical comparisons are descriptive, not controlled interventions.

The [recorder](runner.py) and [freeze wrapper](freeze.py) reuse the existing
command-bound implementations unchanged. CLI stdout is preserved, and agents
read it through recorded jq or text views. Guidance and ordinary inspection
bytes are separate; requested syntax is not proof a projection was admitted.
This cooperative protocol is not a security sandbox or production transport.

Every original answer, request, response, and view is frozen before the
separate, unchanged [neutral question](post-task-question.txt). Retrospective
answers are self-reports and do not alter the initial trial results.

## Observed requests and reading

The [measurements](measurements.json) separate raw CLI retrieval and local
rendering. All four read the 2,153 B common guide, excluded from product totals.
Guidance includes command help, query help, explain, and failures in those
operations. Inspection stderr includes candidate-limit diagnostics.

| Participant | Guidance calls / retrieved B | HAL views B | Inspection calls / retrieved B | Total calls | Total rendered B |
| --- | --- | --- | --- | --- | --- |
| 1 | 2 / 21,913 | 15,384 | 1 / 5,769 | 3 | 22,305 |
| 2 | 2 / 21,913 | 9,762 | 1 / 5,769 | 3 | 16,708 |
| 3 | 4 / 24,922 | 18,045 | 1 / 5,831 | 5 | 28,154 |
| 4 | 3 / 6,272 | 0 | 2 / 5,971 | 5 | 10,224 |

Participants 1–3 all choose command help followed by `package query --explain`.
They use local jq to inspect keys, facet inventories, and bindings. Some
initial filters guess `cli`, `options`, or `syntax`, producing null fields;
later reads expose the actual `input_rules`. Facet selection uses display
names rather than canonical `key` values. No root explain, schema/contract,
link traversal, companion, or separate package-detail verification occurs.

Participant 3 reads nearly all embedded data, then tries `-Q depends`, which
fails because `-Q` expects a section. `-Q Packages` provides the concrete
non-equality spelling, and the agent executes `depends starts-with Polly`.
Its final answer is supported by an exact Polly manifest declaration in the
returned package, but the prefix predicate is broader than exact Polly
identity. A malformed jq expression is retained as a recorded error.

Participant 4 uses brief `-Q` and `-Q Packages` without explain. It then tries
depth four with 40 candidates, although the already read guidance states the
five-candidate nuspec-expensive cap. The product rejects that query; the agent
removes depth and executes a valid direct query. This is failure to apply
present guidance, not a missing explain rule.

All four find Microsoft.Extensions.Http.Polly@10.0.12 with a direct Polly
declaration of `7.2.4` in `.NETStandard2.0`, selected for `net10.0`. The returned
summary and `CandidateLimitReached` warning qualify the 40-candidate search.
Answers distinguish declared ranges from inspected package versions and do
not claim indirect traversal, an application restore graph, or family-wide
exhaustion. Different prefix spelling and live search prevent a claim of
identical candidate sets despite the common observed match.

The [assessment](assessment.json) is a manual fixed-criteria domain and
friction assessment, separate from the byte/hash replay. Three explain uses
are actual successful HAL emissions; the legacy `selected_hal_requests`
counter refers only to selected **root** explain and is zero here.

## Neutral retrospective and next questions

Original files in all four trials were frozen before any separate question.
Retrospectives describe help for parser syntax, HAL for facet semantics, and
ordinary output for package/framework/coverage facts. Participants 1–2 say
another package read would repeat manifest evidence; participant 3 attributes
its `-Q` recovery to precise predicate syntax. Their descriptions are
consistent with recorded request order but do not establish necessity or
causality. Participant 4's account acknowledges the rejected depth request.

The registered HAL binding supplies `--where key=value`, operator identities
and semantic rules, but does not spell out `key starts-with value`. `-Q
Packages` does. [#9962](https://github.com/richlander/dotnet-inspect/issues/9962)
preserves that narrow CLI knowledge-registration follow-up. A non-equality
predicate was not required for the exact Polly question, so the observation
alone does not prove every recovery request was necessary.

The sample addresses the earlier graph-eagerness concern: these HAL users
answer from embedded data and stop after the needed inspection. The remaining
cost lies in broad local inventories, repeated fields/links, and discovering
the state field names. It does not decide HAL versus an unfamiliar direct
model: only HAL was offered, and no necessary-navigation task was exercised.

## Worked local read

This author recipe is generated **after** the trials from the pinned preflight
response. It was not given to participants. It selects the three dependency
facets by canonical key, keeps their semantic rules and values, and reads only
the binding's gesture/input rules:

```bash
dotnet-inspect package query --explain > query.hal.json
jq -c -f worked-selection.jq query.hal.json
```

The [filter](worked-selection.jq) reproduces
[worked-selection.json](worked-selection.json), 2,631 B versus the full
18,649 B dataset. `data_scope` retains the original dataset's completeness;
this local subset does not become the complete facet inventory. It needs no
link request for these facts. This illustrates a smaller reading shape,
not measured participant behavior or an intervention result.

## Replay

```bash
python3 tools/ExplainReadingScenarios/usability/package-query-adoption/verify.py
```

[Dispatch](dispatch.json) pins inputs before launch; the final manifest pins
replay code, assessments, and complete trial artifacts. The replay verifies
preflight identity, unaltered stdout, frozen original answers, copied common
guides/configs, recorded jq including the error, serialized read-before-request
order, retrospective question identity, inventories, and byte arithmetic.
It is a standalone evidence verifier, not a CI regression or answer scorer.
Archived participant guides use `guide.txt`; their bytes match the original
`guide.md` files used by the recorder, without adding a Markdown heading.

## Evidence boundaries

Assess answer correctness, available guidance, navigation/friction, and stated
stopping decisions separately. CLI retrieved bytes and local rendered bytes
are different quantities. Neither is a NuGet download size, token count,
latency, delivered-context, or production-transfer measurement. This small
nonrandomized sample cannot establish causal or population effects.

This is one diagnostic evidence slice. The CLI entrance and original shared
CLI/Browser projection are already adopted; no new product substrate,
architecture migration, or retirement is introduced. The routinely used
ExplainReadingScenarios harness is the consumer. Any resulting product or
skill correction must be scoped separately. No shipped skill is edited here.
