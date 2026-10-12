# HAL stopping-rule reading trials

Two guide variants test answering from local data before navigating. The first
local-data-first instruction produced mixed results. A stronger version adds
the user's explicit low-latency preference and prohibition on verification-only
requests. All answers remain correct, but both strong-guide Style readers still
fetch the schema; one explicitly acknowledges it supplied no needed fact.
The stronger wording does not establish reliable adherence. Neutral questions
asked only after freezing each trial help distinguish confirmation from
semantic uncertainty without changing its completed results.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9896](https://github.com/richlander/dotnet-inspect/issues/9896).
Parent comparison: [minimal-instruction trials](../minimal/README.md),
[PR #9884](https://github.com/richlander/dotnet-inspect/pull/9884).
Theme tracker: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Protocol

The [baseline HAL guide](guide-baseline.txt) is the earlier 685 B guide unchanged.
The [experimental guide](guide-stop.txt) is 839 B and adds only this instruction:

> Answer from the current resource and its embedded resources first. Follow a
> link only when you can identify a specific missing fact needed for the task.

The [participant protocol](participant.txt), [Query task](query-task.txt),
[Style task](style-task.txt), and [assessment criteria](assessment-criteria.txt)
are unchanged. Four fresh GPT-6 Sol/high participants, with no prior conversation
or results, each received one task and the experimental guide: two Query and two
Style. The earlier HAL participants are the historical comparison group, not
repeated measurements of these participants. Assignment is nonrandomized;
there is no contemporaneous baseline group for the primary tasks.

Two additional fresh participants received the separate
[navigation boundary task](navigation-task.txt): one baseline guide and one
experimental guide. They start at the narrow library-literal facet, then need
dependency depth/target facts absent from that closure. Every later request must
use an already returned href unchanged, without search or constructed paths.
The [boundary criteria](navigation-criteria.txt) were fixed before dispatch.
This deliberately foregrounds navigation and is assessed separately from the
primary tasks; it is not a representative agent workflow or a general
regression guarantee.

All participants used the unchanged [recorder](../skill-informed/runner.py),
their own jq, and cooperative restrictions on source, skills, network, other
trials, and inspection execution. No task answer, schema coaching, or post-task
question was supplied during the original trial. [dispatch.json](dispatch.json)
pins the original inputs. Guide byte counts exclude the common protocol,
task, and tool framing.

The frozen source is `536ce35bccbd6baa56d592fc6a8e944afd836d30`, reporting
`0.27.0+536ce35`, built in Release with SDK `11.0.100-rc.1.26425.128`.
The apphost hash matches the baseline; the rebuilt DLL hash differs and is
recorded separately. All 51 distinct earlier HAL requests, including discovery
and explicit contracts, reproduced byte for byte. Binary hashes remained
unchanged after the new trials. [manifest.json](manifest.json) pins the source,
binaries, inputs, responses, frozen answers/logs and retrospective responses.
No product, shipped skill, HAL lowering, or discovery behavior changes here.

## Results

These are independent participant groups; matching row numbers do not mean
before/after runs by the same agent.

| Task / guide / participant | Requests | Retrieved B | Product rendered B | Contracts | Assessment |
| --- | ---: | ---: | ---: | ---: | --- |
| Query / earlier baseline / 1 | 26 | 90,158 | 56,332 | 0 | Satisfied |
| Query / earlier baseline / 2 | 26 | 185,248 | 56,896 | 2 | Satisfied |
| Query / stopping rule / 1 | 19 | 67,518 | 54,522 | 0 | Satisfied |
| Query / stopping rule / 2 | 19 | 71,913 | 49,339 | 0 | Satisfied |
| Style / earlier baseline / 1 | 8 | 121,069 | 42,905 | 1 | Partial |
| Style / earlier baseline / 2 | 10 | 118,524 | 25,549 | 1 | Partial |
| Style / stopping rule / 1 | 9 | 117,064 | 37,833 | 1 | Satisfied |
| Style / stopping rule / 2 | 7 | 116,771 | 52,095 | 1 | Satisfied |

Query totals fell from 275,406 B to 139,431 B (49.4%); rendered totals fell
from 113,228 B to 103,861 B (8.3%). Style totals fell from 239,593 B to
233,835 B (2.4%), while rendered totals rose from 68,454 B to 89,928 B (31.4%).
The new Style answers correctly recognize already embedded tiers, resolving the
earlier fetch-necessity error. Both still fetched standalone tiers and a
95,618 B schema contract. Correct closure recognition did not eliminate
corroborative reads. Both new Query agents also retrieved individual facets
and a CLI binding already available in a query-space closure.

| Boundary guide | Requests | Retrieved B | Product rendered B | Href requests | Assessment |
| --- | ---: | ---: | ---: | ---: | --- |
| Baseline | 10 | 37,114 | 27,755 | 9 | Satisfied |
| Stopping rule | 8 | 26,968 | 26,888 | 7 | Satisfied |

Both boundary agents obtained the required facts through the emitted CLI
binding and dependency-facet links. That binding advertises ordinary JSON
resource links without `projection=hal`: subsequent facets use compact ordinary
explain resources. Participants call them full contracts, but these are not
explicit `.contract` or `projection=contract` requests. The boundary preserves
necessary navigation; it does not establish uninterrupted HAL mode.

All initial six CLI trials succeeded; they had no jq errors or identical
repeated payloads. Different resources still overlap. [measurements.json](measurements.json)
retains request/view counts, hrefs, discovery splits, and guide emissions;
[assessment.json](assessment.json) retains manual obligation-level assessment
with trace provenance. Every primary answer preserves unavailable/incomplete
qualifications and unexecuted-result uncertainty. Product views were reported
untruncated; three optional local answer-validation displays were reported
truncated. Rendering receipts alone do not prove UI delivery.

## Explicit value-judgement follow-up

The operator observed that an agent can recognize a read is unnecessary and
still perform it for verification. The [strong guide](guide-strong.txt) adds
only this [policy](verification-policy.txt) to the prior stopping-rule guide:

> The user values low-latency operations and only permits following link
> relations if they are necessary to answer a question. Verification-only
> requests harm latency and are not permitted.

The guide is 1,028 B. Four more fresh GPT-6 Sol/high participants receive the
unchanged Query/Style protocol and tasks, plus one fresh participant with the
same necessary-navigation boundary task. [dispatch-strong.json](dispatch-strong.json)
pins these inputs before dispatch. The [additional criteria](strong-assessment-criteria.txt)
separate domain correctness from policy adherence: zero schema requests do not
prove compliance, and a genuinely missing schema meaning can justify a read.
The exact source, SDK, apphost and DLL hashes match the earlier cohort, and
remained unchanged afterward. All original six trials, reflections and fixed
inputs are retained unchanged; only the verifier extends to the new cohort.
These are independent, nonrandomized participants, not repeated runs or a
causal test. The policy states a user preference, not a measured latency claim.

| Strong-guide task / participant | Requests | Retrieved B | Product rendered B | jq errors | Schemas | Domain assessment |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Query / 1 | 13 | 56,543 | 28,696 | 0 | 0 | Satisfied |
| Query / 2 | 13 | 64,787 | 49,730 | 0 | 0 | Satisfied |
| Style / 1 | 5 | 113,382 | 33,813 | 1 | 1 | Satisfied |
| Style / 2 | 6 | 116,625 | 41,980 | 4 | 1 | Satisfied |
| Navigation boundary | 6 | 22,568 | 21,016 | 0 | 0 | Satisfied |

Compared with the prior stopping-rule cohort, Query requests fall from 38 to
26 and retrieval from 139,431 B to 121,330 B (13.0%); rendered totals fall
from 103,861 B to 78,426 B (24.5%). Style requests fall from 16 to 11, but
retrieval falls only from 233,835 B to 230,007 B (1.6%), with both 95,618 B
schemas still fetched. Style rendering falls from 89,928 B to 75,793 B (15.7%).
All five trials' CLI requests succeed with no identical repeated payload. Their
five jq errors and corrected filters remain in the evidence. Query 1 reports
one unquoted-href shell failure before the recorder ran; it is not a CLI
receipt or product retrieval. No strong-cohort tool rendering was reported
truncated. The boundary still obtains the required facts through advertised
hrefs; fewer requests do not establish that each was minimal.

Policy adherence is weaker than domain correctness:

- Query 1 re-fetches library-target after its query-space view already exposes
  that context. It says the embedded target lacked detail, but excluding
  navigation/scope metadata leaves identical core fields in the two records.
- Query 2 re-fetches literal and depends after rendering their rules from the
  query-space. Its retrospective calls these confirmation. Neither Query
  reader requests an explicit schema; both still repeat local data.
- Style 1 avoids a standalone tier fetch, but asks for the schema after complete
  maps/sets and embedded tiers were rendered. It describes clarifying map
  coverage and state semantics. That can represent semantic uncertainty,
  rather than a proven verification-only motive; adherence is not established.
- Style 2 requests choices and tiers before inspecting either, then reads the
  schema after viewing complete maps and embedded tiers. Its frozen answer and
  retrospective explicitly say the extra reads were unnecessary, and the
  schema supplied no needed fact. The explicit restriction was not followed.

Thus the value judgement does not reliably eliminate the observed behavior.
It is useful to retain the cost objective, but the traces do not justify
shipping this wording as a solved stopping policy. A next experiment can ask
for a concrete missing-fact statement before each request and check whether
that fact is already local. This would be an additional intervention, not a
retrospective question or a claimed result of this study.

Strong-cohort answers and separate reflections:
[Query 1](query-strong-1/answer.json), [reflection](query-strong-1/post-task.json);
[Query 2](query-strong-2/answer.json), [reflection](query-strong-2/post-task.json);
[Style 1](style-strong-1/answer.json), [reflection](style-strong-1/post-task.json);
[Style 2](style-strong-2/answer.json), [reflection](style-strong-2/post-task.json);
[boundary](navigation-strong-1/answer.json), [reflection](navigation-strong-1/post-task.json).

## Ask after completion

Each original answer, request log, response, and rendered view was frozen before
asking the [same question](post-task-question.txt):

> Looking back at your completed task, how did you decide what to read or request
> next, and when you had enough information to answer? Give a brief account of
> observable choices and the information you expected further reads to provide.

Participants answered from their existing conversation only. They were told
not to make further product reads, read new files/responses, revise answers, or
change logs. The question was not announced beforehand. All eleven new trials retain
`frozen.json` and a separate `post-task.json`; all original files remained
unchanged. The four earlier HAL participants answered the same question later,
with the committed parent manifest supplying their pre-question identities.
Their scratch originals were checked against those identities afterward. Their
[feedback](baseline-feedback/) is separate from parent results.
Earlier feedback was delayed until this successor session; new feedback was
requested immediately after completion. That difference limits comparisons of
recall. Retrospective content is excluded from retrieval/rendering totals.

The question helped distinguish three motivations:

- Earlier Query HAL 2 describes looking for missing normalization and
  unavailable-data rules. Those are task-specific uncertainties, not simply
  automatic traversal of every link.
- Both initial stopping-rule Style participants say their local choice resource supplied the
  needed facts and that tier/contract reads corroborated them. The logs still
  show those reads, including contract requests after embedded tiers were
  rendered. Their recognition is useful; it is not evidence of stopping early.
- Both boundary participants describe following the binding to find the
  missing dependency rules. Their traces show those advertised hrefs and facts.

The same question was used for the strong-guide cohort after each completed
answer and trace was frozen, with no product requests, views or corrections
allowed during the feedback phase. All five originals remained unchanged.

These are brief self-reports about observable choices, not hidden reasoning or
proof of motivation. For example, Style 1 describes opening the tier resource
once the choice resource was sufficient, but its standalone tier request and
view precede the substantive choice views. Read ordering should come from the
trace; the retrospective helps interpret it, and cannot repair the answer or
rewrite the result.

Retained primary answers and reflections:
[Query 1](query-stop-1/answer.json), [reflection](query-stop-1/post-task.json);
[Query 2](query-stop-2/answer.json), [reflection](query-stop-2/post-task.json);
[Style 1](style-stop-1/answer.json), [reflection](style-stop-1/post-task.json);
[Style 2](style-stop-2/answer.json), [reflection](style-stop-2/post-task.json).
Boundary answers and reflections:
[baseline](navigation-baseline-1/answer.json), [reflection](navigation-baseline-1/post-task.json);
[stopping rule](navigation-stop-1/answer.json), [reflection](navigation-stop-1/post-task.json).

## Interpretation and reproduction

The sample supports trying a local-data-first rule but does not establish a
causal instruction effect, a format winner, or a general efficiency gain.
Query savings coexist with redundant facet reads; Style verification remains
expensive. A next focused experiment could require a concrete missing fact
before corroborative retrieval, including a check that it is absent from the
local closure. This evidence does not justify removing links or shipping a
stronger rule without testing that it preserves needed schema reads.

Counts are UTF-8 CLI stdout/stderr and recorder emissions, not tokens, timing,
reasoning effort, or delivered context. Replay checks integrity and byte counts;
it does not score answer semantics or promise future participant behavior.
With Python 3 and jq, run from the repository root:

```bash
python3 tools/ExplainReadingScenarios/usability/hal-stop/verify.py
```

For a fresh trial, use the fixed prompt, assigned guide, matching CLI/SDK config,
and one task with the existing recorder. After its final answer and before
asking the question, freeze the trial:

```bash
python3 tools/ExplainReadingScenarios/usability/hal-stop/freeze.py /tmp/trial
# Ask the separate post-task question without further reads or corrections.
python3 tools/ExplainReadingScenarios/usability/hal-stop/freeze.py /tmp/trial --check
```

The verifier replays successful jq views, verifies retained error text, fixed
inputs, question identity, frozen original hashes, response hashes and exact
measurements. It also verifies that every boundary request after the specified
start uses an earlier advertised href verbatim. Empty streams and successful
views are reconstructed. `freeze.py --check` checks the complete original
scratch inventory, including additional recordings after the freeze.

The Release build passed with zero warnings/errors. Independent selected-data
checks passed all 14 reading shapes, registered rules and preparation
equivalences, plus 49 HAL hrefs. A copied evidence directory with a same-length
response mutation failed hash verification. Markdown and whitespace checks
passed for this evidence-only slice.
